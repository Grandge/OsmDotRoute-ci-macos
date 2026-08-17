using Microsoft.Extensions.DependencyInjection;
using OsmDotRoute.Extensions.DependencyInjection;
using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 3 ステップ 3C.3「AddOsmDotRoute(odrgPath) DI 登録」の検証テスト（REQ-MAP-009）。
/// </summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddOsmDotRoute_OdrgPath_RegistersRouterDbAndRouter()
    {
        var services = new ServiceCollection();
        services.AddOsmDotRoute(TestPaths.TsushimaOdrg);
        using var provider = services.BuildServiceProvider();

        var routerDb = provider.GetRequiredService<RouterDb>();
        Assert.NotNull(routerDb);
        var stats = routerDb.GetStatistics();
        Assert.True(stats.VertexCount > 0);
        Assert.True(stats.EdgeCount > 0);

        var router = provider.GetRequiredService<Router>();
        Assert.NotNull(router);

        var restrictions = provider.GetRequiredService<RestrictedAreaService>();
        Assert.NotNull(restrictions);
    }

    [Fact]
    public void AddOsmDotRoute_NullPath_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddOsmDotRoute((string)null!));
    }

    [Fact]
    public void AddOsmDotRoute_EmptyPath_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddOsmDotRoute(""));
        Assert.Throws<ArgumentException>(() => services.AddOsmDotRoute("   "));
    }

    [Fact]
    public void AddOsmDotRoute_OptionsWithoutOdrgPath_ThrowsInvalidOperation()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddOsmDotRoute(_ => { }));
    }

    [Fact]
    public void AddOsmDotRoute_OptionsWithOdrgPath_ResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddOsmDotRoute(options =>
        {
            options.OdrgPath = TestPaths.TsushimaOdrg;
            options.DefaultProfile = VehicleProfile.Car;
        });
        using var provider = services.BuildServiceProvider();

        var router = provider.GetRequiredService<Router>();
        Assert.NotNull(router);
    }
}
