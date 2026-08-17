using BenchmarkDotNet.Attributes;
using OsmDotRoute.Benchmarks.Generators;
using OsmDotRoute.Native;

namespace OsmDotRoute.Benchmarks.Benchmarks;

/// <summary>
/// 制約下の経路計算性能（REQ-NFR-002 + Phase 3 ステップ 3B 効果測定 + ステップ 3E ベンチ再実施）。
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 計画書 §3.5 の 3 ケース（C0/C1/C2）を <see cref="Case"/> で切替。
/// Phase 3 ステップ 3E.1 で C0/C1/C2 にリナンバし、Bicycle 切替を追加:
/// </para>
/// <list type="bullet">
///   <item><c>"C0"</c>: 制約なし + Car (baseline)</item>
///   <item><c>"C1"</c>: mixed-100 + Car (Phase 1 C3 相当、3B 効果本命)</item>
///   <item><c>"C2"</c>: mixed-100 + Bicycle (Phase 3 新規)</item>
/// </list>
/// <para>
/// Mode は Native-Detached / Native-Attached 2 通り:
/// </para>
/// <list type="bullet">
///   <item><c>"Native-Detached"</c>: 津島市 .odrg + <see cref="NativeRoadGraph"/> + AttachGraph **未実行**
///         (3B 前相当、graph 未注入の Phase 1 動作フォールバック)</item>
///   <item><c>"Native-Attached"</c>: 津島市 .odrg + <see cref="NativeRoadGraph"/> + AttachGraph 実行済
///         (3B キャッシュ動作、本命)</item>
/// </list>
/// <para>
/// 3B 効果 = <c>(Native-Detached - Native-Attached) / Native-Detached × 100</c>。
/// 同一 RouterDb (津島市 .odrg) + 同一 100 ペア で 3B キャッシュ有無のみが違う。
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class RouteWithConstraintsBenchmark
{
    private OsmDotRoute.RouterDb _routerDb = default!;
    private NativeRoadGraph? _nativeGraph;
    private Router _router = default!;
    private VehicleProfile _profile = default!;
    private RoutePair[] _pairs = default!;
    private int _index;

    /// <summary>
    /// Native-Detached / Native-Attached モード切替（3B 効果計測の本命）。
    /// </summary>
    [Params("Native-Detached", "Native-Attached")]
    public string Mode { get; set; } = "Native-Attached";

    /// <summary>
    /// Phase 3 計画書 §3.5 のシナリオ:
    /// C0 = 制約なし + Car / C1 = mixed-100 + Car / C2 = mixed-100 + Bicycle。
    /// </summary>
    [Params("C0", "C1", "C2")]
    public string Case { get; set; } = "C0";

    [GlobalSetup]
    public void Setup()
    {
        _pairs = [.. TestDataInitializer.LoadRoutePairs().Pairs];
        _index = 0;

        (_profile, RestrictedAreaService? restrictions) = Case switch
        {
            "C0" => (VehicleProfile.Car, (RestrictedAreaService?)null),
            "C1" => (VehicleProfile.Car, BuildMixed(100)),
            "C2" => (VehicleProfile.Bicycle, BuildMixed(100)),
            _ => throw new InvalidOperationException($"未知の Case: {Case}"),
        };

        switch (Mode)
        {
            case "Native-Detached":
                (_routerDb, _nativeGraph) = BenchmarkAssets.LoadNativeRouterDb();
                _router = new Router(_routerDb, restrictions, autoAttachGraph: false);  // 3B 前相当: AttachGraph スキップ → Phase 1 fallback
                break;
            case "Native-Attached":
                (_routerDb, _nativeGraph) = BenchmarkAssets.LoadNativeRouterDb();
                _router = new Router(_routerDb, restrictions);  // 3B キャッシュ動作 (autoAttachGraph=true default)
                break;
            default:
                throw new InvalidOperationException($"未知の Mode: {Mode}");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _nativeGraph?.Dispose();
        _nativeGraph = null;
    }

    [Benchmark(Description = "Router.Calculate (Mode + Case)")]
    public Route? Calculate()
    {
        var pair = _pairs[_index];
        _index = (_index + 1) % _pairs.Length;
        return _router.Calculate(_profile, pair.From, pair.To);
    }

    private static RestrictedAreaService BuildMixed(int count)
    {
        var file = TestDataInitializer.LoadMixedRestrictions();
        return Register(file, count);
    }

    private static RestrictedAreaService BuildBlockOnly(int count)
    {
        var file = TestDataInitializer.LoadBlockRestrictions();
        return Register(file, count);
    }

    private static RestrictedAreaService Register(RestrictionsFile file, int count)
    {
        var service = new RestrictedAreaService();
        var polygons = RestrictionGenerator.ToPolygons(file);
        for (var i = 0; i < count && i < polygons.Count; i++)
        {
            var (entry, polygon) = polygons[i];
            if (entry.Type == "block")
            {
                service.AddBlockArea(polygon);
            }
            else
            {
                service.AddDifficultyArea(polygon, entry.DifficultyType!);
            }
        }
        return service;
    }
}
