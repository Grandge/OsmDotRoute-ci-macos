using BenchmarkDotNet.Attributes;
using OsmDotRoute.Benchmarks.Generators;
using OsmDotRoute.Native;

namespace OsmDotRoute.Benchmarks.Benchmarks;

/// <summary>
/// OsmDotRoute の経路計算性能（REQ-NFR-001、制約なし C0 相当）。
/// route-pairs.json の 100 ペアを順繰りに回す。
/// Phase 3 ステップ 3C.4 で Native 系統 (.odrg) に統一。
/// </summary>
[MemoryDiagnoser]
public class RouteCalculationBenchmark
{
    private OsmDotRoute.RouterDb _routerDb = default!;
    private NativeRoadGraph _graph = default!;
    private Router _router = default!;
    private VehicleProfile _profile = default!;
    private RoutePair[] _pairs = default!;
    private int _index;

    [GlobalSetup]
    public void Setup()
    {
        (_routerDb, _graph) = BenchmarkAssets.LoadNativeRouterDb();
        _router = new Router(_routerDb);
        _profile = VehicleProfile.Car;
        _pairs = [.. TestDataInitializer.LoadRoutePairs().Pairs];
        _index = 0;
    }

    [GlobalCleanup]
    public void Cleanup() => _graph.Dispose();

    [Benchmark(Description = "Router.Calculate (制約なし)")]
    public Route? Calculate()
    {
        var pair = _pairs[_index];
        _index = (_index + 1) % _pairs.Length;
        return _router.Calculate(_profile, pair.From, pair.To);
    }
}
