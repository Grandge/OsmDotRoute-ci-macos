using BenchmarkDotNet.Attributes;
using OsmDotRoute.Benchmarks.Generators;
using OsmDotRoute.Geometry;
using OsmDotRoute.Native;

namespace OsmDotRoute.Benchmarks.Benchmarks;

/// <summary>
/// 動的制約 add/remove スループット（Phase 3 計画書 §3.5 C4、ステップ 3E.2）。
/// 1 op = <see cref="RestrictedAreaService.AddBlockArea(GeoPolygon, string?)"/> +
/// <see cref="RestrictedAreaService.Remove(RestrictedAreaId)"/> 1 サイクル
/// （ユーザー判断 Q3=A 確定、Phase 1 未測定の新規シナリオ）。
/// </summary>
/// <remarks>
/// <para>
/// 測定対象は <see cref="RestrictedAreaService"/> 単独。<see cref="Router"/> コンストラクタ経由で
/// <see cref="NativeRoadGraph"/> を自動 attach 済 = 3B eager bake 動作下のスループット。
/// </para>
/// <para>
/// polygon プールは <c>restrictions-mixed-100.json</c> の block 系 50 件をリサイクル。
/// 1 op で eager bake (QueryEdgesByAabb + EdgeIntersectsShape + HashSet 追加) と
/// cache RemoveArea (HashSet 削除) の合計コストを観測する。
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class RestrictionThroughputBenchmark
{
    private OsmDotRoute.RouterDb _routerDb = default!;
    private NativeRoadGraph _graph = default!;
    private RestrictedAreaService _service = default!;
    private GeoPolygon[] _polygonPool = default!;
    private int _index;

    [GlobalSetup]
    public void Setup()
    {
        (_routerDb, _graph) = BenchmarkAssets.LoadNativeRouterDb();
        _service = new RestrictedAreaService();
        // Router コンストラクタが内部で _service.AttachGraph(graph) を呼ぶ (3B eager bake 有効化)
        _ = new Router(_routerDb, _service);

        var file = TestDataInitializer.LoadMixedRestrictions();
        _polygonPool = RestrictionGenerator.ToPolygons(file)
            .Where(p => p.Entry.Type == "block")
            .Select(p => p.Polygon)
            .ToArray();
        if (_polygonPool.Length == 0)
        {
            throw new InvalidOperationException(
                "restrictions-mixed-100.json から block 系 polygon が取得できません。" +
                "先に `--generate-data` で TestData を再生成してください。");
        }
        _index = 0;
    }

    [GlobalCleanup]
    public void Cleanup() => _graph.Dispose();

    [Benchmark(Description = "AddBlockArea + Remove(id) 1 サイクル")]
    public void AddRemoveCycle()
    {
        var polygon = _polygonPool[_index];
        _index = (_index + 1) % _polygonPool.Length;
        var id = _service.AddBlockArea(polygon);
        _service.Remove(id);
    }
}
