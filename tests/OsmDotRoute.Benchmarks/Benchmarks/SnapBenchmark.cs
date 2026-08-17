using BenchmarkDotNet.Attributes;
using OsmDotRoute.Benchmarks.Generators;
using OsmDotRoute.Native;

namespace OsmDotRoute.Benchmarks.Benchmarks;

/// <summary>
/// 道路スナップ単独の性能（経路計算の内訳分解用、計画書 §3.1 補助）。
/// 同じ route-pairs.json の From 座標を順繰りに使う（100 サンプル）。
/// Phase 3 ステップ 3C.4 で Native 系統 (.odrg) に統一。
/// </summary>
[MemoryDiagnoser]
public class SnapBenchmark
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

    [Benchmark(Description = "Router.SnapToRoad")]
    public GeoCoordinate? Snap()
    {
        var pair = _pairs[_index];
        _index = (_index + 1) % _pairs.Length;
        return _router.SnapToRoad(_profile, pair.From, 500f);
    }
}
