namespace OsmDotRoute.Routing;

/// <summary>
/// 単方向 Dijkstra による経路探索エンジン（REQ-RTE-001、REQ-RST-013〜015）。
/// スナップ結果 2 点を入力に最短時間経路を探索する。動的制約（進入不可エリア・難所エリア）は
/// <see cref="EdgeWeightCalculator"/> 経由で各エッジ評価に反映される（ステップ 9 で統合）。
/// </summary>
/// <remarks>
/// <para>
/// スナップ点はエッジ上の任意位置（オフセット）であり、グラフ頂点ではない。
/// このため、ソーススナップから「両端点（<see cref="RoadEdge.From"/> / <see cref="RoadEdge.To"/>）」を
/// 仮想頂点として初期フロンティアに入れ、ターゲット側でも同様に「両端点経由でターゲット点に到達した時の総コスト」を
/// 各 pop ごとに比較して <c>bestCost</c> を更新する。
/// </para>
/// <para>
/// ソースとターゲットが同一エッジに乗る場合は、Dijkstra 開始前に「同一エッジ上を直接通過する経路」のコストを
/// <c>bestCost</c> 初期値として設定し、それより悪い候補は枝刈りする。
/// </para>
/// <para>
/// 重み = 距離 / (速度 × 結合 speedFactor)（秒）。制約評価はエッジ単位で 1 回（部分通過時も同じ係数）。
/// </para>
/// </remarks>
internal sealed class DijkstraEngine
{
    private readonly IRoadGraph _graph;
    private readonly EdgeWeightCalculator _calculator;

    public DijkstraEngine(IRoadGraph graph, EdgeWeightCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(calculator);
        _graph = graph;
        _calculator = calculator;
    }

    /// <summary>
    /// スナップ済みの 2 点間で最短経路を探索する。経路未発見時は <c>null</c>。
    /// </summary>
    public DijkstraResult? Run(SnapResult sourceSnap, SnapResult targetSnap)
    {
        var sourceEdge = _graph.GetEdge(sourceSnap.EdgeId);
        var sourceEval = _calculator.Evaluate(sourceEdge);
        if (!sourceEval.CanPass) return null;

        var targetEdge = _graph.GetEdge(targetSnap.EdgeId);
        var targetEval = _calculator.Evaluate(targetEdge);
        if (!targetEval.CanPass) return null;

        // スナップ点 → エッジ端点の方向性
        // GetEdge は MoveToEdge 経由のため canonical view (DataInverted=false 想定) で取得しているが、
        // 念のため返却値の DataInverted を尊重する。
        var sourceCanForward = EdgeWeightCalculator.CanTraverseInEnumeratorDirection(sourceEval, sourceEdge.DataInverted);
        var sourceCanReverse = EdgeWeightCalculator.CanTraverseInEnumeratorDirection(sourceEval, !sourceEdge.DataInverted);
        var targetCanForward = EdgeWeightCalculator.CanTraverseInEnumeratorDirection(targetEval, targetEdge.DataInverted);
        var targetCanReverse = EdgeWeightCalculator.CanTraverseInEnumeratorDirection(targetEval, !targetEdge.DataInverted);

        var sourceDist = (double)sourceEdge.DistanceM;
        var targetDist = (double)targetEdge.DistanceM;
        var fSource = sourceSnap.Offset / 65535.0;
        var fTarget = targetSnap.Offset / 65535.0;

        var bestCost = double.PositiveInfinity;
        var bestDist = 0.0;
        var bestEntryVertex = uint.MaxValue;
        var bestSameEdge = false;

        // 同一エッジ特殊ケース: 直接通過コストを bestCost 初期値に（制約込み）
        if (sourceSnap.EdgeId == targetSnap.EdgeId)
        {
            if (fTarget > fSource && sourceCanForward)
            {
                var d = (fTarget - fSource) * sourceDist;
                var t = _calculator.EvaluateEdgePartialDurationSec(sourceEdge, d, sourceEval);
                if (t < bestCost) { bestCost = t; bestDist = d; bestSameEdge = true; }
            }
            else if (fTarget < fSource && sourceCanReverse)
            {
                var d = (fSource - fTarget) * sourceDist;
                var t = _calculator.EvaluateEdgePartialDurationSec(sourceEdge, d, sourceEval);
                if (t < bestCost) { bestCost = t; bestDist = d; bestSameEdge = true; }
            }
            else if (Math.Abs(fTarget - fSource) < double.Epsilon)
            {
                // 完全同一点
                return new DijkstraResult(0, 0, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<double>(), SameEdge: true);
            }
        }

        var vertexCount = _graph.VertexCount;
        var cost = new double[vertexCount];
        var dist = new double[vertexCount];
        var parentVertex = new uint[vertexCount];
        var parentEdge = new uint[vertexCount];
        var visited = new bool[vertexCount];
        for (uint i = 0; i < vertexCount; i++)
        {
            cost[i] = double.PositiveInfinity;
            parentVertex[i] = uint.MaxValue;
            parentEdge[i] = uint.MaxValue;
        }

        var pq = new BinaryHeap<uint>();

        // ソース初期化: スナップ点 → エッジ両端点（制約込み）
        if (sourceCanForward)
        {
            var d = (1.0 - fSource) * sourceDist;
            var t = _calculator.EvaluateEdgePartialDurationSec(sourceEdge, d, sourceEval);
            if (!double.IsPositiveInfinity(t))
            {
                cost[sourceEdge.To] = t;
                dist[sourceEdge.To] = d;
                parentEdge[sourceEdge.To] = sourceSnap.EdgeId;
                parentVertex[sourceEdge.To] = uint.MaxValue; // 仮想ソース印
                pq.Push(sourceEdge.To, t);
            }
        }
        if (sourceCanReverse)
        {
            var d = fSource * sourceDist;
            var t = _calculator.EvaluateEdgePartialDurationSec(sourceEdge, d, sourceEval);
            if (!double.IsPositiveInfinity(t) && t < cost[sourceEdge.From])
            {
                cost[sourceEdge.From] = t;
                dist[sourceEdge.From] = d;
                parentEdge[sourceEdge.From] = sourceSnap.EdgeId;
                parentVertex[sourceEdge.From] = uint.MaxValue;
                pq.Push(sourceEdge.From, t);
            }
        }

        // メインループ
        while (pq.TryPop(out var u, out var uCost))
        {
            if (visited[u]) continue;
            if (uCost > cost[u]) continue; // stale
            visited[u] = true;

            // 既知 bestCost より悪ければ以降のフロンティアは候補にならない
            if (uCost >= bestCost) break;

            // ターゲットエッジ端点にあれば、エッジ通過で更新候補（制約込み）
            if (u == targetEdge.From && targetCanForward)
            {
                var remD = fTarget * targetDist;
                var remT = _calculator.EvaluateEdgePartialDurationSec(targetEdge, remD, targetEval);
                if (!double.IsPositiveInfinity(remT))
                {
                    var total = uCost + remT;
                    if (total < bestCost)
                    {
                        bestCost = total;
                        bestDist = dist[u] + remD;
                        bestEntryVertex = u;
                        bestSameEdge = false;
                    }
                }
            }
            if (u == targetEdge.To && targetCanReverse)
            {
                var remD = (1.0 - fTarget) * targetDist;
                var remT = _calculator.EvaluateEdgePartialDurationSec(targetEdge, remD, targetEval);
                if (!double.IsPositiveInfinity(remT))
                {
                    var total = uCost + remT;
                    if (total < bestCost)
                    {
                        bestCost = total;
                        bestDist = dist[u] + remD;
                        bestEntryVertex = u;
                        bestSameEdge = false;
                    }
                }
            }

            // 近傍展開（制約込み）
            var en = _graph.GetEdgeEnumerator(u);
            while (en.MoveNext())
            {
                var v = en.To;
                if (visited[v]) continue;

                var edgeTime = _calculator.EvaluateEdgeDurationSec(en);
                if (double.IsPositiveInfinity(edgeTime)) continue;

                var newCost = uCost + edgeTime;
                if (newCost < cost[v])
                {
                    cost[v] = newCost;
                    dist[v] = dist[u] + en.DistanceM;
                    parentVertex[v] = u;
                    parentEdge[v] = en.EdgeId;
                    pq.Push(v, newCost);
                }
            }
        }

        if (double.IsPositiveInfinity(bestCost)) return null;

        // 経路復元（同一エッジ直接通過の場合は頂点列なし）
        if (bestSameEdge)
        {
            return new DijkstraResult(bestCost, bestDist, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<double>(), SameEdge: true);
        }

        var vertexPath = new List<uint>();
        var edgePath = new List<uint>();
        var w = bestEntryVertex;
        while (w != uint.MaxValue)
        {
            vertexPath.Add(w);
            if (parentEdge[w] != uint.MaxValue)
            {
                edgePath.Add(parentEdge[w]);
            }
            w = parentVertex[w];
        }
        vertexPath.Reverse();
        edgePath.Reverse();

        // 各通過頂点での起点（ソーススナップ点）からの累積所要秒（REQ-FMT-006、Shape 点別累積秒の素材）
        var vertexCumulativeDurationsSec = new double[vertexPath.Count];
        for (int i = 0; i < vertexPath.Count; i++)
        {
            vertexCumulativeDurationsSec[i] = cost[vertexPath[i]];
        }

        return new DijkstraResult(bestCost, bestDist, vertexPath, edgePath, vertexCumulativeDurationsSec, SameEdge: false);
    }
}

/// <summary>
/// Dijkstra 探索結果。
/// </summary>
/// <param name="TotalDurationSec">総所要時間（秒）。ソース/ターゲットスナップ部分のエッジ通過時間を含む</param>
/// <param name="TotalDistanceM">総距離（メートル）。エッジ <c>DistanceM</c> 値の合算（多角線の実長ではない）</param>
/// <param name="VertexPath">通過頂点列（ソース側端点 → ターゲット側端点）。同一エッジ直通の場合は空</param>
/// <param name="EdgePath">通過エッジ ID 列（先頭がソーススナップエッジ、末尾が <c>VertexPath</c> 末尾頂点への流入エッジ）。同一エッジ直通の場合は空</param>
/// <param name="VertexCumulativeDurationsSec"><see cref="VertexPath"/> と整列した、ソーススナップ点から各通過頂点までの累積所要秒。同一エッジ直通の場合は空</param>
/// <param name="SameEdge">ソース・ターゲットが同一エッジ上で直接通過した場合 <c>true</c></param>
internal sealed record DijkstraResult(
    double TotalDurationSec,
    double TotalDistanceM,
    IReadOnlyList<uint> VertexPath,
    IReadOnlyList<uint> EdgePath,
    IReadOnlyList<double> VertexCumulativeDurationsSec,
    bool SameEdge);
