namespace OsmDotRoute;

/// <summary>
/// 緯度経度の矩形範囲（南西端と北東端で表現）。
/// 動的制約 GML 入力時のマップ範囲フィルタ（REQ-RST-040）等に使用する公開値型。
/// </summary>
/// <param name="SouthWest">南西端（最小緯度・最小経度）</param>
/// <param name="NorthEast">北東端（最大緯度・最大経度）</param>
public readonly record struct MapBounds(GeoCoordinate SouthWest, GeoCoordinate NorthEast)
{
    /// <summary>南端緯度</summary>
    public double MinLatitude => SouthWest.Latitude;

    /// <summary>北端緯度</summary>
    public double MaxLatitude => NorthEast.Latitude;

    /// <summary>西端経度</summary>
    public double MinLongitude => SouthWest.Longitude;

    /// <summary>東端経度</summary>
    public double MaxLongitude => NorthEast.Longitude;

    /// <summary>
    /// 北西端・南東端の座標ペアから矩形範囲を生成する（REQ-RTE-010、Ver. 1.3.0）。
    /// </summary>
    /// <param name="northWest">北西端（最大緯度・最小経度）</param>
    /// <param name="southEast">南東端（最小緯度・最大経度）</param>
    /// <returns>南西端・北東端に正規化した <see cref="MapBounds"/></returns>
    /// <remarks>
    /// 値の妥当性（南北・東西の逆転、面積ゼロ、NaN 等）は検証しない。
    /// 検証は利用先 API（<see cref="Router.CalculateBoundaryCrossing"/> 等）が
    /// <see cref="BoundaryCrossingKind.InvalidParameter"/> として報告する。
    /// </remarks>
    public static MapBounds FromNorthWestSouthEast(GeoCoordinate northWest, GeoCoordinate southEast)
        => new(
            new GeoCoordinate(southEast.Latitude, northWest.Longitude),
            new GeoCoordinate(northWest.Latitude, southEast.Longitude));

    /// <summary>
    /// 指定座標が本範囲内（境界線上を含む）にあるかを判定する。
    /// </summary>
    /// <param name="coordinate">判定対象の緯度経度</param>
    /// <returns>範囲内（境界含む）なら <c>true</c></returns>
    public bool Contains(GeoCoordinate coordinate)
    {
        return coordinate.Latitude >= SouthWest.Latitude
            && coordinate.Latitude <= NorthEast.Latitude
            && coordinate.Longitude >= SouthWest.Longitude
            && coordinate.Longitude <= NorthEast.Longitude;
    }
}
