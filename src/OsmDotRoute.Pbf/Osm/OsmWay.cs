namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の Way 解析結果。道路ネットワーク抽出の中核データ。
/// </summary>
/// <param name="Id">OSM Way ID。</param>
/// <param name="NodeRefs">構成ノードの OSM Node ID 列（delta デコード済の絶対値）。</param>
/// <param name="TagKeys">tag キー（<see cref="OsmStringTable"/> インデックス、空タグなら長さ 0）。</param>
/// <param name="TagValues">tag 値（<see cref="OsmStringTable"/> インデックス、<paramref name="TagKeys"/> と同じ長さ）。</param>
/// <remarks>
/// <para>Phase 2 では `highway=*` タグの way を道路として抽出し、`NodeRefs` から頂点列を取得する。</para>
/// <para>Info / 拡張 LocationsOnWays (lat/lon) は意図的にスキップ。</para>
/// </remarks>
internal sealed record OsmWay(
    long Id,
    long[] NodeRefs,
    int[] TagKeys,
    int[] TagValues);
