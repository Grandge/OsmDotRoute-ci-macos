namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// WGS84 経緯度のバウンディングボックス (度単位の double)。
/// </summary>
/// <remarks>
/// OSM PBF の HeaderBBox はナノ度 (sint64) で格納されているが、本構造体は度単位に変換済みの値を保持する。
/// </remarks>
internal readonly record struct OsmBoundingBox(
    double MinLon,
    double MinLat,
    double MaxLon,
    double MaxLat);
