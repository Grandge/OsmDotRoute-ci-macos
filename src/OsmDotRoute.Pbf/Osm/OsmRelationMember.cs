namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// <see cref="OsmRelation"/> のメンバー 1 件。
/// </summary>
/// <param name="MemberId">参照先 OSM 要素 ID (delta デコード済の絶対値、種別は <paramref name="Type"/> で判別)。</param>
/// <param name="Type">メンバー種別 (Node / Way / Relation)。</param>
/// <param name="RoleStringIndex">role 文字列の <see cref="OsmStringTable"/> インデックス (例: "outer", "inner", "from", "via", "to")。</param>
internal readonly record struct OsmRelationMember(
    long MemberId,
    OsmMemberType Type,
    int RoleStringIndex);
