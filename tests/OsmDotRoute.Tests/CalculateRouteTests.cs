using OsmDotRoute.Tests.Native;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// 経路計算基本動作の検証テスト（Phase 1 ステップ 5b 起源、Phase 3 ステップ 3C.2 で .odrg（津島市）ベースに書換）。
/// Itinero RouterDb 比較セマンティクス（旧 ±10% 一致）は Phase 3 で Itinero 撤去のため廃止、
/// Native 系既存テスト [`NativeRouterIntegrationTests`](Native/NativeRouterIntegrationTests.cs) でカバー。
/// </summary>
public class CalculateRouteTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public CalculateRouteTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Calculate_NullProfile_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => _fixture.Router.Calculate(null!, _fixture.MediumPair.From, _fixture.MediumPair.To));
    }

    [Fact]
    public void Calculate_FromOutsideNetwork_ReturnsNull()
    {
        var stats = _fixture.RouterDb.GetStatistics();
        var farPoint = new GeoCoordinate(stats.NorthEast.Latitude + 5.0, stats.NorthEast.Longitude + 5.0);
        var inPoint = _fixture.SamePoint;

        // 起点がネットワーク外
        Assert.Null(_fixture.Router.Calculate(VehicleProfile.Car, farPoint, inPoint));
        // 終点がネットワーク外
        Assert.Null(_fixture.Router.Calculate(VehicleProfile.Car, inPoint, farPoint));
    }

    [Fact]
    public void Calculate_SamePoint_ReturnsTrivialOrTinyRoute()
    {
        var route = _fixture.Router.Calculate(VehicleProfile.Car, _fixture.SamePoint, _fixture.SamePoint);
        Assert.NotNull(route);
        // 同一点なら距離はごく小さい（スナップ誤差程度）
        Assert.InRange(route!.TotalDistanceM, 0.0, 50.0);
    }

    [Fact]
    public void Calculate_PedestrianProfile_ProducesValidRoute()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(VehicleProfile.Pedestrian, from, to);
        Assert.NotNull(route);
        Assert.True(route!.TotalDistanceM > 0);
        Assert.True(route.TotalDurationSec > 0);
        Assert.True(route.Shape.Length >= 2);
    }

    [Fact]
    public void Calculate_ZeroSearchDistance_ReturnsNull()
    {
        // searchDistanceM=0 ではスナップ不能 → 経路は得られない（引数がスナップへ伝播していることの検証）
        var (from, to) = _fixture.MediumPair;
        Assert.Null(_fixture.Router.Calculate(VehicleProfile.Car, from, to, searchDistanceM: 0f));
    }

    [Fact]
    public void Calculate_CustomSearchDistance_MatchesDefault()
    {
        // 既定 500m を十分に含む広い半径なら、既定指定と同一の経路になる
        var (from, to) = _fixture.MediumPair;
        var byDefault = _fixture.Router.Calculate(VehicleProfile.Car, from, to);
        var byCustom = _fixture.Router.Calculate(VehicleProfile.Car, from, to, searchDistanceM: 1000f);

        Assert.NotNull(byDefault);
        Assert.NotNull(byCustom);
        Assert.Equal(byDefault!.TotalDistanceM, byCustom!.TotalDistanceM, precision: 5);
        Assert.Equal(byDefault.Shape.Length, byCustom.Shape.Length);
    }

    [Fact]
    public void Calculate_RouteShape_StartsAtSnapFromAndEndsAtSnapTo()
    {
        var (from, to) = _fixture.MediumPair;
        var route = _fixture.Router.Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(route);

        var snappedFrom = _fixture.Router.SnapToRoad(VehicleProfile.Car, from);
        var snappedTo = _fixture.Router.SnapToRoad(VehicleProfile.Car, to);
        Assert.NotNull(snappedFrom);
        Assert.NotNull(snappedTo);

        // シェイプ先頭はスナップ後の起点座標、末尾はスナップ後の終点座標
        var routeShape = route!.Shape.Span;
        Assert.Equal(snappedFrom!.Value.Latitude, routeShape[0].Latitude, precision: 5);
        Assert.Equal(snappedFrom.Value.Longitude, routeShape[0].Longitude, precision: 5);
        Assert.Equal(snappedTo!.Value.Latitude, routeShape[^1].Latitude, precision: 5);
        Assert.Equal(snappedTo.Value.Longitude, routeShape[^1].Longitude, precision: 5);
    }
}
