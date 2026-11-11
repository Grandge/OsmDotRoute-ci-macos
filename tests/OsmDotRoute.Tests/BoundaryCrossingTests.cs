using OsmDotRoute.Geometry;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// 範囲境界との交点判定の純幾何検証（REQ-RTE-010〜013、Ver. 1.3.0）。
/// 合成 <see cref="Route"/> を用い、ルート探索を介さず <see cref="Router.FindBoundaryCrossing"/> を検証する。
/// </summary>
public class BoundaryCrossingTests
{
    /// <summary>検証用の矩形範囲 R（南西 35.0/135.0 〜 北東 35.1/135.1）。</summary>
    private static readonly MapBounds Bounds = new(
        new GeoCoordinate(35.0, 135.0), new GeoCoordinate(35.1, 135.1));

    private const double SpeedMps = 10.0;

    /// <summary>Shape から Route を組み立てる（速度一定 10 m/s、累積秒は距離比例）。</summary>
    private static Route MakeRoute(params GeoCoordinate[] shape)
    {
        var cumulative = new double[shape.Length];
        double total = 0.0;
        for (int i = 1; i < shape.Length; i++)
        {
            total += GeoMath.HaversineMeters(shape[i - 1], shape[i]);
            cumulative[i] = total / SpeedMps;
        }
        return new Route(total, total / SpeedMps, shape, cumulative);
    }

    private static double PolylineLength(params GeoCoordinate[] points)
    {
        double sum = 0.0;
        for (int i = 1; i < points.Length; i++) sum += GeoMath.HaversineMeters(points[i - 1], points[i]);
        return sum;
    }

    // ---- 両端が範囲内 / 範囲外 ----

    [Fact]
    public void BothEndpointsInside_ReturnsBothInside()
    {
        var from = new GeoCoordinate(35.02, 135.02);
        var to = new GeoCoordinate(35.08, 135.08);
        var route = MakeRoute(from, new GeoCoordinate(35.05, 135.03), to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.BothInside, result.Kind);
        Assert.Null(result.Crossing);
        Assert.Null(result.DistanceToOutsidePointM);
        Assert.Null(result.DurationToOutsidePointSec);
    }

    [Fact]
    public void BothEndpointsOutside_RouteStaysOutside_ReturnsBothOutside()
    {
        var from = new GeoCoordinate(34.9, 134.9);
        var to = new GeoCoordinate(34.9, 135.2);
        var route = MakeRoute(from, new GeoCoordinate(34.9, 135.05), to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.BothOutside, result.Kind);
        Assert.Null(result.Crossing);
    }

    [Fact]
    public void BothEndpointsInside_RouteBulgesOutside_ReturnsRouteSearchError()
    {
        var from = new GeoCoordinate(35.05, 135.05);
        var to = new GeoCoordinate(35.06, 135.05);
        // 途中で範囲外（東側）へ膨らむ
        var route = MakeRoute(from, new GeoCoordinate(35.055, 135.2), to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.RouteSearchError, result.Kind);
    }

    [Fact]
    public void BothEndpointsOutside_RoutePassesThroughBounds_ReturnsRouteSearchError()
    {
        var from = new GeoCoordinate(35.05, 134.9);
        var to = new GeoCoordinate(35.05, 135.2);
        // 頂点はすべて範囲外だが、線分が範囲を貫通する（外側は非凸なので線分交差判定が必要なケース）
        var route = MakeRoute(from, to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.RouteSearchError, result.Kind);
    }

    // ---- 片側のみ範囲外 ----

    [Fact]
    public void PointBOutside_ReturnsCrossingOnEasternEdge()
    {
        var from = new GeoCoordinate(35.05, 135.05);
        var to = new GeoCoordinate(35.05, 135.2);
        var route = MakeRoute(from, to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.PointBOutside, result.Kind);
        Assert.NotNull(result.Crossing);
        Assert.Equal(35.05, result.Crossing!.Value.Latitude, 9);
        Assert.Equal(135.1, result.Crossing!.Value.Longitude, 9);

        double expected = GeoMath.HaversineMeters(new GeoCoordinate(35.05, 135.1), to);
        Assert.Equal(expected, result.DistanceToOutsidePointM!.Value, 3);
        Assert.Equal(expected / SpeedMps, result.DurationToOutsidePointSec!.Value, 3);
    }

    [Fact]
    public void PointAOutside_ReturnsCrossingOnWesternEdge()
    {
        var from = new GeoCoordinate(35.05, 134.8);
        var to = new GeoCoordinate(35.05, 135.05);
        var route = MakeRoute(from, to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.PointAOutside, result.Kind);
        Assert.Equal(35.05, result.Crossing!.Value.Latitude, 9);
        Assert.Equal(135.0, result.Crossing!.Value.Longitude, 9);

        double expected = GeoMath.HaversineMeters(from, new GeoCoordinate(35.05, 135.0));
        Assert.Equal(expected, result.DistanceToOutsidePointM!.Value, 3);
        Assert.Equal(expected / SpeedMps, result.DurationToOutsidePointSec!.Value, 3);
    }

    [Fact]
    public void EndpointOnBoundaryLine_IsTreatedAsInside()
    {
        // 南辺の真上にある起点は「範囲内」扱い（MapBounds.Contains は境界を含む）
        var from = new GeoCoordinate(35.0, 135.05);
        var to = new GeoCoordinate(35.2, 135.05);
        var route = MakeRoute(from, to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.PointBOutside, result.Kind);
        Assert.Equal(35.1, result.Crossing!.Value.Latitude, 9);
        Assert.Equal(135.05, result.Crossing!.Value.Longitude, 9);
    }

    [Fact]
    public void CrossingIsInterpolated_NotSnappedToNearestShapeVertex()
    {
        // 交点 (35.1, 135.05) は Shape 頂点として存在しない
        var from = new GeoCoordinate(35.09, 135.05);
        var to = new GeoCoordinate(35.30, 135.05);
        var route = MakeRoute(from, to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(35.1, result.Crossing!.Value.Latitude, 9);
        Assert.DoesNotContain(
            route.Shape.ToArray(),
            p => Math.Abs(p.Latitude - result.Crossing!.Value.Latitude) < 1e-12
              && Math.Abs(p.Longitude - result.Crossing!.Value.Longitude) < 1e-12);
    }

    [Fact]
    public void MultipleCrossings_ReturnsCrossingNearestToInsidePoint()
    {
        // A(範囲内) → 東へ出る → 北へ移動 → 範囲内へ戻る → 北へ移動 → 再び東へ出て B
        var from = new GeoCoordinate(35.05, 135.05);
        var p1 = new GeoCoordinate(35.05, 135.20);
        var p2 = new GeoCoordinate(35.06, 135.20);
        var p3 = new GeoCoordinate(35.06, 135.05);
        var p4 = new GeoCoordinate(35.07, 135.05);
        var to = new GeoCoordinate(35.07, 135.20);
        var route = MakeRoute(from, p1, p2, p3, p4, to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.PointBOutside, result.Kind);
        // 範囲内側（A）に近い側の最初の交点を採る（最後に範囲外へ出る交点ではない）
        var expectedCrossing = new GeoCoordinate(35.05, 135.1);
        Assert.Equal(expectedCrossing.Latitude, result.Crossing!.Value.Latitude, 9);
        Assert.Equal(expectedCrossing.Longitude, result.Crossing!.Value.Longitude, 9);

        // 距離は途中で範囲内に戻る区間も含む
        double expected = PolylineLength(expectedCrossing, p1, p2, p3, p4, to);
        Assert.Equal(expected, result.DistanceToOutsidePointM!.Value, 3);
        Assert.Equal(expected / SpeedMps, result.DurationToOutsidePointSec!.Value, 3);
        Assert.True(result.DistanceToOutsidePointM!.Value
            > GeoMath.HaversineMeters(expectedCrossing, to));
    }

    [Fact]
    public void SnappedStartOutsideBounds_ReturnsRouteSearchError()
    {
        // 生の起点は範囲内だが、スナップ結果（Shape 先頭）が範囲外 → 端点判定とルート形状が矛盾
        var from = new GeoCoordinate(35.0005, 135.05);
        var to = new GeoCoordinate(34.8, 135.05);
        var route = MakeRoute(new GeoCoordinate(34.999, 135.05), to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.RouteSearchError, result.Kind);
    }

    [Fact]
    public void DegenerateShape_ReturnsRouteSearchError()
    {
        var from = new GeoCoordinate(35.05, 135.05);
        var to = new GeoCoordinate(35.05, 135.2);
        var route = MakeRoute(from);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.RouteSearchError, result.Kind);
    }

    [Fact]
    public void DurationFollowsCumulativeDurations_NotUniformSpeed()
    {
        // 交点は最初の線分の中央。累積秒を非線形に与え、線分内 t 補間が使われることを確認する。
        var from = new GeoCoordinate(35.05, 135.05);
        var mid = new GeoCoordinate(35.05, 135.15);   // 交点 135.1 はこの線分の中央
        var to = new GeoCoordinate(35.05, 135.25);
        var shape = new[] { from, mid, to };
        var cumulative = new double[] { 0.0, 100.0, 120.0 };
        var route = new Route(PolylineLength(shape), 120.0, shape, cumulative);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        // 交点の累積秒 = 0 + 0.5 × (100 - 0) = 50 → 残り 120 - 50 = 70 秒
        Assert.Equal(70.0, result.DurationToOutsidePointSec!.Value, 6);
    }

    // ---- パラメータ異常 ----

    [Theory]
    [InlineData(35.1, 135.0, 35.0, 135.1)]     // 南北逆転
    [InlineData(35.0, 135.1, 35.1, 135.0)]     // 東西逆転
    [InlineData(35.0, 135.0, 35.0, 135.1)]     // 緯度幅ゼロ
    [InlineData(35.0, 135.0, 35.1, 135.0)]     // 経度幅ゼロ
    [InlineData(double.NaN, 135.0, 35.1, 135.1)] // NaN
    [InlineData(35.0, 135.0, 95.0, 135.1)]     // 緯度定義域外
    public void InvalidBounds_ReturnsInvalidParameter(double swLat, double swLon, double neLat, double neLon)
    {
        var bounds = new MapBounds(new GeoCoordinate(swLat, swLon), new GeoCoordinate(neLat, neLon));
        var from = new GeoCoordinate(35.05, 135.05);
        var to = new GeoCoordinate(35.05, 135.2);
        var route = MakeRoute(from, to);

        var result = Router.FindBoundaryCrossing(route, from, to, bounds);

        Assert.Equal(BoundaryCrossingKind.InvalidParameter, result.Kind);
    }

    [Fact]
    public void NonFiniteEndpoint_ReturnsInvalidParameter()
    {
        var from = new GeoCoordinate(double.NaN, 135.05);
        var to = new GeoCoordinate(35.05, 135.2);
        var route = MakeRoute(new GeoCoordinate(35.05, 135.05), to);

        var result = Router.FindBoundaryCrossing(route, from, to, Bounds);

        Assert.Equal(BoundaryCrossingKind.InvalidParameter, result.Kind);
    }

    [Fact]
    public void FindBoundaryCrossing_NullRoute_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Router.FindBoundaryCrossing(
            null!, new GeoCoordinate(35.05, 135.05), new GeoCoordinate(35.05, 135.2), Bounds));
    }

    // ---- MapBounds 北西/南東ファクトリ ----

    [Fact]
    public void FromNorthWestSouthEast_NormalizesToSouthWestNorthEast()
    {
        var bounds = MapBounds.FromNorthWestSouthEast(
            northWest: new GeoCoordinate(35.1, 135.0),
            southEast: new GeoCoordinate(35.0, 135.1));

        Assert.Equal(Bounds, bounds);
        Assert.Equal(35.0, bounds.MinLatitude, 9);
        Assert.Equal(135.0, bounds.MinLongitude, 9);
        Assert.Equal(35.1, bounds.MaxLatitude, 9);
        Assert.Equal(135.1, bounds.MaxLongitude, 9);
    }
}
