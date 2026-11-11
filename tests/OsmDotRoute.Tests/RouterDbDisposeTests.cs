using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// REQ-MAP-010「RouterDb のリソース確定解放（IDisposable）」の検証テスト。
/// 親プロFB <c>feature_request_routerdb_dispose.md</c> の受け入れ基準 1〜5 に対応する。
/// </summary>
public sealed class RouterDbDisposeTests
{
    /// <summary>テスト用に Tsushima .odrg を一時パスへコピーする（元データのロック・削除を避ける）。</summary>
    private static string CopyOdrgToTemp()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".odrg");
        File.Copy(TestPaths.TsushimaOdrg, tempPath);
        return tempPath;
    }

    [Fact]
    public void Dispose_FileVersion_ReleasesFileLock_DeleteSucceeds()
    {
        // 受け入れ基準 1: Dispose 後に File.Delete が成功すること
        var tempPath = CopyOdrgToTemp();
        var routerDb = RouterDb.LoadFromOdrg(tempPath);

        routerDb.Dispose();

        File.Delete(tempPath);    // ロック残留時は IOException
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void Dispose_FileVersion_ReleasesFileLock_CopyOverwriteSucceeds()
    {
        // 受け入れ基準 1: Dispose 後に File.Copy(..., overwrite: true) が成功すること
        //（親プロのシナリオ上書き保存フローの再現）
        var tempPath = CopyOdrgToTemp();
        var sourcePath = CopyOdrgToTemp();
        try
        {
            var routerDb = RouterDb.LoadFromOdrg(tempPath);
            routerDb.Dispose();

            File.Copy(sourcePath, tempPath, overwrite: true);    // ロック残留時は IOException
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Dispose_FileVersion_LockIsHeldBeforeDispose()
    {
        // Dispose 前は MMF がファイルをロックしている（前提確認: テスト自体の有効性担保）。
        // ロックは Windows 固有の挙動（Unix では MMF を開いたままでも unlink 可能）のため Windows 限定。
        if (!OperatingSystem.IsWindows()) return;

        var tempPath = CopyOdrgToTemp();
        var routerDb = RouterDb.LoadFromOdrg(tempPath);
        try
        {
            Assert.ThrowsAny<IOException>(() => File.Delete(tempPath));
        }
        finally
        {
            routerDb.Dispose();
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        // 受け入れ基準 2: 多重呼び出しが安全であること
        var routerDb = RouterDb.LoadFromOdrg(TestPaths.TsushimaOdrg);
        routerDb.Dispose();
        routerDb.Dispose();    // 例外にならない
    }

    [Fact]
    public void Dispose_ThenGetStatistics_ThrowsObjectDisposedException()
    {
        // 受け入れ基準 3: Dispose 後の本インスタンス使用は ObjectDisposedException
        var routerDb = RouterDb.LoadFromOdrg(TestPaths.TsushimaOdrg);
        routerDb.Dispose();

        Assert.Throws<ObjectDisposedException>(() => routerDb.GetStatistics());
    }

    [Fact]
    public void Dispose_ThenRouterCalculate_ThrowsObjectDisposedException()
    {
        // 受け入れ基準 3: Dispose 後の派生 Router 使用は ObjectDisposedException
        var routerDb = RouterDb.LoadFromOdrg(TestPaths.TsushimaOdrg);
        var router = new Router(routerDb);
        routerDb.Dispose();

        var from = new GeoCoordinate(35.18, 136.73);
        var to = new GeoCoordinate(35.19, 136.74);
        Assert.Throws<ObjectDisposedException>(() => router.Calculate(VehicleProfile.Car, from, to));
    }

    [Fact]
    public void Dispose_MemoryVersion_IsSafeAndIdempotent()
    {
        // 受け入れ基準 4: メモリ版（ピン留めバッファ）でも矛盾なく解放されること
        var data = File.ReadAllBytes(TestPaths.TsushimaOdrg);
        var routerDb = RouterDb.LoadFromOdrg((ReadOnlyMemory<byte>)data);

        var stats = routerDb.GetStatistics();
        Assert.True(stats.VertexCount > 0);

        routerDb.Dispose();
        routerDb.Dispose();    // 冪等

        Assert.Throws<ObjectDisposedException>(() => routerDb.GetStatistics());
    }

    [Fact]
    public void Using_FileVersion_WorksAsDisposable()
    {
        // 親プロ利用計画の using パターン（統計取得 → 自動解放）が成立すること
        var tempPath = CopyOdrgToTemp();
        using (var routerDb = RouterDb.LoadFromOdrg(tempPath))
        {
            var stats = routerDb.GetStatistics();
            Assert.True(stats.EdgeCount > 0);
        }
        File.Delete(tempPath);    // using 終了でロック解放済み
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void NoDispose_ExistingUsage_Unchanged()
    {
        // 受け入れ基準 5: Dispose を呼ばない既存利用コードの挙動が変わらないこと
        var routerDb = RouterDb.LoadFromOdrg(TestPaths.TsushimaOdrg);
        var router = new Router(routerDb);

        var from = new GeoCoordinate(35.18, 136.73);
        var to = new GeoCoordinate(35.19, 136.74);
        var route = router.Calculate(VehicleProfile.Car, from, to);

        Assert.NotNull(route);
        Assert.True(route!.TotalDistanceM > 0);
    }
}