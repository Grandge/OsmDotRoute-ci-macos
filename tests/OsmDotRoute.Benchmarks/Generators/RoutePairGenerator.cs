namespace OsmDotRoute.Benchmarks.Generators;

/// <summary>
/// 起終点ペアを決定論的に生成する。
/// bbox 内で乱数 2 点 → OsmDotRoute スナップ成功 + 直線距離 [minM, maxM] フィルタで採用。
/// </summary>
internal static class RoutePairGenerator
{
    /// <summary>ペアを <paramref name="count"/> 件生成する（採用条件を満たすまで最大 <paramref name="maxAttempts"/> 回試行）。</summary>
    public static RoutePairsFile Generate(
        Router router,
        VehicleProfile profile,
        MapBounds bounds,
        int seed,
        int count = 100,
        double minDistanceMeters = 1_000,
        double maxDistanceMeters = 30_000,
        int maxAttempts = 100_000,
        float snapDistanceM = 500f)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(profile);

        var rng = new Random(seed);
        var pairs = new List<RoutePair>(count);
        var attempts = 0;

        while (pairs.Count < count && attempts < maxAttempts)
        {
            attempts++;
            var from = RandomPoint(rng, bounds);
            var to = RandomPoint(rng, bounds);

            var dist = Haversine.DistanceMeters(from, to);
            if (dist < minDistanceMeters || dist > maxDistanceMeters) continue;

            var fromSnap = router.SnapToRoad(profile, from, snapDistanceM);
            if (fromSnap is null) continue;
            var toSnap = router.SnapToRoad(profile, to, snapDistanceM);
            if (toSnap is null) continue;

            pairs.Add(new RoutePair(from.Latitude, from.Longitude, to.Latitude, to.Longitude, dist));
        }

        if (pairs.Count < count)
        {
            throw new InvalidOperationException(
                $"起終点ペア生成に失敗: {pairs.Count}/{count} 件で {maxAttempts} 回試行を超過しました。" +
                "bbox が狭い・距離レンジが厳しい・スナップ半径が短いなどの可能性があります。");
        }

        return new RoutePairsFile(
            seed,
            count,
            bounds.MinLatitude, bounds.MinLongitude,
            bounds.MaxLatitude, bounds.MaxLongitude,
            pairs);
    }

    private static GeoCoordinate RandomPoint(Random rng, MapBounds bounds)
    {
        var lat = bounds.MinLatitude + rng.NextDouble() * (bounds.MaxLatitude - bounds.MinLatitude);
        var lon = bounds.MinLongitude + rng.NextDouble() * (bounds.MaxLongitude - bounds.MinLongitude);
        return new GeoCoordinate(lat, lon);
    }
}
