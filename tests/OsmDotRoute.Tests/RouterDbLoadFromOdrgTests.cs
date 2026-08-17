using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 3 ステップ 3C.1「RouterDb.LoadFromOdrg public static factory」の検証テスト。
/// </summary>
public sealed class RouterDbLoadFromOdrgTests
{
    [Fact]
    public void LoadFromOdrg_ValidTsushimaOdrg_ReturnsRouterDb()
    {
        var routerDb = RouterDb.LoadFromOdrg(TestPaths.TsushimaOdrg);
        Assert.NotNull(routerDb);

        var stats = routerDb.GetStatistics();
        Assert.True(stats.VertexCount > 0);
        Assert.True(stats.EdgeCount > 0);
    }

    [Fact]
    public void LoadFromOdrg_NullPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => RouterDb.LoadFromOdrg((string)null!));
    }

    [Fact]
    public void LoadFromOdrg_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => RouterDb.LoadFromOdrg(""));
        Assert.Throws<ArgumentException>(() => RouterDb.LoadFromOdrg("   "));
    }

    [Fact]
    public void LoadFromOdrg_NonExistentPath_ThrowsFileNotFoundException()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".odrg");
        Assert.Throws<FileNotFoundException>(() => RouterDb.LoadFromOdrg(fakePath));
    }

    [Fact]
    public void LoadFromOdrg_InvalidFormat_ThrowsOdrgFormatException()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".odrg");
        File.WriteAllBytes(fakePath, new byte[] { 0x00, 0x01, 0x02, 0x03 });    // マジックバイト不一致
        try
        {
            Assert.Throws<OdrgFormatException>(() => RouterDb.LoadFromOdrg(fakePath));
        }
        finally
        {
            File.Delete(fakePath);
        }
    }

    [Fact]
    public void LoadFromOdrg_RouterCalculatesRoute_E2E()
    {
        // 内部 NativeRoadGraph + NativeRoadSnapper が動作し、Router で経路計算できる E2E
        var routerDb = RouterDb.LoadFromOdrg(TestPaths.TsushimaOdrg);
        var router = new Router(routerDb);

        // 津島市範囲内の座標で経路計算（Native 系既存テストの座標を流用）
        var from = new GeoCoordinate(35.18, 136.73);
        var to = new GeoCoordinate(35.19, 136.74);

        var route = router.Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(route);
        Assert.True(route!.TotalDistanceM > 0);
        Assert.True(route.Shape.Length >= 2);
    }
}
