using OsmDotRoute.Native;
using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests.Native;

/// <summary>
/// Phase 3 ステップ 3A.3e の sanity check（3 件）。
/// 完全なパリティテスト（残り 9 件、Itinero との突合含む）は 3A.3f 担当。
/// </summary>
public sealed class NativeRoadGraphSanityTests
{
    [Fact]
    public void Constructor_LoadsTsushimaOdrg_SuccessfullyExposesStatistics()
    {
        EnsureTestData();
        using var graph = new NativeRoadGraph(TestPaths.TsushimaOdrg);

        Assert.True(graph.VertexCount > 0, "頂点数が 0 より大きい");
        Assert.True(graph.EdgeCount > 0, "辺数が 0 より大きい");

        var bounds = graph.GetBounds();
        // 津島市は愛知県西部 (おおよそ 35.13〜35.25 / 136.65〜136.80)
        Assert.InRange(bounds.SouthWest.Latitude, 35.0, 35.5);
        Assert.InRange(bounds.SouthWest.Longitude, 136.5, 137.0);
        Assert.InRange(bounds.NorthEast.Latitude, 35.0, 35.5);
        Assert.InRange(bounds.NorthEast.Longitude, 136.5, 137.0);
        Assert.True(bounds.SouthWest.Latitude <= bounds.NorthEast.Latitude);
        Assert.True(bounds.SouthWest.Longitude <= bounds.NorthEast.Longitude);
    }

    [Fact]
    public void GetVertex_AtIndexZero_ReturnsCoordinateInBounds()
    {
        EnsureTestData();
        using var graph = new NativeRoadGraph(TestPaths.TsushimaOdrg);
        var bounds = graph.GetBounds();

        var first = graph.GetVertex(0);
        Assert.InRange(first.Latitude, bounds.SouthWest.Latitude, bounds.NorthEast.Latitude);
        Assert.InRange(first.Longitude, bounds.SouthWest.Longitude, bounds.NorthEast.Longitude);
    }

    [Fact]
    public void Dispose_ThenAccess_ThrowsObjectDisposedException()
    {
        EnsureTestData();
        var graph = new NativeRoadGraph(TestPaths.TsushimaOdrg);
        graph.Dispose();

        Assert.Throws<ObjectDisposedException>(() => graph.GetVertex(0));
    }

    private static void EnsureTestData()
    {
        if (!File.Exists(TestPaths.TsushimaOdrg))
        {
            Assert.Fail($"リポジトリ同梱の津島.odrg が見つかりません: {TestPaths.TsushimaOdrg}");
        }
    }
}
