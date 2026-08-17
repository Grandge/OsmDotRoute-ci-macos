using OsmDotRoute.Tests.Native;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// 道路スナップ機能の検証テスト（Phase 1 ステップ 4 起源、REQ-RTE-002〜003 / REQ-RTE-008、
/// Phase 3 ステップ 3C.2 で .odrg（津島市）ベースに書換）。
/// </summary>
public class SnapToRoadTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public SnapToRoadTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void SnapToRoad_PointOnNetwork_ReturnsNearbyCoordinate()
    {
        // 車両通行可能な頂点座標そのままスナップ → 同一/近傍点が返る
        var vertex = _fixture.SamePoint;

        var snapped = _fixture.Router.SnapToRoad(VehicleProfile.Pedestrian, vertex, searchDistanceM: 1000f);

        Assert.NotNull(snapped);
        // 道路頂点そのものから snap した場合、誤差は緯度経度 0.001 度 (約 100m) 以内
        Assert.InRange(snapped.Value.Latitude - vertex.Latitude, -0.001, 0.001);
        Assert.InRange(snapped.Value.Longitude - vertex.Longitude, -0.001, 0.001);
    }

    [Fact]
    public void SnapToRoad_PointFarOutsideNetwork_ReturnsNull()
    {
        var stats = _fixture.RouterDb.GetStatistics();
        // 北東端から +5 度（約 555km）離れた点はネットワーク外（searchDistanceM=500m 内に道路なし）
        var farPoint = new GeoCoordinate(
            stats.NorthEast.Latitude + 5.0,
            stats.NorthEast.Longitude + 5.0);

        var snapped = _fixture.Router.SnapToRoad(VehicleProfile.Car, farPoint, searchDistanceM: 500f);

        Assert.Null(snapped);
    }

    [Fact]
    public void SnapToRoad_CarProfile_OnRoadNetwork_ReturnsCoordinate()
    {
        var carVertex = _fixture.SamePoint;     // fixture が車両通行可能頂点を保証

        var snapped = _fixture.Router.SnapToRoad(VehicleProfile.Car, carVertex, searchDistanceM: 500f);

        Assert.NotNull(snapped);
        // スナップ後の点が元の点の近傍（500m 半径以内）
        Assert.InRange(snapped.Value.Latitude - carVertex.Latitude, -0.01, 0.01);
        Assert.InRange(snapped.Value.Longitude - carVertex.Longitude, -0.01, 0.01);
    }

    [Fact]
    public void SnapToRoad_PedestrianProfile_OnRoadNetwork_ReturnsCoordinate()
    {
        var snapped = _fixture.Router.SnapToRoad(VehicleProfile.Pedestrian, _fixture.SamePoint);

        Assert.NotNull(snapped);
    }

    [Fact]
    public void SnapToRoad_NullProfile_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => _fixture.Router.SnapToRoad(null!, _fixture.SamePoint));
    }

    [Fact]
    public void SnapToRoad_DefaultSearchDistance_Is500Meters()
    {
        // 既定値 500m の動作確認: 道路頂点直上の点は必ず成功する
        var snapped = _fixture.Router.SnapToRoad(VehicleProfile.Pedestrian, _fixture.SamePoint);
        Assert.NotNull(snapped);
    }
}
