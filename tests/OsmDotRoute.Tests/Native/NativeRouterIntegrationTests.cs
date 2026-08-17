using OsmDotRoute.Geometry;
using OsmDotRoute.Native;
using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests.Native;

/// <summary>
/// Phase 3 ステップ 3A.6 — Native 系統 (<see cref="NativeRoadGraph"/> + <see cref="NativeRoadSnapper"/>) を
/// <see cref="OsmDotRoute.RouterDb"/> / <see cref="OsmDotRoute.Router"/> に組み込んだ場合の自己整合性検証 16 件
/// （計画書 §4.6-B、Q3 / Q5）。
/// </summary>
/// <remarks>
/// <para>
/// 計画書 v0.10 §2.13 / §2.14 の Q1 で確定したとおり、Itinero との突合は技術的に不可能
/// (.odrg と Itinero RouterDb の頂点 ID / エッジ ID が独立採番) のため、
/// Native 単体の不変量検証 + Phase 1 既存 526 件 (Itinero 系) の全 pass 維持で並存証明を代替する。
/// </para>
/// <para>
/// テスト 13 (<c>Route_ShapeIsContinuous</c>) は計画書 §4.6-B の「<c>Route_SegmentConnectivity</c>」からの
/// 軽微逸脱: <see cref="OsmDotRoute.Route"/> 型に <c>RouteSegment</c> / <c>Segments</c> プロパティが
/// 存在しない (TotalDistanceM / TotalDurationSec / Shape のみ) ため、Shape の隣接点間 Haversine 距離合計が
/// 経路総距離と整合することを連続性の代替不変量として検証する。計画書 v0.10 自体に
/// 「(RouteSegment が存在する場合)」の注釈があり想定済の逸脱パターン。
/// </para>
/// </remarks>
public sealed class NativeRouterIntegrationTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public NativeRouterIntegrationTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    // ====== Smoke 5 件 ======

    [Fact]
    public void Calculate_SamePoint_ReturnsTinyRoute()
    {
        var route = _fixture.Router.Calculate(_fixture.Car, _fixture.SamePoint, _fixture.SamePoint);
        Assert.NotNull(route);
        Assert.InRange(route!.TotalDistanceM, 0.0, 50.0);
    }

    [Fact]
    public void Calculate_ShortDistance_ReturnsRoute()
    {
        var (from, to) = _fixture.ShortPair;
        var route = _fixture.Router.Calculate(_fixture.Car, from, to);
        Assert.NotNull(route);
    }

    [Fact]
    public void Calculate_MediumDistance_ReturnsRoute()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(_fixture.Car, from, to);
        Assert.NotNull(route);
    }

    [Fact]
    public void Calculate_FromOutsideBounds_ReturnsNull()
    {
        var far = new GeoCoordinate(89.0, 0.0);
        var route = _fixture.Router.Calculate(_fixture.Car, far, _fixture.SamePoint);
        Assert.Null(route);
    }

    [Fact]
    public void Calculate_ToOutsideBounds_ReturnsNull()
    {
        var far = new GeoCoordinate(89.0, 0.0);
        var route = _fixture.Router.Calculate(_fixture.Car, _fixture.SamePoint, far);
        Assert.Null(route);
    }

    // ====== 不変量 8 件 ======

    [Fact]
    public void Route_TotalDistanceIsPositive()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(_fixture.Car, from, to);
        Assert.NotNull(route);
        Assert.True(route!.TotalDistanceM > 0.0);
    }

    [Fact]
    public void Route_FirstShapePointNearStart()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(_fixture.Car, from, to);
        Assert.NotNull(route);
        Assert.True(route!.Shape.Length > 0);
        var dist = GeoMath.HaversineMeters(from, route.Shape.Span[0]);
        Assert.InRange(dist, 0.0, 600.0);
    }

    [Fact]
    public void Route_LastShapePointNearEnd()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(_fixture.Car, from, to);
        Assert.NotNull(route);
        Assert.True(route!.Shape.Length > 0);
        var dist = GeoMath.HaversineMeters(to, route.Shape.Span[^1]);
        Assert.InRange(dist, 0.0, 600.0);
    }

    [Fact]
    public void Route_StraightLineDistanceLeqRouteDistance()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(_fixture.Car, from, to);
        Assert.NotNull(route);
        var straight = GeoMath.HaversineMeters(from, to);
        Assert.True(straight <= route!.TotalDistanceM + 1.0,
            $"直線距離 {straight:F1}m が経路距離 {route.TotalDistanceM:F1}m を超えている");
    }

    [Fact]
    public void Route_ShapeIsNotEmpty()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(_fixture.Car, from, to);
        Assert.NotNull(route);
        Assert.True(route!.Shape.Length > 0);
    }

    [Fact]
    public void Route_ReverseDirectionApproximatelySameDistance()
    {
        var (from, to) = _fixture.MediumPair;
        var fwd = _fixture.Router.Calculate(_fixture.Car, from, to);
        var rev = _fixture.Router.Calculate(_fixture.Car, to, from);
        Assert.NotNull(fwd);
        Assert.NotNull(rev);
        var diff = Math.Abs(fwd!.TotalDistanceM - rev!.TotalDistanceM);
        var ratio = diff / fwd.TotalDistanceM;
        Assert.True(ratio <= 0.02,
            $"双方向距離差が ±2% を超過: fwd={fwd.TotalDistanceM:F1}m rev={rev.TotalDistanceM:F1}m diff={ratio:P2}");
    }

    [Fact]
    public void Route_DeterministicForSameInput()
    {
        var (from, to) = _fixture.MediumPair;
        var r1 = _fixture.Router.Calculate(_fixture.Car, from, to);
        var r2 = _fixture.Router.Calculate(_fixture.Car, from, to);
        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(r1!.TotalDistanceM, r2!.TotalDistanceM);
        Assert.Equal(r1.Shape.Length, r2.Shape.Length);
    }

    [Fact]
    public void Route_ShapeIsContinuous()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(_fixture.Car, from, to);
        Assert.NotNull(route);
        var shape = route!.Shape.Span;
        Assert.True(shape.Length >= 2,
            $"中距離経路で Shape 点数が {shape.Length} 件 (>= 2 期待)");

        double sumJump = 0.0;
        for (int i = 0; i < shape.Length - 1; i++)
        {
            sumJump += GeoMath.HaversineMeters(shape[i], shape[i + 1]);
        }
        // Shape の隣接点 Haversine 合計 ≈ TotalDistanceM (DistanceM は .odrg 焼成時の Haversine 積算)
        // スナップ部分シェイプの含み方で多少の差異が出るため ±20% 許容
        var ratio = sumJump / route.TotalDistanceM;
        Assert.InRange(ratio, 0.8, 1.2);
    }

    // ====== RouterDb コンストラクタ 2 件 ======

    [Fact]
    public void RouterDb_ConstructWithNativeGraphAndSnapper_DoesNotThrow()
    {
        using var graph = new NativeRoadGraph(TestPaths.TsushimaOdrg);
        var snapper = new NativeRoadSnapper(graph);
        var routerDb = new OsmDotRoute.RouterDb(graph, snapper);
        Assert.NotNull(routerDb);
    }

    [Fact]
    public void RouterDb_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new OsmDotRoute.RouterDb(null!, _fixture.Snapper));
        Assert.Throws<ArgumentNullException>(
            () => new OsmDotRoute.RouterDb(_fixture.Graph, null!));
    }

    // ====== Fixture sanity 1 件 ======

    [Fact]
    public void Fixture_Initializes_WithoutException()
    {
        Assert.NotNull(_fixture.Graph);
        Assert.NotNull(_fixture.Snapper);
        Assert.NotNull(_fixture.Truth);
        Assert.NotNull(_fixture.RouterDb);
        Assert.NotNull(_fixture.Router);
        Assert.NotNull(_fixture.Car);
        Assert.NotEqual(default, _fixture.SamePoint);
        Assert.NotEqual(default, _fixture.ShortPair.From);
        Assert.NotEqual(default, _fixture.ShortPair.To);
        Assert.NotEqual(default, _fixture.MediumPair.From);
        Assert.NotEqual(default, _fixture.MediumPair.To);
    }
}
