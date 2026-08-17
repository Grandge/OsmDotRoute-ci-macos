namespace OsmDotRoute;

/// <summary>
/// 緯度経度の座標 (WGS84)。緯度は北を正、経度は東を正とする。
/// </summary>
/// <param name="Latitude">緯度（度、北を正）</param>
/// <param name="Longitude">経度（度、東を正）</param>
public readonly record struct GeoCoordinate(double Latitude, double Longitude);
