namespace OsmDotRoute.Benchmarks.Generators;

/// <summary>route-pairs.json の 1 ペア。</summary>
internal sealed record RoutePair(
    double FromLat,
    double FromLon,
    double ToLat,
    double ToLon,
    double DistanceMeters)
{
    public GeoCoordinate From => new(FromLat, FromLon);
    public GeoCoordinate To => new(ToLat, ToLon);
}

/// <summary>route-pairs.json のルートオブジェクト。</summary>
internal sealed record RoutePairsFile(
    int Seed,
    int Count,
    double BoundsMinLat,
    double BoundsMinLon,
    double BoundsMaxLat,
    double BoundsMaxLon,
    List<RoutePair> Pairs);
