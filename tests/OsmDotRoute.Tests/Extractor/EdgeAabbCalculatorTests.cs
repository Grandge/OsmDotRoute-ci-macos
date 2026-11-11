using System;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.5 — <see cref="EdgeAabbCalculator.Compute"/> の AABB 計算テスト。
/// </summary>
public sealed class EdgeAabbCalculatorTests
{
    private static GeoCoordinate C(double lat, double lon) => new(lat, lon);

    [Fact]
    public void TwoPointsNoShape_BboxOfEndpoints()
    {
        var aabb = EdgeAabbCalculator.Compute(
            fromVertex: C(35.16, 136.70),
            toVertex: C(35.20, 136.78),
            shape: ReadOnlySpan<GeoCoordinate>.Empty);

        Assert.Equal(136.70, aabb.MinLon);
        Assert.Equal(35.16, aabb.MinLat);
        Assert.Equal(136.78, aabb.MaxLon);
        Assert.Equal(35.20, aabb.MaxLat);
    }

    [Fact]
    public void SamePoint_ZeroAreaBbox()
    {
        var p = C(35.18, 136.74);
        var aabb = EdgeAabbCalculator.Compute(p, p, ReadOnlySpan<GeoCoordinate>.Empty);
        Assert.Equal(p.Longitude, aabb.MinLon);
        Assert.Equal(p.Longitude, aabb.MaxLon);
        Assert.Equal(p.Latitude, aabb.MinLat);
        Assert.Equal(p.Latitude, aabb.MaxLat);
    }

    [Fact]
    public void ShapeExtendsBeyondEndpoints_BboxIncludesShape()
    {
        // from と to は直線上だが、shape が外側に大きく出っ張る
        var aabb = EdgeAabbCalculator.Compute(
            fromVertex: C(35.0, 136.0),
            toVertex: C(35.0, 137.0),
            shape: new[]
            {
                C(36.0, 136.5),  // 北に出っ張る
                C(34.0, 136.5),  // 南に出っ張る
            });

        Assert.Equal(136.0, aabb.MinLon);
        Assert.Equal(137.0, aabb.MaxLon);
        Assert.Equal(34.0, aabb.MinLat);
        Assert.Equal(36.0, aabb.MaxLat);
    }

    [Fact]
    public void NegativeCoordinates_HandledCorrectly()
    {
        var aabb = EdgeAabbCalculator.Compute(
            fromVertex: C(-33.5, -74.05),
            toVertex: C(-33.4, -73.95),
            shape: ReadOnlySpan<GeoCoordinate>.Empty);

        Assert.Equal(-74.05, aabb.MinLon);
        Assert.Equal(-33.5, aabb.MinLat);
        Assert.Equal(-73.95, aabb.MaxLon);
        Assert.Equal(-33.4, aabb.MaxLat);
    }

    [Fact]
    public void EndpointsInWrongOrder_StillProducesCorrectBbox()
    {
        // from の方が大きい値でも min/max は正しく取れる
        var aabb = EdgeAabbCalculator.Compute(
            fromVertex: C(35.20, 136.78),
            toVertex: C(35.16, 136.70),
            shape: ReadOnlySpan<GeoCoordinate>.Empty);

        Assert.Equal(136.70, aabb.MinLon);
        Assert.Equal(35.16, aabb.MinLat);
        Assert.Equal(136.78, aabb.MaxLon);
        Assert.Equal(35.20, aabb.MaxLat);
    }

    [Fact]
    public void ManyShapePoints_BboxIsTrueMinMax()
    {
        var shape = new GeoCoordinate[100];
        for (int i = 0; i < 100; i++)
            shape[i] = C(35.0 + i * 0.001, 136.0 + i * 0.001);

        var aabb = EdgeAabbCalculator.Compute(
            fromVertex: C(35.0, 136.0),
            toVertex: C(35.099, 136.099),
            shape: shape);

        Assert.Equal(136.0, aabb.MinLon);
        Assert.Equal(35.0, aabb.MinLat);
        Assert.InRange(aabb.MaxLon, 136.099 - 1e-9, 136.099 + 1e-9);
        Assert.InRange(aabb.MaxLat, 35.099 - 1e-9, 35.099 + 1e-9);
    }
}
