using OsmDotRoute.Tests.Native;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// 制約対応 Dijkstra 統合の検証テスト（Phase 1 ステップ 9 起源、REQ-RST-013〜015 / REQ-RST-030〜032、
/// Phase 3 ステップ 3C.2 で .odrg（津島市）ベースに書換）。
/// 制約なしベースラインに対して進入不可・難所エリア（単独・重複・通行不可・未知タイプ）の効果を検証する。
/// </summary>
public class RestrictedRoutingTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public RestrictedRoutingTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>テスト共通の baseline 文脈。</summary>
    private sealed record BaselineContext(
        RouterDb RouterDb,
        GeoCoordinate From,
        GeoCoordinate To,
        Route CarBaseline,
        Route PedestrianBaseline);

    [Fact]
    public void Calculate_NoRestrictions_MatchesBaselineExactly()
    {
        var ctx = SetupBaseline();
        var router = new Router(ctx.RouterDb, new RestrictedAreaService());     // 空サービス
        var result = router.Calculate(VehicleProfile.Car, ctx.From, ctx.To);
        Assert.NotNull(result);
        // 制約 0 件 → baseline と同じ結果
        Assert.Equal(ctx.CarBaseline.TotalDistanceM, result!.TotalDistanceM, precision: 1);
        Assert.Equal(ctx.CarBaseline.TotalDurationSec, result.TotalDurationSec, precision: 1);
    }

    [Fact]
    public void Calculate_BlockArea_OnRoute_DetoursOrReturnsNull()
    {
        var ctx = SetupBaseline();
        var restrictions = new RestrictedAreaService();
        restrictions.AddBlockArea(MakeSmallPolygonAroundShapeMidpoint(ctx.CarBaseline.Shape));
        var router = new Router(ctx.RouterDb, restrictions);

        var result = router.Calculate(VehicleProfile.Car, ctx.From, ctx.To);

        if (result is null) return;        // 迂回路なし → null は許容
        // 迂回ルート: 距離またはシェイプが baseline と異なる
        var sameShape = HasSameShape(result.Shape, ctx.CarBaseline.Shape);
        Assert.False(sameShape, $"BlockArea を経路上に置いたのにシェイプが baseline と同一。baseline.Dist={ctx.CarBaseline.TotalDistanceM:F1}m, constrained.Dist={result.TotalDistanceM:F1}m");
    }

    [Fact]
    public void Calculate_DifficultyArea_Flooding_CoveringRoute_Car_Time_3_33x()
    {
        var ctx = SetupBaseline();
        var restrictions = new RestrictedAreaService();
        restrictions.AddDifficultyArea(MakePolygonCoveringShape(ctx.CarBaseline.Shape, marginDeg: 0.01), DifficultyTypes.Flooding);
        var router = new Router(ctx.RouterDb, restrictions);

        var result = router.Calculate(VehicleProfile.Car, ctx.From, ctx.To);
        Assert.NotNull(result);

        // 全エッジが flooding 領域内 → car speedFactor=0.3、所要時間が baseline の 1/0.3 ≈ 3.33 倍
        var ratio = result!.TotalDurationSec / ctx.CarBaseline.TotalDurationSec;
        Assert.InRange(ratio, 3.30, 3.36);
        // 同じ経路を辿るはず（全領域が同じ係数なので最短経路は不変）
        Assert.Equal(ctx.CarBaseline.TotalDistanceM, result.TotalDistanceM, precision: 0);
    }

    [Fact]
    public void Calculate_DifficultyArea_Flooding_CoveringRoute_Pedestrian_Time_10x()
    {
        var ctx = SetupBaseline();
        var restrictions = new RestrictedAreaService();
        restrictions.AddDifficultyArea(MakePolygonCoveringShape(ctx.PedestrianBaseline.Shape, marginDeg: 0.01), DifficultyTypes.Flooding);
        var router = new Router(ctx.RouterDb, restrictions);

        var result = router.Calculate(VehicleProfile.Pedestrian, ctx.From, ctx.To);
        Assert.NotNull(result);

        // pedestrian flooding speedFactor=0.1 → 所要時間 10 倍
        var ratio = result!.TotalDurationSec / ctx.PedestrianBaseline.TotalDurationSec;
        Assert.InRange(ratio, 9.9, 10.1);
        Assert.Equal(ctx.PedestrianBaseline.TotalDistanceM, result.TotalDistanceM, precision: 0);
    }

    [Fact]
    public void Calculate_DifficultyArea_FloodingAndConstruction_Overlapping_Car_Time_16_67x()
    {
        var ctx = SetupBaseline();
        var polygon = MakePolygonCoveringShape(ctx.CarBaseline.Shape, marginDeg: 0.01);
        var restrictions = new RestrictedAreaService();
        restrictions.AddDifficultyArea(polygon, DifficultyTypes.Flooding);
        restrictions.AddDifficultyArea(polygon, DifficultyTypes.Construction);
        var router = new Router(ctx.RouterDb, restrictions);

        var result = router.Calculate(VehicleProfile.Car, ctx.From, ctx.To);
        Assert.NotNull(result);

        // car: flooding(0.3) × construction(0.2) = 0.06 → 1/0.06 ≈ 16.67 倍
        var ratio = result!.TotalDurationSec / ctx.CarBaseline.TotalDurationSec;
        Assert.InRange(ratio, 16.50, 16.80);
    }

    [Fact]
    public void Calculate_DifficultyArea_Landslide_CoveringRoute_Car_DetoursOrReturnsNull()
    {
        var ctx = SetupBaseline();
        var restrictions = new RestrictedAreaService();
        // ベースラインルートを覆う landslide（canPass:false） → 通行不可
        restrictions.AddDifficultyArea(MakePolygonCoveringShape(ctx.CarBaseline.Shape, marginDeg: 0.01), DifficultyTypes.Landslide);
        var router = new Router(ctx.RouterDb, restrictions);

        var result = router.Calculate(VehicleProfile.Car, ctx.From, ctx.To);

        if (result is null) return;     // 迂回路なし → null は許容
        // 迂回した場合は baseline と異なる経路
        var sameShape = HasSameShape(result.Shape, ctx.CarBaseline.Shape);
        Assert.False(sameShape, "landslide が canPass:false を返したのに baseline と同じ経路を辿った");
    }

    [Fact]
    public void Calculate_BlockArea_Overrides_DifficultyArea()
    {
        var ctx = SetupBaseline();
        var polygon = MakeSmallPolygonAroundShapeMidpoint(ctx.CarBaseline.Shape);
        var restrictions = new RestrictedAreaService();
        // 同じ領域に flooding（通行可）と BlockArea（通行不可）を重ねる
        restrictions.AddDifficultyArea(polygon, DifficultyTypes.Flooding);
        restrictions.AddBlockArea(polygon);
        var router = new Router(ctx.RouterDb, restrictions);

        var blocked = router.Calculate(VehicleProfile.Car, ctx.From, ctx.To);

        // BlockArea 優先 → 迂回 or null
        if (blocked is null) return;
        var sameShape = HasSameShape(blocked.Shape, ctx.CarBaseline.Shape);
        Assert.False(sameShape, "BlockArea が DifficultyArea 重複時に優先されていない");
    }

    [Fact]
    public void Calculate_UnknownDifficultyType_AppliesDifficultyDefault_NoSpeedChange()
    {
        var ctx = SetupBaseline();
        var restrictions = new RestrictedAreaService();
        // 未知タイプ "meteor" → difficultyDefault (speedFactor=1.0, canPass=true) 適用 → 速度変化なし
        restrictions.AddDifficultyArea(MakePolygonCoveringShape(ctx.CarBaseline.Shape, marginDeg: 0.01), "meteor");
        var router = new Router(ctx.RouterDb, restrictions);

        var result = router.Calculate(VehicleProfile.Car, ctx.From, ctx.To);
        Assert.NotNull(result);
        Assert.Equal(ctx.CarBaseline.TotalDistanceM, result!.TotalDistanceM, precision: 1);
        Assert.Equal(ctx.CarBaseline.TotalDurationSec, result.TotalDurationSec, precision: 1);
    }

    [Fact]
    public void Calculate_AfterClearAll_RestoresBaseline()
    {
        var ctx = SetupBaseline();
        var restrictions = new RestrictedAreaService();
        restrictions.AddBlockArea(MakeSmallPolygonAroundShapeMidpoint(ctx.CarBaseline.Shape));
        var router = new Router(ctx.RouterDb, restrictions);

        // クリア前: 制約適用
        // クリア後: baseline と同じ
        restrictions.ClearAll();
        var afterClear = router.Calculate(VehicleProfile.Car, ctx.From, ctx.To);
        Assert.NotNull(afterClear);
        Assert.Equal(ctx.CarBaseline.TotalDistanceM, afterClear!.TotalDistanceM, precision: 1);
        Assert.Equal(ctx.CarBaseline.TotalDurationSec, afterClear.TotalDurationSec, precision: 1);
    }

    // --- ヘルパ ---

    /// <summary>baseline 文脈を構築する。fixture の MediumPair を流用、car / pedestrian の両方で経路が取れることを期待。</summary>
    private BaselineContext SetupBaseline()
    {
        var (from, to) = _fixture.MediumPair;
        var router = new Router(_fixture.RouterDb);     // 制約なし baseline
        var car = router.Calculate(VehicleProfile.Car, from, to);
        var ped = router.Calculate(VehicleProfile.Pedestrian, from, to);
        Assert.NotNull(car);
        Assert.NotNull(ped);
        Assert.True(car!.Shape.Length >= 4 && ped!.Shape.Length >= 4,
            $"baseline ペアの shape 点数が不足: car={car.Shape.Length}, ped={ped.Shape.Length}");
        return new BaselineContext(_fixture.RouterDb, from, to, car, ped);
    }

    /// <summary>シェイプ全体を margin 度だけ広げた外接矩形ポリゴンを作る。</summary>
    private static GeoPolygon MakePolygonCoveringShape(ReadOnlyMemory<GeoCoordinate> shape, double marginDeg)
    {
        var span = shape.Span;
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
        minLat -= marginDeg; maxLat += marginDeg;
        minLon -= marginDeg; maxLon += marginDeg;
        return new GeoPolygon(new[]
        {
            new GeoCoordinate(minLat, minLon),
            new GeoCoordinate(minLat, maxLon),
            new GeoCoordinate(maxLat, maxLon),
            new GeoCoordinate(maxLat, minLon),
            new GeoCoordinate(minLat, minLon),
        });
    }

    /// <summary>シェイプ中央付近を覆う小さなポリゴン（≈30m 四方）。局所遮断テスト用。</summary>
    private static GeoPolygon MakeSmallPolygonAroundShapeMidpoint(ReadOnlyMemory<GeoCoordinate> shape)
    {
        var span = shape.Span;
        var midIdx = span.Length / 2;
        var c = span[midIdx];
        const double d = 0.0003;       // ≒ 30〜33m
        return new GeoPolygon(new[]
        {
            new GeoCoordinate(c.Latitude - d, c.Longitude - d),
            new GeoCoordinate(c.Latitude - d, c.Longitude + d),
            new GeoCoordinate(c.Latitude + d, c.Longitude + d),
            new GeoCoordinate(c.Latitude + d, c.Longitude - d),
            new GeoCoordinate(c.Latitude - d, c.Longitude - d),
        });
    }

    /// <summary>2 つのシェイプが頂点単位で（許容誤差 1e-6 度）一致するか。</summary>
    private static bool HasSameShape(ReadOnlyMemory<GeoCoordinate> a, ReadOnlyMemory<GeoCoordinate> b)
    {
        if (a.Length != b.Length) return false;
        var sa = a.Span;
        var sb = b.Span;
        for (var i = 0; i < sa.Length; i++)
        {
            if (Math.Abs(sa[i].Latitude - sb[i].Latitude) > 1e-6) return false;
            if (Math.Abs(sa[i].Longitude - sb[i].Longitude) > 1e-6) return false;
        }
        return true;
    }
}
