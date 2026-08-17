using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// way を頂点境界で分割した結果のエッジ 1 本分の記録。
/// </summary>
/// <param name="OsmWayId">由来となった OSM Way ID（トレース・デバッグ用）。</param>
/// <param name="FromVertexId">始端頂点 ID（<see cref="VertexAssignment"/> 採番、0 始まり）。</param>
/// <param name="ToVertexId">終端頂点 ID（<see cref="VertexAssignment"/> 採番、0 始まり）。閉ループエッジでは <paramref name="FromVertexId"/> と一致しうる。</param>
/// <param name="ShapeNodeRefs">
/// 端点を**含まない**中間ノードの OSM Node ID 列。仕様書 §4.3 の `shapeLength` 定義に合わせて
/// 端点 2 つを除いた中間点のみを保持。直線エッジの場合は長さ 0。
/// </param>
/// <param name="TagKeys">由来 way の tag キー（<see cref="OsmStringTable"/> インデックス、3.5/3.6 で参照）。</param>
/// <param name="TagValues">由来 way の tag 値（<see cref="OsmStringTable"/> インデックス）。</param>
/// <param name="StringTable">tag インデックスの解決元。same-block 内のみ有効。</param>
/// <remarks>
/// 3.4 で生成、3.5 でフラグ bake、3.6 でプロファイル bake、3.8 で .odrg に書出。
/// 座標 (lon, lat) は <see cref="ShapeNodeRefs"/> および頂点表から PBF 3 パス目で解決。
/// </remarks>
internal sealed record EdgeRecord(
    long OsmWayId,
    int FromVertexId,
    int ToVertexId,
    long[] ShapeNodeRefs,
    int[] TagKeys,
    int[] TagValues,
    OsmStringTable StringTable);
