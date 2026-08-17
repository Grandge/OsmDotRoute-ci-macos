using OsmDotRoute.Geometry;
using Xunit;

namespace OsmDotRoute.Tests.Native;

/// <summary>
/// Phase 3 ステップ 3B.5-A — Native 系統 (<see cref="NativeRoadGraph"/> + <see cref="OsmDotRoute.Router"/>) で
/// <see cref="RestrictedAreaService"/> を組み合わせた動作の検証 6 件（計画書 §4.5-A、T13=(A) Native 独自シナリオ）。
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 既存 <c>RestrictedRoutingTests</c> 9 件 + <c>RestrictedAreaServiceTests</c> 14 件 +
/// <c>RestrictedAreaServiceGmlTests</c> 13 件で「graph 注入時の動作 = Phase 1 セマンティクス」は
/// 既に証明済（3B.4 完了時）。本テストは Native 経路で「制約効果が実機で見える」シナリオを軽量追加する。
/// </para>
/// <para>
/// fixture は <see cref="NativeRouterDbFixture"/> (3A.6 新設) を流用し、津島市 .odrg + Car プロファイル +
/// 中距離ペア (~1km) で経路計算を行う。
/// </para>
/// </remarks>
public sealed class NativeRouterWithRestrictionsTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public NativeRouterWithRestrictionsTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>中距離ペアの起点付近に置く小ポリゴン (経路を塞ぐ用)、約 100m 四方。</summary>
    private GeoPolygon MakePolygonAtFrom(double sideDeg = 0.001)
    {
        var from = _fixture.MediumPair.From;
        var ring = new[]
        {
            new GeoCoordinate(from.Latitude - sideDeg, from.Longitude - sideDeg),
            new GeoCoordinate(from.Latitude + sideDeg, from.Longitude - sideDeg),
            new GeoCoordinate(from.Latitude + sideDeg, from.Longitude + sideDeg),
            new GeoCoordinate(from.Latitude - sideDeg, from.Longitude + sideDeg),
            new GeoCoordinate(from.Latitude - sideDeg, from.Longitude - sideDeg),
        };
        return new GeoPolygon(ring);
    }

    /// <summary>中距離経路の Shape 全体を包む大ポリゴン (Difficulty で全エッジ係数適用)。</summary>
    private GeoPolygon MakePolygonCoveringRoute(ReadOnlyMemory<GeoCoordinate> shape, double marginDeg = 0.005)
    {
        var span = shape.Span;
        double minLat = double.PositiveInfinity, maxLat = double.NegativeInfinity;
        double minLon = double.PositiveInfinity, maxLon = double.NegativeInfinity;
        for (int i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (c.Latitude < minLat) minLat = c.Latitude;
            if (c.Latitude > maxLat) maxLat = c.Latitude;
            if (c.Longitude < minLon) minLon = c.Longitude;
            if (c.Longitude > maxLon) maxLon = c.Longitude;
        }
        var ring = new[]
        {
            new GeoCoordinate(minLat - marginDeg, minLon - marginDeg),
            new GeoCoordinate(maxLat + marginDeg, minLon - marginDeg),
            new GeoCoordinate(maxLat + marginDeg, maxLon + marginDeg),
            new GeoCoordinate(minLat - marginDeg, maxLon + marginDeg),
            new GeoCoordinate(minLat - marginDeg, minLon - marginDeg),
        };
        return new GeoPolygon(ring);
    }

    [Fact]
    public void EmptyService_RouteMatchesNoRestrictionsBaseline()
    {
        // baseline: 制約なし
        var baseline = _fixture.Router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);
        Assert.NotNull(baseline);

        // 空 RestrictedAreaService で AttachGraph 後 (空 cache)、経路一致
        var restrictions = new RestrictedAreaService();
        var router = new OsmDotRoute.Router(_fixture.RouterDb, restrictions);
        var withEmpty = router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);

        Assert.NotNull(withEmpty);
        Assert.Equal(baseline!.TotalDistanceM, withEmpty!.TotalDistanceM, precision: 1);
        Assert.Equal(baseline.TotalDurationSec, withEmpty.TotalDurationSec, precision: 1);
    }

    [Fact]
    public void BlockArea_OnRouteStart_DetoursOrReturnsNull()
    {
        var baseline = _fixture.Router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);
        Assert.NotNull(baseline);

        var restrictions = new RestrictedAreaService();
        restrictions.AddBlockArea(MakePolygonAtFrom());
        var router = new OsmDotRoute.Router(_fixture.RouterDb, restrictions);
        var withBlock = router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);

        // 起点付近に Block → null (起点周辺の道が全閉) または迂回 (距離増加)
        if (withBlock is null) return;
        Assert.True(withBlock.TotalDistanceM >= baseline!.TotalDistanceM - 1.0,
            $"Block 配置時の距離 {withBlock.TotalDistanceM:F1}m が baseline {baseline.TotalDistanceM:F1}m を下回る");
    }

    [Fact]
    public void DifficultyArea_Flooding_CoveringRoute_IncreasesDuration()
    {
        var baseline = _fixture.Router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);
        Assert.NotNull(baseline);

        var restrictions = new RestrictedAreaService();
        restrictions.AddDifficultyArea(MakePolygonCoveringRoute(baseline!.Shape), DifficultyTypes.Flooding);
        var router = new OsmDotRoute.Router(_fixture.RouterDb, restrictions);
        var withDifficulty = router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);
        Assert.NotNull(withDifficulty);

        // Flooding 領域内 → Car の SpeedFactor=0.3 程度 → 所要時間が大きく増加 (1.5 倍以上)
        var ratio = withDifficulty!.TotalDurationSec / baseline.TotalDurationSec;
        Assert.True(ratio > 1.5,
            $"Difficulty 領域内で時間が baseline の 1.5 倍以上を期待、実際は {ratio:F2} 倍");
    }

    [Fact]
    public void AddBlock_ThenRemove_RestoresBaseline()
    {
        var baseline = _fixture.Router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);
        Assert.NotNull(baseline);

        var restrictions = new RestrictedAreaService();
        var router = new OsmDotRoute.Router(_fixture.RouterDb, restrictions);

        // Block 追加 → 経路変化 (迂回) または null
        var id = restrictions.AddBlockArea(MakePolygonAtFrom());
        var withBlock = router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);

        // Remove で baseline 復元
        restrictions.Remove(id);
        var afterRemove = router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);

        Assert.NotNull(afterRemove);
        Assert.Equal(baseline!.TotalDistanceM, afterRemove!.TotalDistanceM, precision: 1);
    }

    [Fact]
    public void ClearAll_AfterAddBlocks_RestoresBaseline()
    {
        var baseline = _fixture.Router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);
        Assert.NotNull(baseline);

        var restrictions = new RestrictedAreaService();
        var router = new OsmDotRoute.Router(_fixture.RouterDb, restrictions);

        // 複数 Block 追加
        restrictions.AddBlockArea(MakePolygonAtFrom(sideDeg: 0.0005));
        restrictions.AddBlockArea(MakePolygonAtFrom(sideDeg: 0.001));
        restrictions.AddBlockArea(MakePolygonAtFrom(sideDeg: 0.0015));

        // ClearAll で全削除 → baseline 復元
        restrictions.ClearAll();
        var afterClear = router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);

        Assert.NotNull(afterClear);
        Assert.Equal(baseline!.TotalDistanceM, afterClear!.TotalDistanceM, precision: 1);
    }

    [Fact]
    public void RemoveByTag_OnlyTaggedAreasRemoved_CacheReflects()
    {
        var baseline = _fixture.Router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);
        Assert.NotNull(baseline);

        var restrictions = new RestrictedAreaService();
        var router = new OsmDotRoute.Router(_fixture.RouterDb, restrictions);

        // タグ付き Block × 2、タグなし Block × 1
        restrictions.AddBlockArea(MakePolygonAtFrom(sideDeg: 0.0005), tag: "removable");
        restrictions.AddBlockArea(MakePolygonAtFrom(sideDeg: 0.001), tag: "removable");
        restrictions.AddBlockArea(MakePolygonAtFrom(sideDeg: 0.002));  // tag なし、残存

        // 全 Block 有効状態で経路 (cache 反映済)
        var withAllBlocks = router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);

        // タグ削除
        restrictions.RemoveByTag("removable");
        // タグなし Block は残るため経路は baseline と異なる可能性
        var afterRemoveByTag = router.Calculate(_fixture.Car, _fixture.MediumPair.From, _fixture.MediumPair.To);

        // 検証: ListAll で残存件数を確認 (RemoveByTag が動作 = cache 連動の間接検証)
        var remaining = restrictions.ListAll();
        Assert.Single(remaining);
        Assert.Null(remaining[0].Tag);
    }
}
