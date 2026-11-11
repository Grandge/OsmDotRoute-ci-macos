using OsmDotRoute.Geometry;
using OsmDotRoute.Native;
using OsmDotRoute.Tests.Native;
using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 3 ステップ 3B.3 — <see cref="RestrictedAreaService.AttachGraph"/> + eager bake 統合の検証 10 件
/// （計画書 §4.3.3）。
/// </summary>
/// <remarks>
/// ユーザー判断 T7=(A) 同一 graph no-op / 別 graph 例外、T8=(A) graph 範囲外形状は無視、
/// T9=(A) Router 自動呼出 を検証する。津島市 .odrg を共通基盤とする <see cref="NativeAndOdrgReaderFixture"/> 流用。
/// </remarks>
public sealed class RestrictedAreaServiceAttachGraphTests : IClassFixture<NativeAndOdrgReaderFixture>
{
    private readonly NativeAndOdrgReaderFixture _fixture;

    public RestrictedAreaServiceAttachGraphTests(NativeAndOdrgReaderFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>津島市 .odrg の bbox 内のポリゴン (中央付近、約 100m 四方、複数道路と交差する想定)。</summary>
    private static GeoPolygon TsushimaCenterPolygon()
    {
        var ring = new[]
        {
            new GeoCoordinate(35.180, 136.740),
            new GeoCoordinate(35.181, 136.740),
            new GeoCoordinate(35.181, 136.741),
            new GeoCoordinate(35.180, 136.741),
            new GeoCoordinate(35.180, 136.740),
        };
        return new GeoPolygon(ring);
    }

    [Fact]
    public void AttachGraph_NotCalled_GraphIsNotAttached()
    {
        var service = new RestrictedAreaService();
        Assert.False(service.IsGraphAttached);
        Assert.Null(service.Cache);
    }

    [Fact]
    public void AttachGraph_FirstCall_CacheInitialized()
    {
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);

        Assert.True(service.IsGraphAttached);
        Assert.NotNull(service.Cache);
    }

    [Fact]
    public void AttachGraph_AddBlockAreaBefore_BakedOnAttach()
    {
        var service = new RestrictedAreaService();
        service.AddBlockArea(TsushimaCenterPolygon());

        // attach 前は cache なし
        Assert.False(service.IsGraphAttached);

        service.AttachGraph(_fixture.Graph);

        // attach 後、既存制約が cache に bake された
        Assert.True(CountBlocked(service) > 0,
            "AttachGraph 時点で既存ポリゴンがエッジ AABB と交差して bake されることを期待");
    }

    [Fact]
    public void AttachGraph_AddBlockAreaAfter_BakedImmediately()
    {
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);

        Assert.Equal(0, CountBlocked(service));

        service.AddBlockArea(TsushimaCenterPolygon());

        Assert.True(CountBlocked(service) > 0,
            "AttachGraph 後の AddBlockArea が Register 経由で即時 bake されることを期待");
    }

    [Fact]
    public void AttachGraph_AddDifficultyArea_BakedToCache()
    {
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);

        service.AddDifficultyArea(TsushimaCenterPolygon(), DifficultyTypes.Flooding);

        int withDifficulty = 0;
        int edgeCount = (int)_fixture.Graph.EdgeCount;
        for (int e = 0; e < edgeCount; e++)
        {
            if (service.Cache!.GetDifficultyAreas((uint)e).Count > 0) withDifficulty++;
        }
        Assert.True(withDifficulty > 0,
            "中央ポリゴンと交差するエッジに DifficultyArea が cache されることを期待");
    }

    [Fact]
    public void AttachGraph_RemoveById_CacheUpdated()
    {
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);

        var id = service.AddBlockArea(TsushimaCenterPolygon());
        int blockedBefore = CountBlocked(service);
        Assert.True(blockedBefore > 0);

        service.Remove(id);
        Assert.Equal(0, CountBlocked(service));
    }

    [Fact]
    public void AttachGraph_RemoveByTag_CacheUpdated()
    {
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);

        service.AddBlockArea(TsushimaCenterPolygon(), tag: "test-tag-3B3");
        int blockedBefore = CountBlocked(service);
        Assert.True(blockedBefore > 0);

        service.RemoveByTag("test-tag-3B3");
        Assert.Equal(0, CountBlocked(service));
    }

    [Fact]
    public void AttachGraph_ClearAll_CacheCleared()
    {
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);

        service.AddBlockArea(TsushimaCenterPolygon());
        Assert.True(CountBlocked(service) > 0);

        service.ClearAll();
        Assert.Equal(0, CountBlocked(service));
    }

    [Fact]
    public void AttachGraph_EighthMeshBlockArea_BakesIntersectingEdgesOnly()
    {
        // REQ-RST-016 仕様確定: 11 桁（1/8 細分、125m）メッシュの一括登録が
        // 既存 AABB 交差セマンティクス（REQ-RST-015）でエッジを遮断する
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);

        // 津島市中心部を覆う 1/8 細分メッシュ群を列挙して一括登録
        var centerBounds = new MapBounds(
            new GeoCoordinate(35.180, 136.740),
            new GeoCoordinate(35.181, 136.741));
        var meshes = MeshCode.EnumerateInBounds(centerBounds, MeshLevel.EighthMesh).ToArray();
        Assert.All(meshes, m => Assert.Equal(MeshLevel.EighthMesh, m.Level));

        var id = service.AddBlockArea(meshes);
        Assert.True(CountBlocked(service) > 0,
            "津島市中心部の 125m メッシュと交差するエッジが bake されることを期待");

        service.Remove(id);
        Assert.Equal(0, CountBlocked(service));

        // グラフ範囲外（東京駅近傍）の 11 桁メッシュは何も遮断しない
        service.AddBlockArea(new MeshCode(53394611111L));
        Assert.Equal(0, CountBlocked(service));
    }

    [Fact]
    public void AttachGraph_MixedQuarterAndEighthMeshes_RegisteredTogether()
    {
        // 10 桁・11 桁混在の一括登録（親プロジェクトの利用形態）
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);

        var centerBounds = new MapBounds(
            new GeoCoordinate(35.180, 136.740),
            new GeoCoordinate(35.181, 136.741));
        var quarter = MeshCode.EnumerateInBounds(centerBounds, MeshLevel.QuarterMesh).First();
        var eighth = MeshCode.EnumerateInBounds(centerBounds, MeshLevel.EighthMesh).First();

        var id = service.AddBlockArea(new[] { quarter, eighth });
        Assert.True(CountBlocked(service) > 0);

        service.Remove(id);
        Assert.Equal(0, CountBlocked(service));
    }

    [Fact]
    public void AttachGraph_SameGraphTwice_NoOp()
    {
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);
        var cache1 = service.Cache;
        Assert.NotNull(cache1);

        // 同一 graph で再 attach → no-op (cache インスタンス維持)
        service.AttachGraph(_fixture.Graph);
        Assert.Same(cache1, service.Cache);
    }

    [Fact]
    public void AttachGraph_DifferentGraph_Throws()
    {
        var service = new RestrictedAreaService();
        service.AttachGraph(_fixture.Graph);

        // 別の NativeRoadGraph インスタンス → ReferenceEquals false
        using var otherGraph = new NativeRoadGraph(TestPaths.TsushimaOdrg);
        Assert.Throws<InvalidOperationException>(() => service.AttachGraph(otherGraph));
    }

    private int CountBlocked(RestrictedAreaService service)
    {
        Assert.NotNull(service.Cache);
        int count = 0;
        int edgeCount = (int)_fixture.Graph.EdgeCount;
        for (int e = 0; e < edgeCount; e++)
        {
            if (service.Cache!.IsBlocked((uint)e)) count++;
        }
        return count;
    }
}
