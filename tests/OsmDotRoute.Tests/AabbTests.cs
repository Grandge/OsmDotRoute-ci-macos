using OsmDotRoute.Geometry;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 1 ステップ 8「制約管理基盤」の <see cref="Aabb"/> 単体テスト。
/// 交差判定（REQ-RST-014）・点包含・線分交差・Union・FromCoordinates を検証する。
/// </summary>
public class AabbTests
{
    private static Aabb MakeBox(double minLat, double minLon, double maxLat, double maxLon)
        => new(new GeoCoordinate(minLat, minLon), new GeoCoordinate(maxLat, maxLon));

    [Fact]
    public void Intersects_Returns_True_For_Overlapping_Boxes()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        var b = MakeBox(35.5, 139.5, 36.5, 140.5);
        Assert.True(a.Intersects(b));
        Assert.True(b.Intersects(a));
    }

    [Fact]
    public void Intersects_Returns_True_For_Touching_Boxes_On_Edge()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        var b = MakeBox(36.0, 139.0, 37.0, 140.0);
        Assert.True(a.Intersects(b));
    }

    [Fact]
    public void Intersects_Returns_False_For_Disjoint_Boxes()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        var b = MakeBox(36.5, 140.5, 37.0, 141.0);
        Assert.False(a.Intersects(b));
    }

    [Fact]
    public void Contains_Returns_True_For_Inner_Point()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        Assert.True(a.Contains(new GeoCoordinate(35.5, 139.5)));
    }

    [Fact]
    public void Contains_Returns_True_On_Boundary()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        Assert.True(a.Contains(new GeoCoordinate(35.0, 139.0)));
        Assert.True(a.Contains(new GeoCoordinate(36.0, 140.0)));
    }

    [Fact]
    public void Contains_Returns_False_For_Outside_Point()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        Assert.False(a.Contains(new GeoCoordinate(34.9, 139.5)));
        Assert.False(a.Contains(new GeoCoordinate(35.5, 140.5)));
    }

    [Fact]
    public void IntersectsSegment_Returns_True_When_Endpoint_Inside()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        var p1 = new GeoCoordinate(35.5, 139.5);     // 内部
        var p2 = new GeoCoordinate(37.0, 141.0);     // 外部
        Assert.True(a.IntersectsSegment(p1, p2));
    }

    [Fact]
    public void IntersectsSegment_Returns_True_When_Segment_Crosses_Box()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        var p1 = new GeoCoordinate(34.0, 139.5);     // 南外部
        var p2 = new GeoCoordinate(37.0, 139.5);     // 北外部、直線が箱を貫通
        Assert.True(a.IntersectsSegment(p1, p2));
    }

    [Fact]
    public void IntersectsSegment_Returns_False_When_Segment_Misses_Box()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        var p1 = new GeoCoordinate(34.0, 138.0);
        var p2 = new GeoCoordinate(34.5, 138.5);
        Assert.False(a.IntersectsSegment(p1, p2));
    }

    [Fact]
    public void IntersectsSegment_Returns_True_When_Segment_Touches_Edge()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        var p1 = new GeoCoordinate(36.0, 138.0);
        var p2 = new GeoCoordinate(36.0, 141.0);     // 上辺と重なる
        Assert.True(a.IntersectsSegment(p1, p2));
    }

    [Fact]
    public void Union_Returns_Box_Containing_Both()
    {
        var a = MakeBox(35.0, 139.0, 36.0, 140.0);
        var b = MakeBox(35.5, 139.5, 37.0, 141.0);
        var u = a.Union(b);
        Assert.Equal(35.0, u.MinLatitude);
        Assert.Equal(139.0, u.MinLongitude);
        Assert.Equal(37.0, u.MaxLatitude);
        Assert.Equal(141.0, u.MaxLongitude);
    }

    [Fact]
    public void FromCoordinates_Computes_BoundingBox()
    {
        var coords = new[]
        {
            new GeoCoordinate(35.0, 139.0),
            new GeoCoordinate(36.5, 140.5),
            new GeoCoordinate(35.5, 138.5),
        };
        var box = Aabb.FromCoordinates(coords);
        Assert.Equal(35.0, box.MinLatitude);
        Assert.Equal(138.5, box.MinLongitude);
        Assert.Equal(36.5, box.MaxLatitude);
        Assert.Equal(140.5, box.MaxLongitude);
    }

    [Fact]
    public void FromCoordinates_Throws_On_Empty()
    {
        Assert.Throws<ArgumentException>(() => Aabb.FromCoordinates(Array.Empty<GeoCoordinate>()));
    }
}
