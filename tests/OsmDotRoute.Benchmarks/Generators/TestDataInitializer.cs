using System.Text.Json;

namespace OsmDotRoute.Benchmarks.Generators;

/// <summary>
/// ベンチマーク用テストデータ（route-pairs.json / restrictions-*.json）の生成・読込ヘルパー。
/// 計画書 §3.2 / §3.3 のシード値を固定し、決定論的に再現可能にする。
/// </summary>
internal static class TestDataInitializer
{
    /// <summary>route-pairs.json 生成時のシード（計画書 §3.2）。</summary>
    public const int RoutePairSeed = 20260520;

    /// <summary>restrictions-mixed-100.json 生成時のシード（計画書 §3.3）。</summary>
    public const int MixedSeed = 20260521;

    /// <summary>restrictions-block-100.json 生成時のシード（計画書 §3.3）。</summary>
    public const int BlockSeed = 20260522;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        IncludeFields = false,
    };

    /// <summary>
    /// プロジェクトソースの TestData ディレクトリ（コミット対象）。
    /// 出力ディレクトリの TestData は <see cref="BenchmarkAssets.TestDataDirectory"/>（csproj で
    /// CopyToOutputDirectory 指定）から自動的にコピーされるため、生成はソース側に行う。
    /// </summary>
    private static string ResolveSourceTestDataDir()
    {
        // bin/Release/net9.0 から 3 階層上に戻り、TestData/ に入る
        // AppContext.BaseDirectory は末尾 \ を含むことがあるので TrimEnd して正規化
        var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < 3; i++) dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir, "TestData");
    }

    /// <summary>3 ファイルすべてを生成して保存する。既存ファイルは上書き。</summary>
    public static GeneratedPaths GenerateAll()
    {
        var srcDir = ResolveSourceTestDataDir();
        Directory.CreateDirectory(srcDir);
        var routePairsPath = Path.Combine(srcDir, "route-pairs.json");
        var mixedPath = Path.Combine(srcDir, "restrictions-mixed-100.json");
        var blockPath = Path.Combine(srcDir, "restrictions-block-100.json");

        Console.WriteLine($"RouterDb 読込: {BenchmarkAssets.TsushimaOdrgPath}");
        var (routerDb, graph) = BenchmarkAssets.LoadNativeRouterDb();
        try
        {
            var stats = routerDb.GetStatistics();
            var bounds = new MapBounds(stats.SouthWest, stats.NorthEast);
            Console.WriteLine($"  Vertices: {stats.VertexCount:N0}, Edges: {stats.EdgeCount:N0}");
            Console.WriteLine($"  Bounds: ({stats.SouthWest.Latitude:F4}, {stats.SouthWest.Longitude:F4}) - ({stats.NorthEast.Latitude:F4}, {stats.NorthEast.Longitude:F4})");

            // route-pairs.json
            Console.WriteLine("起終点ペア生成中（100 件）...");
            var profile = VehicleProfile.Car;
            var router = new Router(routerDb);
            var pairs = RoutePairGenerator.Generate(router, profile, bounds, RoutePairSeed);
            File.WriteAllText(routePairsPath, JsonSerializer.Serialize(pairs, JsonOptions));
            Console.WriteLine($"  → {routePairsPath} ({pairs.Pairs.Count} ペア)");

            // restrictions-mixed-100.json
            Console.WriteLine("混合制約生成中（100 件）...");
            var mixed = RestrictionGenerator.GenerateMixed(bounds, MixedSeed);
            File.WriteAllText(mixedPath, JsonSerializer.Serialize(mixed, JsonOptions));
            Console.WriteLine($"  → {mixedPath} ({mixed.Areas.Count} 件)");

            // restrictions-block-100.json
            Console.WriteLine("BlockOnly 制約生成中（100 件）...");
            var blockOnly = RestrictionGenerator.GenerateBlockOnly(bounds, BlockSeed);
            File.WriteAllText(blockPath, JsonSerializer.Serialize(blockOnly, JsonOptions));
            Console.WriteLine($"  → {blockPath} ({blockOnly.Areas.Count} 件)");

            return new GeneratedPaths(routePairsPath, mixedPath, blockPath);
        }
        finally
        {
            graph.Dispose();
        }
    }

    public static RoutePairsFile LoadRoutePairs()
    {
        if (!File.Exists(BenchmarkAssets.RoutePairsPath))
        {
            throw new FileNotFoundException(
                $"route-pairs.json が見つかりません。先に `dotnet run -c Release --project tests/OsmDotRoute.Benchmarks -- --generate-data` を実行してください。",
                BenchmarkAssets.RoutePairsPath);
        }
        var json = File.ReadAllText(BenchmarkAssets.RoutePairsPath);
        return JsonSerializer.Deserialize<RoutePairsFile>(json, JsonOptions)
            ?? throw new InvalidDataException("route-pairs.json のデシリアライズに失敗");
    }

    public static RestrictionsFile LoadMixedRestrictions() => LoadRestrictionsFile(BenchmarkAssets.MixedRestrictionsPath);
    public static RestrictionsFile LoadBlockRestrictions() => LoadRestrictionsFile(BenchmarkAssets.BlockRestrictionsPath);

    private static RestrictionsFile LoadRestrictionsFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{Path.GetFileName(path)} が見つかりません。先に `--generate-data` を実行してください。", path);
        }
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RestrictionsFile>(json, JsonOptions)
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} のデシリアライズに失敗");
    }
}

internal sealed record GeneratedPaths(string RoutePairsPath, string MixedGeoJsonPath, string BlockGeoJsonPath);
