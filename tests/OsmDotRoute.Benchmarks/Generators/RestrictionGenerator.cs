namespace OsmDotRoute.Benchmarks.Generators;

/// <summary>
/// 制約ポリゴンを決定論的に生成する。
/// 中心点を bbox 内で乱数選択 → 3〜5 頂点の凸風多角形を半径 200〜2000m で生成。
/// </summary>
internal static class RestrictionGenerator
{
    /// <summary>
    /// 混合パターン: BlockArea 50 件 + DifficultyArea 50 件
    /// （flood / debris / narrow / damaged / closed を各 10 件）。
    /// </summary>
    public static RestrictionsFile GenerateMixed(MapBounds bounds, int seed, int total = 100)
    {
        var rng = new Random(seed);
        var areas = new List<RestrictionEntry>(total);
        var blockCount = total / 2;
        var difficultyTypes = new[] { "flood", "debris", "narrow", "damaged", "closed" };
        var perType = (total - blockCount) / difficultyTypes.Length;

        for (var i = 0; i < blockCount; i++)
        {
            areas.Add(GenerateEntry(rng, bounds, "block", difficultyType: null));
        }
        foreach (var dt in difficultyTypes)
        {
            for (var i = 0; i < perType; i++)
            {
                areas.Add(GenerateEntry(rng, bounds, "difficulty", dt));
            }
        }

        return new RestrictionsFile(seed, areas.Count, "mixed", areas);
    }

    /// <summary>Block のみパターン: BlockArea を <paramref name="total"/> 件生成。</summary>
    public static RestrictionsFile GenerateBlockOnly(MapBounds bounds, int seed, int total = 100)
    {
        var rng = new Random(seed);
        var areas = new List<RestrictionEntry>(total);
        for (var i = 0; i < total; i++)
        {
            areas.Add(GenerateEntry(rng, bounds, "block", difficultyType: null));
        }
        return new RestrictionsFile(seed, areas.Count, "block-only", areas);
    }

    private static RestrictionEntry GenerateEntry(Random rng, MapBounds bounds, string type, string? difficultyType)
    {
        var center = new GeoCoordinate(
            bounds.MinLatitude + rng.NextDouble() * (bounds.MaxLatitude - bounds.MinLatitude),
            bounds.MinLongitude + rng.NextDouble() * (bounds.MaxLongitude - bounds.MinLongitude));

        var vertexCount = 3 + rng.Next(3); // 3, 4, 5
        var radiusMeters = 200.0 + rng.NextDouble() * 1800.0; // 200〜2000m
        var startAngle = rng.NextDouble() * Math.PI * 2;

        // 緯度経度の概算スケール（中心緯度における 1 度あたりメートル）
        var metersPerDegLat = 111_320.0;
        var metersPerDegLon = 111_320.0 * Math.Cos(center.Latitude * Math.PI / 180.0);

        var outer = new List<List<double>>(vertexCount + 1);
        for (var i = 0; i < vertexCount; i++)
        {
            var theta = startAngle + i * (Math.PI * 2 / vertexCount);
            var r = radiusMeters * (0.7 + rng.NextDouble() * 0.6); // 70〜130% の揺らぎで凸風
            var dLat = (r * Math.Sin(theta)) / metersPerDegLat;
            var dLon = (r * Math.Cos(theta)) / metersPerDegLon;
            outer.Add(new List<double> { center.Latitude + dLat, center.Longitude + dLon });
        }
        // 閉ループとして最初の頂点を末尾にも追加（GeoPolygon は 3 頂点以上必要、閉ループは任意だが揃える）
        outer.Add(new List<double> { outer[0][0], outer[0][1] });

        return new RestrictionEntry(type, difficultyType, outer);
    }

    /// <summary>
    /// 制約 JSON を <see cref="GeoPolygon"/> 列と種別タプル列に変換する。
    /// </summary>
    public static IReadOnlyList<(RestrictionEntry Entry, GeoPolygon Polygon)> ToPolygons(RestrictionsFile file)
    {
        var result = new List<(RestrictionEntry, GeoPolygon)>(file.Areas.Count);
        foreach (var area in file.Areas)
        {
            var coords = area.OuterBoundary
                .Select(p => new GeoCoordinate(p[0], p[1]))
                .ToList();
            result.Add((area, new GeoPolygon(coords)));
        }
        return result;
    }
}
