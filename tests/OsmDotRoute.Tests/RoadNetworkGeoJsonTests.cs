using System.Text.Json;
using OsmDotRoute.Tests.Native;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// 道路ネットワーク GeoJSON 出力の検証テスト（Phase 1 ステップ 6 起源、REQ-RTE-004、
/// Phase 3 ステップ 3C.2 で .odrg（津島市）ベースに書換、Itinero 直接列挙比較テストは廃止）。
/// </summary>
public class RoadNetworkGeoJsonTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public RoadNetworkGeoJsonTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetRoadNetworkGeoJson_ProducesValidJsonRoundtrip()
    {
        var geoJson = _fixture.Router.GetRoadNetworkGeoJson();
        Assert.NotNull(geoJson);
        Assert.False(string.IsNullOrWhiteSpace(geoJson.Json));

        using var doc = JsonDocument.Parse(geoJson.Json);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal("FeatureCollection", root.GetProperty("type").GetString());

        var features = root.GetProperty("features");
        Assert.Equal(JsonValueKind.Array, features.ValueKind);
        Assert.True(features.GetArrayLength() > 0, "features が 0 件");

        // 先頭 feature のスキーマ確認
        var first = features[0];
        Assert.Equal("Feature", first.GetProperty("type").GetString());
        var geometry = first.GetProperty("geometry");
        Assert.Equal("LineString", geometry.GetProperty("type").GetString());
        var coords = geometry.GetProperty("coordinates");
        Assert.True(coords.GetArrayLength() >= 2, "LineString は少なくとも 2 点必要");
        var firstCoord = coords[0];
        Assert.Equal(2, firstCoord.GetArrayLength());
        var lon = firstCoord[0].GetDouble();
        var lat = firstCoord[1].GetDouble();
        // 日本領域内（REQ-NFR-009）
        Assert.InRange(lon, 122.0, 154.0);
        Assert.InRange(lat, 20.0, 46.0);

        Assert.True(first.TryGetProperty("properties", out var props));
        Assert.Equal(JsonValueKind.Object, props.ValueKind);
    }

    [Fact]
    public void GetRoadNetworkGeoJson_FeatureCount_EqualsGraphEdgeCount()
    {
        var stats = _fixture.RouterDb.GetStatistics();

        var geoJson = _fixture.Router.GetRoadNetworkGeoJson();
        using var doc = JsonDocument.Parse(geoJson.Json);
        var featureCount = doc.RootElement.GetProperty("features").GetArrayLength();

        // RouterDbStatistics.EdgeCount は重複排除済みの辺数。features 数と一致するはず。
        Assert.Equal(stats.EdgeCount, featureCount);
    }

    [Fact]
    public void GetRoadNetworkGeoJson_AllCoordinatesInJapanBounds()
    {
        var geoJson = _fixture.Router.GetRoadNetworkGeoJson();
        using var doc = JsonDocument.Parse(geoJson.Json);
        var features = doc.RootElement.GetProperty("features");

        // 先頭 100 features を抜き取り検査
        var limit = Math.Min(100, features.GetArrayLength());
        for (int i = 0; i < limit; i++)
        {
            var coords = features[i].GetProperty("geometry").GetProperty("coordinates");
            foreach (var c in coords.EnumerateArray())
            {
                var lon = c[0].GetDouble();
                var lat = c[1].GetDouble();
                Assert.InRange(lon, 122.0, 154.0);
                Assert.InRange(lat, 20.0, 46.0);
            }
        }
    }
}
