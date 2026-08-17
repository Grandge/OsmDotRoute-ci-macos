using System.Diagnostics;
using OsmDotRoute.Benchmarks.Generators;
using OsmDotRoute.Native;

namespace OsmDotRoute.Benchmarks;

/// <summary>
/// 都道府県単位ベンチ（Phase 3 ステップ 3G）。
/// 指定 .odrg で 100 ペア生成 → C0 ベンチ（Stopwatch 10 イテレーション）。
/// BenchmarkDotNet ではなく手動計測。大規模 .odrg のロード時間も含めて計測する。
/// </summary>
internal static class PrefectureBench
{
    public static void Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: --prefecture-bench <path-to-.odrg> [pair-count] [iterations]");
            Console.WriteLine("Example: --prefecture-bench samples/Data/aichi.odrg 100 10");
            return;
        }

        var odrgPath = args[0];
        var pairCount = args.Length > 1 ? int.Parse(args[1]) : 100;
        var iterations = args.Length > 2 ? int.Parse(args[2]) : 10;

        if (!File.Exists(odrgPath))
        {
            Console.Error.WriteLine($"File not found: {odrgPath}");
            return;
        }

        Console.WriteLine($"=== Prefecture Benchmark ===");
        Console.WriteLine($"odrg     : {odrgPath}");
        Console.WriteLine($"pairs    : {pairCount}");
        Console.WriteLine($"iterations: {iterations}");
        Console.WriteLine();

        // Load .odrg
        Console.Write("Loading .odrg ... ");
        var loadSw = Stopwatch.StartNew();
        var graph = new NativeRoadGraph(odrgPath);
        var snapper = new NativeRoadSnapper(graph);
        var routerDb = new OsmDotRoute.RouterDb(graph, snapper);
        loadSw.Stop();
        Console.WriteLine($"{loadSw.Elapsed.TotalSeconds:F2}s");

        var stats = routerDb.GetStatistics();
        Console.WriteLine($"Vertices : {stats.VertexCount:N0}");
        Console.WriteLine($"Edges    : {stats.EdgeCount:N0}");
        Console.WriteLine($"FileSize : {new FileInfo(odrgPath).Length / (1024.0 * 1024.0):F1} MB");
        Console.WriteLine($"Bounds   : ({stats.SouthWest.Latitude:F4}, {stats.SouthWest.Longitude:F4}) - ({stats.NorthEast.Latitude:F4}, {stats.NorthEast.Longitude:F4})");
        Console.WriteLine();

        // Generate route pairs
        Console.Write($"Generating {pairCount} route pairs ... ");
        var bounds = new MapBounds(stats.SouthWest, stats.NorthEast);
        var profile = VehicleProfile.Car;
        var router = new Router(routerDb);
        var pairsFile = RoutePairGenerator.Generate(router, profile, bounds, seed: 20260528,
            count: pairCount, maxDistanceMeters: 50_000);
        Console.WriteLine("done");
        Console.WriteLine();

        // Warmup
        Console.Write("Warmup (3 iterations) ... ");
        for (var w = 0; w < 3; w++)
        {
            foreach (var pair in pairsFile.Pairs)
                router.Calculate(profile, pair.From, pair.To);
        }
        Console.WriteLine("done");

        // Benchmark
        Console.WriteLine($"Running C0 benchmark ({iterations} iterations x {pairCount} pairs) ...");
        var times = new double[iterations];
        var nullCount = 0;

        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            foreach (var pair in pairsFile.Pairs)
            {
                var route = router.Calculate(profile, pair.From, pair.To);
                if (route == null && i == 0) nullCount++;
            }
            sw.Stop();
            times[i] = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"  iter {i + 1,2}: {times[i]:F2} ms ({times[i] / pairCount:F3} ms/route)");
        }

        Console.WriteLine();
        Console.WriteLine("=== Results ===");
        var mean = times.Average();
        var stdDev = Math.Sqrt(times.Select(t => (t - mean) * (t - mean)).Average());
        Console.WriteLine($"Mean      : {mean:F2} ms total, {mean / pairCount:F3} ms/route");
        Console.WriteLine($"StdDev    : {stdDev:F2} ms");
        Console.WriteLine($"Min       : {times.Min():F2} ms");
        Console.WriteLine($"Max       : {times.Max():F2} ms");
        Console.WriteLine($"Null routes: {nullCount}/{pairCount}");
        Console.WriteLine($"Load time : {loadSw.Elapsed.TotalSeconds:F2}s");

        // Allocated estimate (GC based, rough)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memBefore = GC.GetTotalMemory(true);
        foreach (var pair in pairsFile.Pairs)
            router.Calculate(profile, pair.From, pair.To);
        var memAfter = GC.GetTotalMemory(false);
        var allocEstimate = (memAfter - memBefore) / (1024.0 * 1024.0);
        Console.WriteLine($"Alloc est : {allocEstimate:F2} MB ({pairCount} routes)");

        graph.Dispose();
    }
}
