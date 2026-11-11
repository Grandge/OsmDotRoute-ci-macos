namespace OsmDotRoute.Native;

/// <summary>
/// <see cref="NativeRoadGraph"/> の CSR ペイロード要素（Phase 3 ステップ 3A.3e）。
/// </summary>
/// <param name="EdgeId">エッジ ID（<c>.odrg</c> 内の論理エッジインデックス）。</param>
/// <param name="IsReversed">
/// 列挙起点頂点が <c>OdrgEdge.ToVertexId</c> 側の場合 <c>true</c>。
/// Itinero の <see cref="OsmDotRoute.Routing.IRoadGraphEdgeEnumerator.DataInverted"/> セマンティクスに対応する。
/// </param>
internal readonly record struct OutEdgeEntry(uint EdgeId, bool IsReversed);
