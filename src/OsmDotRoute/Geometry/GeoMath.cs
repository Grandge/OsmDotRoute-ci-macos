namespace OsmDotRoute.Geometry;

/// <summary>
/// 経緯度ベースの幾何計算ヘルパ（Phase 3 ステップ 3A.5a、計画書 §4.5.1）。
/// </summary>
/// <remarks>
/// <para>
/// 全関数は WGS84 平均半径 <see cref="EarthRadiusMeters"/> = 6,371,008.8 m を採用。
/// 局所スケール（数十 m〜数 km）の計算は緯度補正コサイン (Q7 確定) による
/// 局所メートル平面化を用い、緯度 35° で誤差サブ cm。
/// </para>
/// <para>
/// 大圏距離は <see cref="HaversineMeters"/>。点-線分最短距離は <see cref="PointToSegment"/>。
/// 検索 bbox の度幅換算は <see cref="MetersToBboxDegrees"/>。
/// </para>
/// </remarks>
internal static class GeoMath
{
    /// <summary>WGS84 平均半径 (m)。</summary>
    public const double EarthRadiusMeters = 6371008.8;

    private const double DegToRad = Math.PI / 180.0;

    /// <summary>
    /// 2 点間の Haversine 大圏距離 (m) を返す。
    /// </summary>
    public static double HaversineMeters(GeoCoordinate a, GeoCoordinate b)
    {
        double lat1 = a.Latitude * DegToRad;
        double lat2 = b.Latitude * DegToRad;
        double dLat = (b.Latitude - a.Latitude) * DegToRad;
        double dLon = (b.Longitude - a.Longitude) * DegToRad;
        double sinDLat = Math.Sin(dLat * 0.5);
        double sinDLon = Math.Sin(dLon * 0.5);
        double h = sinDLat * sinDLat + Math.Cos(lat1) * Math.Cos(lat2) * sinDLon * sinDLon;
        double c = 2.0 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1.0 - h));
        return EarthRadiusMeters * c;
    }

    /// <summary>
    /// 検索半径 <paramref name="meters"/> (m) を、指定緯度における bbox 度幅 (dLat, dLon) に換算する
    /// （Q5 確定式）。
    /// </summary>
    /// <param name="meters">検索半径 (m)。</param>
    /// <param name="lat">基準緯度 (度)。経度方向の補正に <c>cos(lat)</c> を用いる。</param>
    /// <returns>緯度方向と経度方向の度幅 (dLat, dLon)。</returns>
    public static (double DLat, double DLon) MetersToBboxDegrees(double meters, double lat)
    {
        // 1 度緯度 ≒ EarthRadius × π/180 ≒ 111195 m (赤道で 111319 m、極で 111694 m、平均値で近似)。
        // 簡略のため定数 111320 を採用。
        const double MetersPerDegLat = 111320.0;
        double dLat = meters / MetersPerDegLat;
        double cosLat = Math.Cos(lat * DegToRad);
        // 経度方向は cos(lat) で短縮。cos(lat) が極めて小さい場合 (極地) は dLon 無限大化を避け dLat に丸める。
        double dLon = cosLat <= 1e-9 ? dLat : meters / (MetersPerDegLat * cosLat);
        return (dLat, dLon);
    }

    /// <summary>
    /// クエリ点 <paramref name="query"/> と線分 (<paramref name="a"/>, <paramref name="b"/>) の
    /// 最短距離 (m) + 線分上の投影点 + 線分上での t 値 [0..1] を返す（Q7 確定: 緯度補正コサイン）。
    /// </summary>
    /// <remarks>
    /// 線分長 0 (a == b) のときは投影点 = a、t = 0、距離 = <see cref="HaversineMeters"/>(query, a)。
    /// 平面化基準は線分中点の緯度。エッジ単位（数十 m）で誤差サブ cm、線分跨ぎでも cos(lat) を 1 度だけ計算。
    /// </remarks>
    public static (double DistanceM, GeoCoordinate Projected, double T) PointToSegment(
        GeoCoordinate query,
        GeoCoordinate a,
        GeoCoordinate b)
    {
        // 平面化基準点 = 線分中点
        double lat0 = (a.Latitude + b.Latitude) * 0.5;
        double lon0 = (a.Longitude + b.Longitude) * 0.5;
        double cosLat0 = Math.Cos(lat0 * DegToRad);

        double ax = (a.Longitude - lon0) * cosLat0 * DegToRad * EarthRadiusMeters;
        double ay = (a.Latitude - lat0) * DegToRad * EarthRadiusMeters;
        double bx = (b.Longitude - lon0) * cosLat0 * DegToRad * EarthRadiusMeters;
        double by = (b.Latitude - lat0) * DegToRad * EarthRadiusMeters;
        double qx = (query.Longitude - lon0) * cosLat0 * DegToRad * EarthRadiusMeters;
        double qy = (query.Latitude - lat0) * DegToRad * EarthRadiusMeters;

        double dx = bx - ax;
        double dy = by - ay;
        double lenSq = dx * dx + dy * dy;

        if (lenSq <= 0.0)
        {
            // 退化線分
            return (HaversineMeters(query, a), a, 0.0);
        }

        // 線分上への投影 t (クランプ [0,1])
        double t = ((qx - ax) * dx + (qy - ay) * dy) / lenSq;
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        double projX = ax + t * dx;
        double projY = ay + t * dy;
        double distX = qx - projX;
        double distY = qy - projY;
        double dist = Math.Sqrt(distX * distX + distY * distY);

        // 投影点を経緯度へ逆変換
        double projLat = lat0 + projY / EarthRadiusMeters / DegToRad;
        double projLon = lon0 + projX / EarthRadiusMeters / DegToRad / cosLat0;

        return (dist, new GeoCoordinate(projLat, projLon), t);
    }
}
