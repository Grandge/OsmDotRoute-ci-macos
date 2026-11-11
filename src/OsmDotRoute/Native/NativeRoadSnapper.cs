using OsmDotRoute.Geometry;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Routing;

namespace OsmDotRoute.Native;

/// <summary>
/// <c>.odrg</c> ベースの <see cref="IRoadSnapper"/> 実装（Phase 3 ステップ 3A.5b、計画書 §4.5.2）。
/// </summary>
/// <remarks>
/// <para>
/// R-tree (<see cref="NativeRTreeQuery"/>) で候補エッジを bbox 絞り込みし、各候補エッジの
/// 完全シェイプ (From 頂点 + 中間シェイプ + To 頂点) に対し <see cref="GeoMath.PointToSegment"/>
/// で点-線分最短距離を計算してグローバル最短エッジを選ぶ。
/// </para>
/// <para>
/// 通行可否は <see cref="NativeRoadGraph.CanPass"/> で BAKED_PROFILE.Flags 直読
/// （計画書 §2.12 Q2、ProfileEvaluator 非依存）。
/// </para>
/// </remarks>
internal sealed class NativeRoadSnapper : IRoadSnapper
{
    private readonly NativeRoadGraph _graph;
    private uint[] _queryBuffer = new uint[1024];

    public NativeRoadSnapper(NativeRoadGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    /// <inheritdoc/>
    public SnapResult? Snap(string profileName, GeoCoordinate point, float searchDistanceM)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return null;
        if (searchDistanceM <= 0f) return null;
        if (!_graph.HasProfile(profileName)) return null;

        var (dLat, dLon) = GeoMath.MetersToBboxDegrees(searchDistanceM, point.Latitude);
        var qbox = new OdrgBbox(
            point.Longitude - dLon,
            point.Latitude - dLat,
            point.Longitude + dLon,
            point.Latitude + dLat);

        // R-tree クエリ (overrun したらバッファを 2 倍化して再クエリ)
        int hits = QueryWithGrowableBuffer(qbox);
        if (hits == 0) return null;

        // 各候補エッジに対し点-線分最短距離計算 → グローバル最短を選択
        uint bestEdgeId = 0;
        double bestDist = double.PositiveInfinity;
        int bestSegmentIndex = 0;
        double bestT = 0.0;
        GeoCoordinate bestProjected = default;
        bool found = false;

        for (int i = 0; i < hits; i++)
        {
            uint edgeId = _queryBuffer[i];
            if (!_graph.CanPass(edgeId, profileName)) continue;

            var fullShape = BuildFullShape(edgeId);
            for (int s = 0; s < fullShape.Length - 1; s++)
            {
                var (dist, proj, t) = GeoMath.PointToSegment(point, fullShape[s], fullShape[s + 1]);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestEdgeId = edgeId;
                    bestSegmentIndex = s;
                    bestT = t;
                    bestProjected = proj;
                    found = true;
                }
            }
        }

        if (!found || bestDist > searchDistanceM) return null;

        ushort offset = ComputeOffset(bestEdgeId, bestSegmentIndex, bestT, bestProjected);
        return new SnapResult(bestProjected, bestEdgeId, offset);
    }

    private int QueryWithGrowableBuffer(in OdrgBbox qbox)
    {
        while (true)
        {
            int hits = NativeRTreeQuery.Query(
                _graph.GetRTreeNodes(),
                _graph.RTreeRootIndex,
                _graph.GetEdgeAabbs(),
                qbox,
                _queryBuffer);
            if (hits <= _queryBuffer.Length) return hits;
            // overrun: バッファを 2 倍化して再クエリ
            _queryBuffer = new uint[Math.Max(_queryBuffer.Length * 2, hits)];
        }
    }

    /// <summary>
    /// エッジの完全シェイプ (From 頂点 + 中間シェイプ + To 頂点) を組み立てる。
    /// 中間シェイプ 0 件のエッジは [From, To] の 1 セグメントになる。
    /// </summary>
    private GeoCoordinate[] BuildFullShape(uint edgeId)
    {
        var edge = _graph.ReadEdge(edgeId);
        var midShape = _graph.GetEdgeShape(edgeId);

        var full = new GeoCoordinate[midShape.Length + 2];
        full[0] = _graph.GetVertex(edge.FromVertexId);
        for (int i = 0; i < midShape.Length; i++)
        {
            full[i + 1] = midShape[i];
        }
        full[^1] = _graph.GetVertex(edge.ToVertexId);
        return full;
    }

    /// <summary>
    /// スナップ点のエッジ全長に対する累積距離比から <see cref="SnapResult.Offset"/> (ushort 0..65535) を計算する
    /// （計画書 §2.12 Q6: 距離比 × 65535）。
    /// </summary>
    private ushort ComputeOffset(uint edgeId, int segmentIndex, double t, GeoCoordinate projected)
    {
        var edge = _graph.ReadEdge(edgeId);
        var midShape = _graph.GetEdgeShape(edgeId);

        // セグメント前までの累積距離 + 現セグメント上の投影位置までの距離
        var from = _graph.GetVertex(edge.FromVertexId);
        var to = _graph.GetVertex(edge.ToVertexId);

        double cumulative = 0.0;
        GeoCoordinate prev = from;
        int segCount = midShape.Length + 1;

        for (int s = 0; s < segmentIndex; s++)
        {
            GeoCoordinate next = (s + 1 < segCount) ? GetSegmentEnd(s, midShape, to) : to;
            cumulative += GeoMath.HaversineMeters(prev, next);
            prev = next;
        }

        // 現セグメント上の prev → projected
        cumulative += GeoMath.HaversineMeters(prev, projected);

        // エッジ全長
        double totalLength = ComputeEdgeLength(from, midShape, to);
        if (totalLength <= 0.0) return 0;

        double ratio = cumulative / totalLength;
        if (ratio < 0.0) ratio = 0.0;
        else if (ratio > 1.0) ratio = 1.0;

        return (ushort)Math.Round(ratio * 65535.0);
    }

    private static GeoCoordinate GetSegmentEnd(int segmentIndex, ReadOnlySpan<GeoCoordinate> midShape, GeoCoordinate to)
    {
        // セグメント s の終点 = midShape[s] (0..midShape.Length-1) または to (最終)
        return segmentIndex < midShape.Length ? midShape[segmentIndex] : to;
    }

    private static double ComputeEdgeLength(GeoCoordinate from, ReadOnlySpan<GeoCoordinate> midShape, GeoCoordinate to)
    {
        double total = 0.0;
        GeoCoordinate prev = from;
        for (int i = 0; i < midShape.Length; i++)
        {
            total += GeoMath.HaversineMeters(prev, midShape[i]);
            prev = midShape[i];
        }
        total += GeoMath.HaversineMeters(prev, to);
        return total;
    }
}
