using OsmDotRoute.Geometry;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 1 ステップ 8「制約管理基盤」の <see cref="PolygonIntersection"/> 単体テスト。
/// Ray Casting による点包含・線分交差・Hole 込み内外判定を検証（REQ-RST-013）。
/// </summary>
public class PolygonIntersectionTests
{
    /// <summary>(35,139)-(36,139)-(36,140)-(35,140) の単位正方形</summary>
    private static GeoPolygon UnitSquare()
    {
        var ring = new[]
        {
            new GeoCoordinate(35.0, 139.0),
            new GeoCoordinate(36.0, 139.0),
            new GeoCoordinate(36.0, 140.0),
            new GeoCoordinate(35.0, 140.0),
            new GeoCoordinate(35.0, 139.0),
        };
        return new GeoPolygon(ring);
    }

    /// <summary>外側 (35,139)-(36,140)、内側 hole (35.4,139.4)-(35.6,139.6)</summary>
    private static GeoPolygon SquareWithHole()
    {
        var outer = new[]
        {
            new GeoCoordinate(35.0, 139.0),
            new GeoCoordinate(36.0, 139.0),
            new GeoCoordinate(36.0, 140.0),
            new GeoCoordinate(35.0, 140.0),
            new GeoCoordinate(35.0, 139.0),
        };
        var hole = new[]
        {
            new GeoCoordinate(35.4, 139.4),
            new GeoCoordinate(35.6, 139.4),
            new GeoCoordinate(35.6, 139.6),
            new GeoCoordinate(35.4, 139.6),
            new GeoCoordinate(35.4, 139.4),
        };
        return new GeoPolygon(outer, new IReadOnlyList<GeoCoordinate>[] { hole });
    }

    [Fact]
    public void Contains_Inner_Point_Returns_True()
    {
        Assert.True(PolygonIntersection.Contains(UnitSquare(), new GeoCoordinate(35.5, 139.5)));
    }

    [Fact]
    public void Contains_Outer_Point_Returns_False()
    {
        Assert.False(PolygonIntersection.Contains(UnitSquare(), new GeoCoordinate(34.5, 139.5)));
        Assert.False(PolygonIntersection.Contains(UnitSquare(), new GeoCoordinate(35.5, 141.0)));
    }

    [Fact]
    public void Contains_Boundary_Point_Returns_True()
    {
        // 境界線上は内側扱い
        Assert.True(PolygonIntersection.Contains(UnitSquare(), new GeoCoordinate(35.0, 139.5)));
        Assert.True(PolygonIntersection.Contains(UnitSquare(), new GeoCoordinate(35.0, 139.0)));
    }

    [Fact]
    public void Contains_Hole_Inside_Point_Returns_False()
    {
        // hole 内部の点はポリゴン外
        Assert.False(PolygonIntersection.Contains(SquareWithHole(), new GeoCoordinate(35.5, 139.5)));
    }

    [Fact]
    public void Contains_Outside_Hole_But_Inside_Outer_Returns_True()
    {
        // hole の外、外周の内側
        Assert.True(PolygonIntersection.Contains(SquareWithHole(), new GeoCoordinate(35.1, 139.1)));
    }

    [Fact]
    public void IntersectsSegment_Segment_Inside_Polygon_Returns_True()
    {
        var p1 = new GeoCoordinate(35.2, 139.2);
        var p2 = new GeoCoordinate(35.8, 139.8);
        Assert.True(PolygonIntersection.IntersectsSegment(UnitSquare(), p1, p2));
    }

    [Fact]
    public void IntersectsSegment_Segment_Crosses_Boundary_Returns_True()
    {
        var p1 = new GeoCoordinate(34.5, 139.5);
        var p2 = new GeoCoordinate(35.5, 139.5);
        Assert.True(PolygonIntersection.IntersectsSegment(UnitSquare(), p1, p2));
    }

    [Fact]
    public void IntersectsSegment_Segment_Outside_Returns_False()
    {
        var p1 = new GeoCoordinate(34.0, 138.0);
        var p2 = new GeoCoordinate(34.5, 138.5);
        Assert.False(PolygonIntersection.IntersectsSegment(UnitSquare(), p1, p2));
    }

    [Fact]
    public void IntersectsSegment_Segment_Across_Polygon_Returns_True()
    {
        // 外部 → 外部 だが多角形を貫通
        var p1 = new GeoCoordinate(35.5, 138.5);
        var p2 = new GeoCoordinate(35.5, 140.5);
        Assert.True(PolygonIntersection.IntersectsSegment(UnitSquare(), p1, p2));
    }

    [Fact]
    public void IntersectsSegment_Segment_Inside_Hole_Returns_True_Because_Hits_Hole_Boundary()
    {
        // hole 境界を横切る線分（実領域を通る）→ 交差
        var p1 = new GeoCoordinate(35.5, 139.3);    // hole 外
        var p2 = new GeoCoordinate(35.5, 139.5);    // hole 内
        Assert.True(PolygonIntersection.IntersectsSegment(SquareWithHole(), p1, p2));
    }

    [Fact]
    public void IntersectsSegment_Segment_Wholly_Inside_Hole_Returns_False()
    {
        // hole 内に完全に収まる線分は実領域に触れないので交差なし
        var p1 = new GeoCoordinate(35.45, 139.45);
        var p2 = new GeoCoordinate(35.55, 139.55);
        Assert.False(PolygonIntersection.IntersectsSegment(SquareWithHole(), p1, p2));
    }

    [Fact]
    public void ComputeBoundingBox_Wraps_Outer_Ring()
    {
        var box = PolygonIntersection.ComputeBoundingBox(UnitSquare());
        Assert.Equal(35.0, box.MinLatitude);
        Assert.Equal(139.0, box.MinLongitude);
        Assert.Equal(36.0, box.MaxLatitude);
        Assert.Equal(140.0, box.MaxLongitude);
    }
}
