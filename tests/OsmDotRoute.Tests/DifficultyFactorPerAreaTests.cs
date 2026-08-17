using OsmDotRoute.Profiles;
using OsmDotRoute.Restrictions;
using OsmDotRoute.Routing;
using OsmDotRoute.Tests.Native;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// 移動困難エリアの <c>speedFactor</c> が「エリア単位」ではなく「Shape 単位」で累乗される不具合
/// （親プロFB <c>bug_request_difficulty_factor_per_shape.md</c>、REQ-RST-013〜015 / REQ-RST-030〜032）の
/// 回帰固定テスト。
/// </summary>
/// <remarks>
/// 原因は <see cref="RestrictedAreaEdgeCache.AddDifficulty"/> が同一 (areaId, edgeId) を無条件に
/// <c>List</c> へ追加していたこと。複数 Shape（メッシュ集合・分割ポリゴン）で 1 エリアを与えると
/// 跨いだ Shape 数だけ係数が掛かり、事実上通行不能になっていた。
/// bake 経路（graph 注入時）とフォールバック経路（<see cref="RestrictedAreaService.EvaluateConstraints"/>、
/// <c>seenIds</c> で ID 単位に重複排除）の結果一致をもって仕様とする。
/// </remarks>
public class DifficultyFactorPerAreaTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public DifficultyFactorPerAreaTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Calculate_ManyMeshesSingleDifficultyArea_AppliesSpeedFactorOnce()
    {
        var baseline = CarBaseline();
        var meshes = MeshesCoveringRoute(baseline);
        Assert.True(meshes.Length >= 10,
            $"1 エリア複数 Shape の再現に十分なメッシュ数が必要。実際={meshes.Length}");

        var restrictions = new RestrictedAreaService();
        restrictions.AddDifficultyArea(meshes, DifficultyTypes.Flooding);   // API 呼出は 1 回 = エリア 1 個
        var router = new Router(_fixture.RouterDb, restrictions);

        var result = router.Calculate(VehicleProfile.Car, baseline.From, baseline.To);
        Assert.NotNull(result);

        // car flooding = speedFactor 0.3 → 所要時間は 1/0.3 ≒ 3.33 倍。
        // 修正前は跨いだメッシュ数 N に対し 0.3^N となり、比が数千〜数十万倍に爆発していた。
        var ratio = result!.TotalDurationSec / baseline.Route.TotalDurationSec;
        Assert.InRange(ratio, 3.30, 3.36);
        Assert.Equal(baseline.Route.TotalDistanceM, result.TotalDistanceM, precision: 0);
    }

    [Fact]
    public void Calculate_ManyMeshes_MatchesSinglePolygonEquivalent()
    {
        var baseline = CarBaseline();
        var bounds = RouteBounds(baseline.Route, marginDeg: 0.002);

        var meshRestrictions = new RestrictedAreaService();
        meshRestrictions.AddDifficultyArea(
            MeshCode.EnumerateInBounds(bounds, MeshLevel.EighthMesh).ToArray(),
            DifficultyTypes.Flooding);
        var meshResult = new Router(_fixture.RouterDb, meshRestrictions)
            .Calculate(VehicleProfile.Car, baseline.From, baseline.To);

        var polygonRestrictions = new RestrictedAreaService();
        polygonRestrictions.AddDifficultyArea(PolygonOf(bounds), DifficultyTypes.Flooding);
        var polygonResult = new Router(_fixture.RouterDb, polygonRestrictions)
            .Calculate(VehicleProfile.Car, baseline.From, baseline.To);

        Assert.NotNull(meshResult);
        Assert.NotNull(polygonResult);

        // 同じ領域を「メッシュ集合（多 Shape）」と「ポリゴン 1 枚（単一 Shape）」で与えた結果は一致する
        Assert.Equal(polygonResult!.TotalDistanceM, meshResult!.TotalDistanceM, precision: 0);
        Assert.Equal(polygonResult.TotalDurationSec, meshResult.TotalDurationSec, precision: 1);
    }

    [Fact]
    public void BakedCache_And_EvaluateConstraints_AgreeOnCombinedFactor()
    {
        var baseline = CarBaseline();
        var meshes = MeshesCoveringRoute(baseline);
        var evaluator = VehicleProfile.Car.Evaluator;
        IRoadGraph graph = _fixture.Graph;

        // bake 経路（graph 注入あり）
        var baked = new RestrictedAreaService();
        baked.AttachGraph(graph);
        baked.AddDifficultyArea(meshes, DifficultyTypes.Flooding);

        // フォールバック経路（graph 注入なし → EvaluateConstraints）
        var fallback = new RestrictedAreaService();
        fallback.AddDifficultyArea(meshes, DifficultyTypes.Flooding);

        int compared = 0;
        for (uint e = 0; e < graph.EdgeCount && compared < 200; e++)
        {
            var areas = baked.Cache!.GetDifficultyAreas(e);
            if (areas.Count == 0) continue;

            double combinedBaked = 1.0;
            foreach (var area in areas)
            {
                combinedBaked *= evaluator.EvaluateDifficulty(area.DifficultyType).SpeedFactor;
            }

            var combinedFallback = fallback.EvaluateConstraints(FullShape(graph, e), evaluator);

            Assert.Equal(combinedFallback, combinedBaked, precision: 6);
            compared++;
        }

        Assert.True(compared > 0, "difficulty が bake されたエッジが 1 件も無く、比較が成立していない");
    }

    [Fact]
    public void Calculate_TwoDifferentMeshAreas_StillMultiply()
    {
        // 非回帰: 「別エリア」同士の重ね合わせは従来どおり積になる（重複排除はエリア内 Shape に限る）
        var baseline = CarBaseline();
        var meshes = MeshesCoveringRoute(baseline);

        var restrictions = new RestrictedAreaService();
        restrictions.AddDifficultyArea(meshes, DifficultyTypes.Flooding);       // car 0.3
        restrictions.AddDifficultyArea(meshes, DifficultyTypes.Construction);   // car 0.2
        var router = new Router(_fixture.RouterDb, restrictions);

        var result = router.Calculate(VehicleProfile.Car, baseline.From, baseline.To);
        Assert.NotNull(result);

        // 0.3 × 0.2 = 0.06 → 1/0.06 ≒ 16.67 倍
        var ratio = result!.TotalDurationSec / baseline.Route.TotalDurationSec;
        Assert.InRange(ratio, 16.50, 16.80);
    }

    [Fact]
    public void Remove_AfterMultiShapeBake_RestoresBaseline()
    {
        var baseline = CarBaseline();
        var meshes = MeshesCoveringRoute(baseline);

        var restrictions = new RestrictedAreaService();
        var id = restrictions.AddDifficultyArea(meshes, DifficultyTypes.Flooding);
        var router = new Router(_fixture.RouterDb, restrictions);

        restrictions.Remove(id);

        var afterRemove = router.Calculate(VehicleProfile.Car, baseline.From, baseline.To);
        Assert.NotNull(afterRemove);
        Assert.Equal(baseline.Route.TotalDistanceM, afterRemove!.TotalDistanceM, precision: 1);
        Assert.Equal(baseline.Route.TotalDurationSec, afterRemove.TotalDurationSec, precision: 1);
    }

    // --- ヘルパ ---

    private sealed record Baseline(GeoCoordinate From, GeoCoordinate To, Route Route);

    private Baseline CarBaseline()
    {
        var (from, to) = _fixture.MediumPair;
        var route = new Router(_fixture.RouterDb).Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(route);
        return new Baseline(from, to, route!);
    }

    /// <summary>ベースライン経路全体を覆う 1/8 細分メッシュ（125m）集合。1 エリア＝多 Shape の再現用。</summary>
    private static MeshCode[] MeshesCoveringRoute(Baseline baseline)
        => MeshCode.EnumerateInBounds(RouteBounds(baseline.Route, marginDeg: 0.002), MeshLevel.EighthMesh).ToArray();

    private static MapBounds RouteBounds(Route route, double marginDeg)
    {
        var span = route.Shape.Span;
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        for (int i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (c.Latitude < minLat) minLat = c.Latitude;
            if (c.Latitude > maxLat) maxLat = c.Latitude;
            if (c.Longitude < minLon) minLon = c.Longitude;
            if (c.Longitude > maxLon) maxLon = c.Longitude;
        }
        return new MapBounds(
            new GeoCoordinate(minLat - marginDeg, minLon - marginDeg),
            new GeoCoordinate(maxLat + marginDeg, maxLon + marginDeg));
    }

    private static GeoPolygon PolygonOf(MapBounds bounds)
    {
        return new GeoPolygon(new[]
        {
            new GeoCoordinate(bounds.MinLatitude, bounds.MinLongitude),
            new GeoCoordinate(bounds.MinLatitude, bounds.MaxLongitude),
            new GeoCoordinate(bounds.MaxLatitude, bounds.MaxLongitude),
            new GeoCoordinate(bounds.MaxLatitude, bounds.MinLongitude),
            new GeoCoordinate(bounds.MinLatitude, bounds.MinLongitude),
        });
    }

    /// <summary>エッジの端点 + 中間シェイプを結合した座標列（<c>EvaluateConstraints</c> 入力形式）。</summary>
    private static IReadOnlyList<GeoCoordinate> FullShape(IRoadGraph graph, uint edgeId)
    {
        var edge = graph.GetEdge(edgeId);
        var list = new List<GeoCoordinate>(edge.Shape.Count + 2) { graph.GetVertex(edge.From) };
        for (int i = 0; i < edge.Shape.Count; i++) list.Add(edge.Shape[i]);
        list.Add(graph.GetVertex(edge.To));
        return list;
    }
}
