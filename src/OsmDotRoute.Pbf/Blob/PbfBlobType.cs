namespace OsmDotRoute.Pbf.Blob;

/// <summary>
/// OSM PBF の Blob 種別 (BlobHeader.type 文字列に対応)。
/// </summary>
internal enum PbfBlobType
{
    /// <summary>BlobHeader.type が "OSMHeader" 以外で認識できない値。</summary>
    Unknown = 0,

    /// <summary>"OSMHeader" — ファイル先頭に 1 つだけ現れる HeaderBlock。</summary>
    Header = 1,

    /// <summary>"OSMData" — Node/Way/Relation を含む PrimitiveBlock。</summary>
    Data = 2,
}
