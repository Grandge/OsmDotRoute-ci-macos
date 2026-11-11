namespace OsmDotRoute;

/// <summary>
/// 道路ネットワーク全体を表す GeoJSON FeatureCollection（LineString 列）（REQ-RTE-004）。
/// </summary>
public sealed class RoadNetworkGeoJson
{
    /// <summary>
    /// JSON 文字列から GeoJSON ラッパーを作成する。
    /// </summary>
    /// <param name="json">GeoJSON FeatureCollection の JSON 文字列</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> が <c>null</c></exception>
    public RoadNetworkGeoJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Json = json;
    }

    /// <summary>GeoJSON FeatureCollection の JSON 文字列</summary>
    public string Json { get; }

    /// <summary>JSON 文字列を返す。</summary>
    public override string ToString() => Json;
}
