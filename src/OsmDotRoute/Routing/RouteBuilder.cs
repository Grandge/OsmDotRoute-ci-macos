using OsmDotRoute.Geometry;

namespace OsmDotRoute.Routing;

/// <summary>
/// Dijkstra 結果から公開型 <see cref="Route"/> を組み立てる。
/// </summary>
/// <remarks>
/// Phase 1 のシェイプ統合方針:
/// <list type="bullet">
///   <item>ソース部分: <c>[ソーススナップ点, ソース端点頂点]</c>（中間シェイプは未補間）</item>
///   <item>中間エッジ: <c>[..中間シェイプ.., 到達側頂点]</c>。エッジ進行方向に合わせシェイプを反転</item>
///   <item>ターゲット部分: <c>[..中間シェイプ.., ターゲットスナップ点]</c>（同様に未補間）</item>
/// </list>
/// 総距離は <see cref="DijkstraResult.TotalDistanceM"/>（エッジ DistanceM の積算）をそのまま採用し、
/// シェイプ多角線の実長との誤差は許容する（Phase 1 完了判定で ±10% 以内）。
/// <para>
/// Phase 4 親プロFB 追補（REQ-FMT-006、Ver. 1.1.0）: Shape 構築と並行して各点の累積所要秒列を組み立てる。
/// 頂点点は <see cref="DijkstraResult.VertexCumulativeDurationsSec"/> から直接採用、エッジ内中間シェイプ点は
/// 多角線距離按分で補間する（エッジ内の SpeedFactor は 1 つなので距離按分が正確）。
/// ソーススナップ点は 0、ターゲットスナップ点は <see cref="DijkstraResult.TotalDurationSec"/> に固定し
/// 端点不変条件を保証する。
/// </para>
/// </remarks>
internal sealed class RouteBuilder
{
    private readonly IRoadGraph _graph;

    public RouteBuilder(IRoadGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    public Route Build(SnapResult sourceSnap, SnapResult targetSnap, DijkstraResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var shape = new List<GeoCoordinate> { sourceSnap.Location };
        var cumulative = new List<double> { 0.0 };

        if (result.SameEdge)
        {
            shape.Add(targetSnap.Location);
            cumulative.Add(result.TotalDurationSec);
            return new Route(result.TotalDistanceM, result.TotalDurationSec, shape.ToArray().AsMemory(), cumulative.ToArray().AsMemory());
        }

        // VertexPath: [sourceEndpoint, v1, v2, ..., targetEndpoint]
        // EdgePath:   [sourceSnap.EdgeId, e_to_v1, e_to_v2, ..., e_to_targetEndpoint]
        var vertexPath = result.VertexPath;
        var edgePath = result.EdgePath;
        var vertexCum = result.VertexCumulativeDurationsSec;

        if (vertexPath.Count == 0)
        {
            // 想定外（SameEdge=false なら少なくとも 1 頂点はある）。スナップ点のみで返す。
            shape.Add(targetSnap.Location);
            cumulative.Add(result.TotalDurationSec);
            return new Route(result.TotalDistanceM, result.TotalDurationSec, shape.ToArray().AsMemory(), cumulative.ToArray().AsMemory());
        }

        // ソース端点
        shape.Add(_graph.GetVertex(vertexPath[0]));
        cumulative.Add(vertexCum[0]);

        // 中間エッジ（index 1 以降の EdgePath を辿る）
        for (int i = 1; i < vertexPath.Count; i++)
        {
            var fromVertex = vertexPath[i - 1];
            var toVertex = vertexPath[i];
            var edgeId = edgePath[i]; // i=1 は中間遷移の最初のエッジ
            var edge = _graph.GetEdge(edgeId);

            // 進行方向（fromVertex → toVertex）がストレージ順（edge.From → edge.To）と一致するか
            var traversedInStorageOrder = edge.From == fromVertex && edge.To == toVertex;
            var startTime = vertexCum[i - 1];
            var endTime = vertexCum[i];
            var edgeDuration = endTime - startTime;

            if (edge.Shape.Count > 0)
            {
                var fromCoord = _graph.GetVertex(fromVertex);
                var toCoord = _graph.GetVertex(toVertex);
                int shapeCount = edge.Shape.Count;

                // 1st pass: エッジ内多角線の実長（fromCoord → 各中間点 → toCoord）を算出
                double totalDist = 0.0;
                var prev = fromCoord;
                for (int s = 0; s < shapeCount; s++)
                {
                    int idx = traversedInStorageOrder ? s : (shapeCount - 1 - s);
                    var cur = edge.Shape[idx];
                    totalDist += GeoMath.HaversineMeters(prev, cur);
                    prev = cur;
                }
                totalDist += GeoMath.HaversineMeters(prev, toCoord);

                // 2nd pass: 中間シェイプ点へ累積距離按分で時間を割り振り、Shape に追加
                if (totalDist <= 0.0)
                {
                    // 退化（中間点を含めて全座標が一致）。中間点に startTime を割り当てる
                    for (int s = 0; s < shapeCount; s++)
                    {
                        int idx = traversedInStorageOrder ? s : (shapeCount - 1 - s);
                        shape.Add(edge.Shape[idx]);
                        cumulative.Add(startTime);
                    }
                }
                else
                {
                    double cumDist = 0.0;
                    prev = fromCoord;
                    for (int s = 0; s < shapeCount; s++)
                    {
                        int idx = traversedInStorageOrder ? s : (shapeCount - 1 - s);
                        var cur = edge.Shape[idx];
                        cumDist += GeoMath.HaversineMeters(prev, cur);
                        shape.Add(cur);
                        cumulative.Add(startTime + (cumDist / totalDist) * edgeDuration);
                        prev = cur;
                    }
                }
            }
            shape.Add(_graph.GetVertex(toVertex));
            cumulative.Add(endTime);
        }

        shape.Add(targetSnap.Location);
        cumulative.Add(result.TotalDurationSec);

        return new Route(result.TotalDistanceM, result.TotalDurationSec, shape.ToArray().AsMemory(), cumulative.ToArray().AsMemory());
    }
}