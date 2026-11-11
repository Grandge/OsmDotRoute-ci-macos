namespace OsmDotRoute.Profiles;

/// <summary>
/// 道路の通行方向制限。OSM の <c>oneway</c> タグから決定される。
/// </summary>
internal enum OnewayDirection
{
    /// <summary>両方向通行可</summary>
    Bidirectional,

    /// <summary>OSM デジタイズ方向（From → To）のみ通行可</summary>
    Forward,

    /// <summary>OSM デジタイズ方向の逆（To → From）のみ通行可</summary>
    Backward,
}
