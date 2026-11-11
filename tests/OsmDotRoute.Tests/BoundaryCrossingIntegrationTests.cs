using OsmDotRoute.Tests.Native;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// 実データ（津島市 .odrg）経路に対する範囲境界交点判定の検証（REQ-RTE-010〜013、Ver. 1.3.0）。
/// </summary>
public class BoundaryCrossingIntegrationTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public BoundaryCrossingIntegrationTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>指定座標を中心とする半径 <paramref name="halfSpanDeg"/> 度の矩形範囲。</summary>
    private static MapBounds BoxAround(GeoCoordinate center, double halfSpanDeg)
        => new(
            new GeoCoordinate(center.Latitude - halfSpanDeg, center.Longitude - halfSpanDeg),
            new GeoCoordinate(center.Latitude + halfSpanDeg, center.Longitude + halfSpanDeg));

    [Fact]
    public void CalculateBoundaryCrossing_NullProfile_ThrowsArgumentNullException()
    {
        var (from, to) = _fixture.MediumPair;
        Assert.Throws<ArgumentNullException>(() => _fixture.Router.CalculateBoundaryCrossing(
            null!, from, to, BoxAround(from, 0.002)));
    }

    [Fact]
    public void CalculateBoundaryCrossing_InvalidBounds_ReturnsInvalidParameter()
    {
        var (from, to) = _fixture.MediumPair;
        var inverted = new MapBounds(
            new GeoCoordinate(from.Latitude + 0.01, from.Longitude + 0.01),
            new GeoCoordinate(from.Latitude - 0.01, from.Longitude - 0.01));

        var result = _fixture.Router.CalculateBoundaryCrossing(VehicleProfile.Car, from, to, inverted);

        Assert.Equal(BoundaryCrossingKind.InvalidParameter, result.Kind);
    }

    [Fact]
    public void CalculateBoundaryCrossing_UnroutablePoint_ReturnsRouteSearchError()
    {
        var stats = _fixture.RouterDb.GetStatistics();
        var farPoint = new GeoCoordinate(stats.NorthEast.Latitude + 5.0, stats.NorthEast.Longitude + 5.0);
        var inPoint = _fixture.SamePoint;

        var result = _fixture.Router.CalculateBoundaryCrossing(
            VehicleProfile.Car, inPoint, farPoint, BoxAround(inPoint, 0.002));

        Assert.Equal(BoundaryCrossingKind.RouteSearchError, result.Kind);
    }

    [Fact]
    public void CalculateBoundaryCrossing_DestinationOutsideBox_ReturnsPointBOutside()
    {
        var (from, to) = _fixture.MediumPair;
        // 起点まわり約 ±220m の矩形。~1km 離れた終点は範囲外になる。
        var bounds = BoxAround(from, 0.002);
        Assert.True(bounds.Contains(from));
        Assert.False(bounds.Contains(to));

        var result = _fixture.Router.CalculateBoundaryCrossing(VehicleProfile.Car, from, to, bounds);

        Assert.Equal(BoundaryCrossingKind.PointBOutside, result.Kind);
        Assert.NotNull(result.Crossing);

        // 交点は矩形の 4 辺のいずれかの上にある
        var c = result.Crossing!.Value;
        bool onEdge =
            Math.Abs(c.Latitude - bounds.MinLatitude) < 1e-9 ||
            Math.Abs(c.Latitude - bounds.MaxLatitude) < 1e-9 ||
            Math.Abs(c.Longitude - bounds.MinLongitude) < 1e-9 ||
            Math.Abs(c.Longitude - bounds.MaxLongitude) < 1e-9;
        Assert.True(onEdge, $"交点が矩形辺上にない: {c.Latitude}, {c.Longitude}");
        Assert.True(bounds.Contains(c), "交点が範囲内（境界含む）にない");

        var route = _fixture.Router.Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(route);
        Assert.InRange(result.DistanceToOutsidePointM!.Value, 1.0, route!.TotalDistanceM * 1.05);
        Assert.InRange(result.DurationToOutsidePointSec!.Value, 0.0, route.TotalDurationSec * 1.05);
    }

    [Fact]
    public void CalculateBoundaryCrossing_OriginOutsideBox_ReturnsPointAOutside()
    {
        var (from, to) = _fixture.MediumPair;
        var bounds = BoxAround(to, 0.002);
        Assert.False(bounds.Contains(from));
        Assert.True(bounds.Contains(to));

        var result = _fixture.Router.CalculateBoundaryCrossing(VehicleProfile.Car, from, to, bounds);

        Assert.Equal(BoundaryCrossingKind.PointAOutside, result.Kind);
        Assert.True(bounds.Contains(result.Crossing!.Value));
        Assert.True(result.DistanceToOutsidePointM!.Value > 0.0);
    }

    [Fact]
    public void CalculateBoundaryCrossing_BothInsideWideBox_ReturnsBothInside()
    {
        var (from, to) = _fixture.MediumPair;
        var stats = _fixture.RouterDb.GetStatistics();
        // 地図全域を覆う矩形（ルートが範囲外へ出ることはない）
        var bounds = new MapBounds(
            new GeoCoordinate(stats.SouthWest.Latitude - 0.01, stats.SouthWest.Longitude - 0.01),
            new GeoCoordinate(stats.NorthEast.Latitude + 0.01, stats.NorthEast.Longitude + 0.01));

        var result = _fixture.Router.CalculateBoundaryCrossing(VehicleProfile.Car, from, to, bounds);

        Assert.Equal(BoundaryCrossingKind.BothInside, result.Kind);
        Assert.Null(result.Crossing);
    }

    [Fact]
    public void CalculateBoundaryCrossing_MatchesFindBoundaryCrossingOnSameRoute()
    {
        var (from, to) = _fixture.MediumPair;
        var bounds = BoxAround(from, 0.002);

        var viaRouter = _fixture.Router.CalculateBoundaryCrossing(VehicleProfile.Car, from, to, bounds);
        var route = _fixture.Router.Calculate(VehicleProfile.Car, from, to);
        var viaGeometry = Router.FindBoundaryCrossing(route!, from, to, bounds);

        Assert.Equal(viaRouter, viaGeometry);
    }
}
