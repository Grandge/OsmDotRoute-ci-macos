using OsmDotRoute.Tests.Native;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Route.CumulativeDurationsSec の不変条件検証（REQ-FMT-006、Ver. 1.1.0、Phase 4 親プロFB 追補）。
/// 親プロジェクト「災害廃棄物処理シミュレーション」の区間別速度低下アニメーション要望を起源とし、
/// Shape 点別累積所要秒の整列・端点・単調性・難所反映・SameEdge・互換コンストラクタの 6 不変条件を網羅する。
/// </summary>
public class CumulativeDurationsTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public CumulativeDurationsTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Cumulative_Length_MatchesShapeLength()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(route);
        // 整列条件: 累積秒列は Shape と 1:1 で対応
        Assert.Equal(route!.Shape.Length, route.CumulativeDurationsSec.Length);
    }

    [Fact]
    public void Cumulative_Endpoints_AreExactlyZeroAndTotalDuration()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(route);
        var cum = route!.CumulativeDurationsSec.Span;
        // 端点不変条件: [0] == 0、[^1] == TotalDurationSec を厳密一致（同じ積算ロジック由来）
        Assert.Equal(0.0, cum[0]);
        Assert.Equal(route.TotalDurationSec, cum[^1]);
    }

    [Fact]
    public void Cumulative_IsMonotonicNonDecreasing()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(route);
        var cum = route!.CumulativeDurationsSec.Span;
        for (int i = 0; i < cum.Length - 1; i++)
        {
            Assert.True(cum[i] <= cum[i + 1],
                $"単調性違反: cum[{i}]={cum[i]:F6} > cum[{i + 1}]={cum[i + 1]:F6}");
        }
    }

    [Fact]
    public void Cumulative_DifficultyAreaCoveringRoute_TimingReflectedPerPoint()
    {
        // 全エッジを flooding で覆い、各 Shape 点の累積秒が baseline の 3.33 倍（1/0.3）に揃うことを検証。
        // 区間別速度低下が累積秒列に正しく反映されることのエンドツーエンド確認。
        var (from, to) = _fixture.MediumPair;
        var baseline = _fixture.Router.Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(baseline);

        var restrictions = new RestrictedAreaService();
        restrictions.AddDifficultyArea(MakePolygonCoveringShape(baseline!.Shape, marginDeg: 0.01), DifficultyTypes.Flooding);
        var router = new Router(_fixture.RouterDb, restrictions);
        var restricted = router.Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(restricted);

        var baseCum = baseline.CumulativeDurationsSec.Span;
        var restCum = restricted!.CumulativeDurationsSec.Span;
        Assert.Equal(baseCum.Length, restCum.Length);

        for (int i = 1; i < baseCum.Length; i++)
        {
            if (baseCum[i] <= 0.0) continue;
            var ratio = restCum[i] / baseCum[i];
            // car flooding speedFactor=0.3 → 累積秒の比は 1/0.3 ≈ 3.333（許容 ±2%）
            Assert.InRange(ratio, 3.27, 3.40);
        }
    }

    [Fact]
    public void Cumulative_SamePoint_TrivialRouteRespectsEndpoints()
    {
        // 同一点起点終点でも端点不変条件が破綻しないことを確認（SameEdge 早期 return 経路）
        var route = _fixture.Router.Calculate(VehicleProfile.Car, _fixture.SamePoint, _fixture.SamePoint);
        Assert.NotNull(route);
        var cum = route!.CumulativeDurationsSec.Span;
        Assert.Equal(route.Shape.Length, cum.Length);
        Assert.Equal(0.0, cum[0]);
        Assert.Equal(route.TotalDurationSec, cum[^1]);
    }

    [Fact]
    public void Cumulative_LegacyConstructor_GeneratesLinearFallbackWithExactEndpoints()
    {
        // Phase 1 互換 3 引数コンストラクタ: 累積秒は線形補間フォールバックだが、端点厳密一致は維持される
        var shape = new[]
        {
            new GeoCoordinate(35.0, 136.0),
            new GeoCoordinate(35.1, 136.0),
            new GeoCoordinate(35.2, 136.0),
        };
        var route = new Route(totalDistanceM: 30000, totalDurationSec: 1200, shape: shape.AsMemory());
        var cum = route.CumulativeDurationsSec.Span;
        Assert.Equal(shape.Length, cum.Length);
        Assert.Equal(0.0, cum[0]);
        Assert.Equal(1200.0, cum[^1]);
        // 単調性も保証
        for (int i = 0; i < cum.Length - 1; i++) Assert.True(cum[i] <= cum[i + 1]);
    }

    /// <summary>シェイプ全体を margin 度だけ広げた外接矩形ポリゴンを作る（RestrictedRoutingTests 同等の補助関数）。</summary>
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
}