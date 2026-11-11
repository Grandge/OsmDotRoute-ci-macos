using OsmDotRoute.Internal.Odrg;

namespace OsmDotRoute.Native;

/// <summary>
/// `.odrg` の SPATIAL_INDEX セクション (STR pack R-tree) に対する範囲クエリ・最近傍クエリを提供する
/// （Phase 3 ステップ 3A.4、計画書 §4.4-B）。
/// </summary>
/// <remarks>
/// <para>
/// 入出力はすべて <see cref="OdrgBbox"/> (Lon-Lat 順) を採用（計画書 §2.10 Q1）。
/// 入力ノード列は <see cref="NativeRoadGraph.GetRTreeNodes"/> から得る MMF 上のゼロコピー Span を想定。
/// </para>
/// <para>
/// 距離計算は経緯度の 2D euclidean (度単位)。R-tree の枝刈り規約と一致するため、
/// Brute-force（全 EDGE_AABB 走査）と決定的に同一の k 候補集合を返す（計画書 §2.10 Q5）。
/// </para>
/// </remarks>
internal static class NativeRTreeQuery
{
    /// <summary>
    /// 指定 bbox と交差する全エッジ ID を <paramref name="resultBuffer"/> に書き込み、ヒット総数を返す。
    /// </summary>
    /// <remarks>
    /// R-tree のリーフノード AABB は子エッジ AABB の外接であるため、ノード AABB がクエリと交差しても
    /// 個々のエッジ AABB は交差しない可能性がある（false positive）。これを排除するため、リーフ展開時に
    /// <paramref name="edgeAabbs"/> でエッジ単位の交差判定を再度行い、Brute-force と完全一致する true-positive
    /// 集合のみを返す（計画書 §4.4-C テスト 4）。
    /// </remarks>
    /// <param name="nodes">R-tree ノード列（<see cref="NativeRoadGraph.GetRTreeNodes"/> 経由）。</param>
    /// <param name="rootIndex">ルートノードインデックス。</param>
    /// <param name="edgeAabbs">EDGE_AABB セクション全体（エッジ ID 添字）。</param>
    /// <param name="queryBox">クエリ bbox (Lon-Lat 順)。</param>
    /// <param name="resultBuffer">ヒットエッジ ID の書込先。<see cref="Span{T}.Length"/> までで切詰。</param>
    /// <returns>
    /// ヒット総数。戻り値 &gt; <c>resultBuffer.Length</c> の場合は overrun
    /// （<c>resultBuffer.Length</c> 件のみ書込済、計画書 §2.10 Q4）。
    /// </returns>
    public static int Query(
        ReadOnlySpan<OdrgRTreeNode> nodes,
        uint rootIndex,
        ReadOnlySpan<OdrgBbox> edgeAabbs,
        in OdrgBbox queryBox,
        Span<uint> resultBuffer)
    {
        if (nodes.Length == 0) return 0;

        int totalHits = 0;
        int writeCount = 0;

        // 明示スタック (深さ ≒ log_M(N) なので 64 で全データ規模に対応)
        Span<uint> stack = stackalloc uint[64];
        int sp = 0;
        stack[sp++] = rootIndex;

        while (sp > 0)
        {
            uint nodeIdx = stack[--sp];
            ref readonly var node = ref nodes[(int)nodeIdx];

            if (!BboxIntersects(node.Bbox, queryBox)) continue;

            if ((node.Flags & OdrgRTreeFlags.LeafBit) != 0)
            {
                uint first = node.FirstChildIndex;
                uint count = node.ChildCount;
                for (uint i = 0; i < count; i++)
                {
                    uint edgeId = first + i;
                    if (!BboxIntersects(edgeAabbs[(int)edgeId], queryBox)) continue;
                    if (writeCount < resultBuffer.Length)
                    {
                        resultBuffer[writeCount++] = edgeId;
                    }
                    totalHits++;
                }
            }
            else
            {
                uint first = node.FirstChildIndex;
                uint count = node.ChildCount;
                for (uint i = 0; i < count; i++)
                {
                    if (sp >= stack.Length)
                    {
                        throw new InvalidOperationException(
                            $"R-tree DFS stack overflow (depth>{stack.Length}). Unexpected tree shape.");
                    }
                    stack[sp++] = first + i;
                }
            }
        }

        return totalHits;
    }

    /// <summary>
    /// 指定座標に最も近い <paramref name="k"/> 件のエッジ ID を点-EDGE_AABB 最小距離順に
    /// <paramref name="resultBuffer"/> に書き込み、書込件数を返す。
    /// </summary>
    /// <remarks>
    /// リーフ展開時に <paramref name="edgeAabbs"/> を直接参照してエッジ単位の距離を計算するため、
    /// 全 EDGE_AABB 走査 (Brute-force) と決定的に同一の k 件集合を返す（同距離タイは除く）。
    /// </remarks>
    /// <param name="nodes">R-tree ノード列。</param>
    /// <param name="rootIndex">ルートノードインデックス。</param>
    /// <param name="edgeAabbs">EDGE_AABB セクション全体（エッジ ID 添字）。</param>
    /// <param name="lon">クエリ点の経度。</param>
    /// <param name="lat">クエリ点の緯度。</param>
    /// <param name="k">取得する最近傍件数。</param>
    /// <param name="resultBuffer">書込先（距離昇順）。</param>
    /// <returns>実際に書き込んだ件数（<c>Min(k, ヒット可能数, resultBuffer.Length)</c>）。</returns>
    public static int Nearest(
        ReadOnlySpan<OdrgRTreeNode> nodes,
        uint rootIndex,
        ReadOnlySpan<OdrgBbox> edgeAabbs,
        double lon,
        double lat,
        int k,
        Span<uint> resultBuffer)
    {
        if (nodes.Length == 0 || k <= 0 || resultBuffer.Length == 0) return 0;
        int effectiveK = Math.Min(k, resultBuffer.Length);

        // 展開待ちノードを点-AABB 最小距離² 昇順で取り出す min-heap
        var nodeQueue = new PriorityQueue<uint, double>();
        nodeQueue.Enqueue(rootIndex, PointBboxDistanceSq(nodes[(int)rootIndex].Bbox, lon, lat));

        // 結果ヒープ: サイズ effectiveK の max-heap (距離² 降順 = 負の優先度)
        var resultHeap = new PriorityQueue<uint, double>();

        while (nodeQueue.TryDequeue(out var nodeIdx, out var nodeDistSq))
        {
            // 結果ヒープが満員で現ノード最小距離 ≥ 現状ワーストなら以降全て棄却 (best-first 性質)
            if (resultHeap.Count >= effectiveK)
            {
                resultHeap.TryPeek(out _, out var worstNegDistSq);
                double worstDistSq = -worstNegDistSq;
                if (nodeDistSq >= worstDistSq) break;
            }

            ref readonly var node = ref nodes[(int)nodeIdx];
            if ((node.Flags & OdrgRTreeFlags.LeafBit) != 0)
            {
                // リーフ展開: 子エッジ AABB を直接読んでエッジ単位の距離で評価
                uint first = node.FirstChildIndex;
                uint count = node.ChildCount;
                for (uint i = 0; i < count; i++)
                {
                    uint edgeId = first + i;
                    double edgeDistSq = PointBboxDistanceSq(edgeAabbs[(int)edgeId], lon, lat);
                    PushResult(resultHeap, edgeId, edgeDistSq, effectiveK);
                }
            }
            else
            {
                uint first = node.FirstChildIndex;
                uint count = node.ChildCount;
                for (uint i = 0; i < count; i++)
                {
                    uint childIdx = first + i;
                    double childDistSq = PointBboxDistanceSq(nodes[(int)childIdx].Bbox, lon, lat);
                    nodeQueue.Enqueue(childIdx, childDistSq);
                }
            }
        }

        // 結果ヒープ (max-heap) を一度配列に取り出し、距離² 昇順に反転して出力
        int writeCount = resultHeap.Count;
        var temp = new uint[writeCount];
        for (int i = 0; i < writeCount; i++)
        {
            resultHeap.TryDequeue(out var id, out _);
            temp[i] = id;
        }
        // temp は max-heap pop 順 = 距離² 降順 → 逆順詰込で昇順
        for (int i = 0; i < writeCount; i++)
        {
            resultBuffer[i] = temp[writeCount - 1 - i];
        }
        return writeCount;
    }

    /// <summary>
    /// 2 つの bbox が交差（境界接触含む）するかを Lon-Lat 平面で判定する。
    /// </summary>
    private static bool BboxIntersects(in OdrgBbox a, in OdrgBbox b)
    {
        if (a.MaxLon < b.MinLon) return false;
        if (a.MinLon > b.MaxLon) return false;
        if (a.MaxLat < b.MinLat) return false;
        if (a.MinLat > b.MaxLat) return false;
        return true;
    }

    /// <summary>
    /// 点 (<paramref name="lon"/>, <paramref name="lat"/>) と AABB の最小距離の二乗（経緯度 2D euclidean、度²単位）。
    /// 点が AABB 内なら 0、外なら矩形境界への二乗距離。
    /// </summary>
    private static double PointBboxDistanceSq(in OdrgBbox box, double lon, double lat)
    {
        double dx = 0.0;
        if (lon < box.MinLon) dx = box.MinLon - lon;
        else if (lon > box.MaxLon) dx = lon - box.MaxLon;

        double dy = 0.0;
        if (lat < box.MinLat) dy = box.MinLat - lat;
        else if (lat > box.MaxLat) dy = lat - box.MaxLat;

        return dx * dx + dy * dy;
    }

    /// <summary>
    /// 結果 max-heap（サイズ k 制限、距離² 降順 = 負の優先度）に候補を挿入する。
    /// </summary>
    private static void PushResult(
        PriorityQueue<uint, double> heap,
        uint edgeId,
        double distSq,
        int k)
    {
        if (heap.Count < k)
        {
            heap.Enqueue(edgeId, -distSq);
        }
        else
        {
            heap.TryPeek(out _, out var worstNegDistSq);
            double worstDistSq = -worstNegDistSq;
            if (distSq < worstDistSq)
            {
                heap.Dequeue();
                heap.Enqueue(edgeId, -distSq);
            }
        }
    }
}

/// <summary>
/// <see cref="OdrgRTreeNode.Flags"/> のビット定義（仕様書 §4.6 R-tree ノード）。
/// </summary>
internal static class OdrgRTreeFlags
{
    /// <summary>葉ノード判定ビット（bit 0）。書出側 <c>OsmDotRoute.Extractor.Pipeline.RTreeNode.LeafFlagBit</c> と一致。</summary>
    public const uint LeafBit = 1u << 0;
}
