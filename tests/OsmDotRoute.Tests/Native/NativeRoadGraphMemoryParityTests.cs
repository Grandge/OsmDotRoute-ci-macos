using OsmDotRoute.Native;
using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests.Native;

/// <summary>
/// Phase 3 ステップ 3J.2 — in-memory ロード（<see cref="RouterDb.LoadFromOdrg(ReadOnlyMemory{byte})"/> /
/// <see cref="NativeRoadGraph(ReadOnlyMemory{byte})"/>）が、ファイル (MMF) 版ロードと**同一結果**を返すことの突合。
/// ブラウザ WASM 経路（fetch 済みバイト列）がファイル経路と乖離しないことを保証する。
/// </summary>
public sealed class NativeRoadGraphMemoryParityTests
{
    private static byte[] ReadOdrgBytes()
    {
        EnsureTestData();
        return File.ReadAllBytes(TestPaths.TsushimaOdrg);
    }

    [Fact]
    public void LoadFromOdrg_Bytes_MatchesFileStatistics()
    {
        var fileDb = RouterDb.LoadFromOdrg(TestPaths.TsushimaOdrg);
        var memDb = RouterDb.LoadFromOdrg(ReadOdrgBytes());

        var f = fileDb.GetStatistics();
        var m = memDb.GetStatistics();

        Assert.Equal(f.VertexCount, m.VertexCount);
        Assert.Equal(f.EdgeCount, m.EdgeCount);
        Assert.Equal(f.SouthWest.Latitude, m.SouthWest.Latitude);
        Assert.Equal(f.SouthWest.Longitude, m.SouthWest.Longitude);
        Assert.Equal(f.NorthEast.Latitude, m.NorthEast.Latitude);
        Assert.Equal(f.NorthEast.Longitude, m.NorthEast.Longitude);
    }

    [Fact]
    public void NativeRoadGraph_Bytes_VerticesMatchFile()
    {
        using var fileGraph = new NativeRoadGraph(TestPaths.TsushimaOdrg);
        using var memGraph = new NativeRoadGraph(ReadOdrgBytes());

        Assert.Equal(fileGraph.VertexCount, memGraph.VertexCount);
        Assert.Equal(fileGraph.EdgeCount, memGraph.EdgeCount);

        // 先頭・中間・末尾の頂点が完全一致すること（バイトレイアウトのオフセット計算が両モードで一致）
        uint last = fileGraph.VertexCount - 1;
        foreach (uint id in new[] { 0u, fileGraph.VertexCount / 2u, last })
        {
            var vf = fileGraph.GetVertex(id);
            var vm = memGraph.GetVertex(id);
            Assert.Equal(vf.Latitude, vm.Latitude);
            Assert.Equal(vf.Longitude, vm.Longitude);
        }
    }

    [Fact]
    public void LoadFromOdrg_Bytes_RouteMatchesFile()
    {
        var fileRouter = new Router(RouterDb.LoadFromOdrg(TestPaths.TsushimaOdrg));
        var memRouter = new Router(RouterDb.LoadFromOdrg(ReadOdrgBytes()));

        var from = new GeoCoordinate(35.18, 136.73);
        var to = new GeoCoordinate(35.19, 136.74);

        var rf = fileRouter.Calculate(VehicleProfile.Car, from, to);
        var rm = memRouter.Calculate(VehicleProfile.Car, from, to);

        Assert.NotNull(rf);
        Assert.NotNull(rm);
        Assert.Equal(rf!.TotalDistanceM, rm!.TotalDistanceM);
        Assert.Equal(rf.Shape.Length, rm.Shape.Length);

        var sf = rf.Shape.Span;
        var sm = rm.Shape.Span;
        for (int i = 0; i < sf.Length; i++)
        {
            Assert.Equal(sf[i].Latitude, sm[i].Latitude);
            Assert.Equal(sf[i].Longitude, sm[i].Longitude);
        }
    }

    [Fact]
    public void SnapToRoad_Bytes_MatchesFile()
    {
        var fileRouter = new Router(RouterDb.LoadFromOdrg(TestPaths.TsushimaOdrg));
        var memRouter = new Router(RouterDb.LoadFromOdrg(ReadOdrgBytes()));

        var point = new GeoCoordinate(35.185, 136.735);
        var sf = fileRouter.SnapToRoad(VehicleProfile.Car, point);
        var sm = memRouter.SnapToRoad(VehicleProfile.Car, point);

        Assert.Equal(sf.HasValue, sm.HasValue);
        if (sf.HasValue)
        {
            Assert.Equal(sf!.Value.Latitude, sm!.Value.Latitude);
            Assert.Equal(sf.Value.Longitude, sm.Value.Longitude);
        }
    }

    [Fact]
    public void LoadFromOdrg_EmptyBytes_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => RouterDb.LoadFromOdrg(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void LoadFromOdrg_InvalidBytes_ThrowsOdrgFormatException()
    {
        var bogus = new byte[] { 0x00, 0x01, 0x02, 0x03 };  // マジックバイト不一致
        Assert.Throws<OsmDotRoute.Internal.Odrg.OdrgFormatException>(
            () => RouterDb.LoadFromOdrg(bogus));
    }

    private static void EnsureTestData()
    {
        if (!File.Exists(TestPaths.TsushimaOdrg))
        {
            Assert.Fail($"リポジトリ同梱の津島.odrg が見つかりません: {TestPaths.TsushimaOdrg}");
        }
    }
}
