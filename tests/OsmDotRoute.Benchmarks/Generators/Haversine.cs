namespace OsmDotRoute.Benchmarks.Generators;

/// <summary>
/// ベンチマーク用の Haversine 距離計算（WGS84、メートル単位）。
/// 経路精度ではなく決定論的なペア生成と距離フィルタが目的なので、楕円体補正は省略。
/// </summary>
internal static class Haversine
{
    private const double EarthRadiusM = 6371000.0;

    public static double DistanceMeters(GeoCoordinate a, GeoCoordinate b)
    {
        var lat1 = a.Latitude * Math.PI / 180.0;
        var lat2 = b.Latitude * Math.PI / 180.0;
        var dLat = (b.Latitude - a.Latitude) * Math.PI / 180.0;
        var dLon = (b.Longitude - a.Longitude) * Math.PI / 180.0;

        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1) * Math.Cos(lat2)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        return EarthRadiusM * c;
    }
}
