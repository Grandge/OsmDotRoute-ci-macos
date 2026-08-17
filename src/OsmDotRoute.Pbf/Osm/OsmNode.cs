namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の単体 Node 解析結果。
/// </summary>
/// <param name="Id">OSM ノード ID。</param>
/// <param name="Lon">WGS84 経度 (度単位、<see cref="PrimitiveBlock.ToLon"/> 変換済)。</param>
/// <param name="Lat">WGS84 緯度 (度単位、<see cref="PrimitiveBlock.ToLat"/> 変換済)。</param>
/// <param name="TagKeys">tag キー（<see cref="OsmStringTable"/> インデックス、空タグなら長さ 0）。</param>
/// <param name="TagValues">tag 値（<see cref="OsmStringTable"/> インデックス、<paramref name="TagKeys"/> と同じ長さ）。</param>
/// <remarks>
/// <para>OSM PBF の Info フィールド (version / timestamp / changeset / user) は Phase 2 ルーティング用途で
/// 不要なため意図的にスキップする。</para>
/// </remarks>
internal sealed record OsmNode(
    long Id,
    double Lon,
    double Lat,
    int[] TagKeys,
    int[] TagValues);
