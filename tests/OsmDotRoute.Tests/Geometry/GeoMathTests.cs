using OsmDotRoute;
using OsmDotRoute.Geometry;
using Xunit;

namespace OsmDotRoute.Tests.Geometry;

/// <summary>
/// Phase 3 ステップ 3A.5a — <see cref="GeoMath"/> ヘルパの単体テスト 8 件（計画書 §4.5.1）。
/// </summary>
public sealed class GeoMathTests
{
    [Fact]
    public void HaversineMeters_SamePoint_ReturnsZero()
    {
        var p = new GeoCoordinate(35.18, 136.74);
        var d = GeoMath.HaversineMeters(p, p);
        Assert.Equal(0.0, d, precision: 6);
    }

    [Fact]
    public void HaversineMeters_KnownPair_MatchesReferenceValue()
    {
        // 東京駅 ↔ 大阪駅、参考値 ≒ 403.4 km (国土地理院距離計算サービスより)
        var tokyo = new GeoCoordinate(35.681236, 139.767125);
        var osaka = new GeoCoordinate(34.702485, 135.495951);
        var d = GeoMath.HaversineMeters(tokyo, osaka);
        Assert.InRange(d, 402_000.0, 405_000.0);
    }

    [Fact]
    public void PointToSegment_QueryOnEndpointA_ReturnsZeroDistanceAndT0()
    {
        var a = new GeoCoordinate(35.18, 136.74);
        var b = new GeoCoordinate(35.19, 136.75);
        var (dist, proj, t) = GeoMath.PointToSegment(a, a, b);
        Assert.InRange(dist, 0.0, 1e-6);
        Assert.Equal(0.0, t, precision: 6);
        Assert.Equal(a.Latitude, proj.Latitude, precision: 9);
        Assert.Equal(a.Longitude, proj.Longitude, precision: 9);
    }

    [Fact]
    public void PointToSegment_QueryOnEndpointB_ReturnsZeroDistanceAndT1()
    {
        var a = new GeoCoordinate(35.18, 136.74);
        var b = new GeoCoordinate(35.19, 136.75);
        var (dist, proj, t) = GeoMath.PointToSegment(b, a, b);
        Assert.InRange(dist, 0.0, 1e-6);
        Assert.Equal(1.0, t, precision: 6);
        Assert.Equal(b.Latitude, proj.Latitude, precision: 9);
        Assert.Equal(b.Longitude, proj.Longitude, precision: 9);
    }

    [Fact]
    public void PointToSegment_QueryOnSegmentMidpoint_ReturnsZeroDistanceAndT05()
    {
        var a = new GeoCoordinate(35.18, 136.74);
        var b = new GeoCoordinate(35.19, 136.75);
        var mid = new GeoCoordinate((a.Latitude + b.Latitude) * 0.5, (a.Longitude + b.Longitude) * 0.5);
        var (dist, proj, t) = GeoMath.PointToSegment(mid, a, b);
        Assert.InRange(dist, 0.0, 1e-3);  // 平面化誤差 mm オーダー
        Assert.Equal(0.5, t, precision: 4);
        Assert.Equal(mid.Latitude, proj.Latitude, precision: 8);
        Assert.Equal(mid.Longitude, proj.Longitude, precision: 8);
    }

    [Fact]
    public void PointToSegment_QueryOutsideSegmentBeforeA_ClampsToA()
    {
        var a = new GeoCoordinate(35.18, 136.74);
        var b = new GeoCoordinate(35.19, 136.75);
        // a から b と逆方向に伸ばした点
        var beforeA = new GeoCoordinate(35.17, 136.73);
        var (dist, proj, t) = GeoMath.PointToSegment(beforeA, a, b);
        Assert.Equal(0.0, t, precision: 6);
        // proj は a にクランプ
        Assert.Equal(a.Latitude, proj.Latitude, precision: 8);
        Assert.Equal(a.Longitude, proj.Longitude, precision: 8);
        // 距離は beforeA から a までの実距離
        var expectedDist = GeoMath.HaversineMeters(beforeA, a);
        Assert.InRange(dist, expectedDist * 0.999, expectedDist * 1.001);
    }

    [Fact]
    public void PointToSegment_QueryPerpendicularToMidpoint_ReturnsPerpendicularDistance()
    {
        // 経度に沿った水平線分 (a から b は東向き)、中点から北に約 100m 離した点
        var a = new GeoCoordinate(35.18, 136.74);
        var b = new GeoCoordinate(35.18, 136.75);
        // 中点から北に 100m: dLat = 100 / 111320 ≈ 0.000898
        var (dLat100, _) = GeoMath.MetersToBboxDegrees(100.0, 35.18);
        var query = new GeoCoordinate(35.18 + dLat100, (a.Longitude + b.Longitude) * 0.5);
        var (dist, _, t) = GeoMath.PointToSegment(query, a, b);
        Assert.InRange(t, 0.49, 0.51);  // 中点 ≈ 0.5
        Assert.InRange(dist, 99.0, 101.0);  // ≈ 100 m ± 1 m
    }

    [Fact]
    public void PointToSegment_DegenerateSegment_ZeroLength_ReturnsDistanceToA()
    {
        var a = new GeoCoordinate(35.18, 136.74);
        var query = new GeoCoordinate(35.19, 136.75);
        var (dist, proj, t) = GeoMath.PointToSegment(query, a, a);
        Assert.Equal(0.0, t, precision: 6);
        Assert.Equal(a.Latitude, proj.Latitude, precision: 9);
        Assert.Equal(a.Longitude, proj.Longitude, precision: 9);
        var expected = GeoMath.HaversineMeters(query, a);
        Assert.InRange(dist, expected * 0.999, expected * 1.001);
    }
}
