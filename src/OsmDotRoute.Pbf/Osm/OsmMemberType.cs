namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM Relation のメンバー種別。PBF の <c>MemberType</c> enum と一対一対応。
/// </summary>
internal enum OsmMemberType
{
    /// <summary>Node メンバー (PBF MemberType = 0)。</summary>
    Node = 0,

    /// <summary>Way メンバー (PBF MemberType = 1)。</summary>
    Way = 1,

    /// <summary>Relation メンバー (PBF MemberType = 2)。</summary>
    Relation = 2,
}
