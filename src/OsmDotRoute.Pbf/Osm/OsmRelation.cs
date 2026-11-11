namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の Relation 解析結果。
/// </summary>
/// <param name="Id">OSM Relation ID。</param>
/// <param name="Members">構成メンバー（ID は delta デコード済の絶対値、role は StringTable インデックス）。</param>
/// <param name="TagKeys">tag キー（<see cref="OsmStringTable"/> インデックス、空タグなら長さ 0）。</param>
/// <param name="TagValues">tag 値（<see cref="OsmStringTable"/> インデックス、<paramref name="TagKeys"/> と同じ長さ）。</param>
/// <remarks>
/// <para>Phase 2 では Relation を抽出対象に含めない（ターン制限 `type=restriction` は Phase 4+ 延期、§5.6-18 計画書）。
/// 本パーサーは仕様完全性のために用意する。</para>
/// </remarks>
internal sealed record OsmRelation(
    long Id,
    OsmRelationMember[] Members,
    int[] TagKeys,
    int[] TagValues);
