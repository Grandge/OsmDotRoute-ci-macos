using OsmDotRoute.Gml;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 1 ステップ 10「GML 入力対応」の <see cref="RestrictedAreaService"/> 統合テスト。
/// `AddBlockAreaFromGml*` / `AddDifficultyAreaFromGml*` の 6 メソッドが
/// GmlParser と連携して正しく制約を登録すること、タグ連携・難所タイプ検証・
/// 実データ読込を検証する（REQ-RST-020〜028）。
/// </summary>
public class RestrictedAreaServiceGmlTests
{
    private const string MinimalGml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <ksj:Dataset xmlns:ksj="http://nlftp.mlit.go.jp/ksj/schemas/ksj-app"
                     xmlns:gml="http://www.opengis.net/gml/3.2"
                     xmlns:xlink="http://www.w3.org/1999/xlink">
          <gml:Curve gml:id="c1"><gml:segments><gml:LineStringSegment><gml:posList>
            35.0 139.0  35.0 140.0  36.0 140.0  36.0 139.0  35.0 139.0
          </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve>
          <gml:Surface gml:id="a1"><gml:patches><gml:PolygonPatch>
            <gml:exterior><gml:Ring><gml:curveMember xlink:href="#c1"/></gml:Ring></gml:exterior>
          </gml:PolygonPatch></gml:patches></gml:Surface>
          <ksj:ExpectedFloodArea gml:id="f1"><ksj:bounds xlink:href="#a1"/></ksj:ExpectedFloodArea>
        </ksj:Dataset>
        """;

    private const string ThreeFeaturesGml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <ksj:Dataset xmlns:ksj="http://nlftp.mlit.go.jp/ksj/schemas/ksj-app"
                     xmlns:gml="http://www.opengis.net/gml/3.2"
                     xmlns:xlink="http://www.w3.org/1999/xlink">
          <gml:Curve gml:id="c1"><gml:segments><gml:LineStringSegment><gml:posList>
            35.0 139.0  35.0 139.1  35.1 139.1  35.1 139.0  35.0 139.0
          </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve>
          <gml:Curve gml:id="c2"><gml:segments><gml:LineStringSegment><gml:posList>
            35.2 139.2  35.2 139.3  35.3 139.3  35.3 139.2  35.2 139.2
          </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve>
          <gml:Curve gml:id="c3"><gml:segments><gml:LineStringSegment><gml:posList>
            35.4 139.4  35.4 139.5  35.5 139.5  35.5 139.4  35.4 139.4
          </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve>
          <gml:Surface gml:id="a1"><gml:patches><gml:PolygonPatch>
            <gml:exterior><gml:Ring><gml:curveMember xlink:href="#c1"/></gml:Ring></gml:exterior>
          </gml:PolygonPatch></gml:patches></gml:Surface>
          <gml:Surface gml:id="a2"><gml:patches><gml:PolygonPatch>
            <gml:exterior><gml:Ring><gml:curveMember xlink:href="#c2"/></gml:Ring></gml:exterior>
          </gml:PolygonPatch></gml:patches></gml:Surface>
          <gml:Surface gml:id="a3"><gml:patches><gml:PolygonPatch>
            <gml:exterior><gml:Ring><gml:curveMember xlink:href="#c3"/></gml:Ring></gml:exterior>
          </gml:PolygonPatch></gml:patches></gml:Surface>
          <ksj:ExpectedFloodArea gml:id="f1"><ksj:bounds xlink:href="#a1"/></ksj:ExpectedFloodArea>
          <ksj:ExpectedFloodArea gml:id="f2"><ksj:bounds xlink:href="#a2"/></ksj:ExpectedFloodArea>
          <ksj:ExpectedFloodArea gml:id="f3"><ksj:bounds xlink:href="#a3"/></ksj:ExpectedFloodArea>
        </ksj:Dataset>
        """;

    [Fact]
    public void AddBlockAreaFromGml_Registers_AsBlockArea()
    {
        var service = new RestrictedAreaService();
        var ids = service.AddBlockAreaFromGml(MinimalGml, tag: "test-block");
        Assert.Single(ids);

        var all = service.ListAll();
        Assert.Single(all);
        var area = Assert.IsType<BlockArea>(all[0]);
        Assert.Equal(ids[0], area.Id);
        Assert.Equal("test-block", area.Tag);
        Assert.NotNull(area.Polygon);
    }

    [Fact]
    public void AddDifficultyAreaFromGml_Registers_AsDifficultyArea_WithType()
    {
        var service = new RestrictedAreaService();
        var ids = service.AddDifficultyAreaFromGml(MinimalGml, DifficultyTypes.Flooding, tag: "flood");
        Assert.Single(ids);

        var area = Assert.IsType<DifficultyArea>(service.ListAll()[0]);
        Assert.Equal(DifficultyTypes.Flooding, area.DifficultyType);
        Assert.Equal("flood", area.Tag);
    }

    [Fact]
    public void AddDifficultyAreaFromGml_Accepts_UserDefinedDifficultyType()
    {
        var service = new RestrictedAreaService();
        var ids = service.AddDifficultyAreaFromGml(MinimalGml, "snow_heavy");
        Assert.Single(ids);
        var area = Assert.IsType<DifficultyArea>(service.ListAll()[0]);
        Assert.Equal("snow_heavy", area.DifficultyType);
    }

    [Fact]
    public void AddDifficultyAreaFromGml_Rejects_EmptyDifficultyType()
    {
        var service = new RestrictedAreaService();
        Assert.Throws<ArgumentException>(() => service.AddDifficultyAreaFromGml(MinimalGml, ""));
        Assert.Throws<ArgumentException>(() => service.AddDifficultyAreaFromGml(MinimalGml, "   "));
        Assert.Throws<ArgumentException>(() => service.AddDifficultyAreaFromGml(MinimalGml, null!));
    }

    [Fact]
    public void AddBlockAreaFromGml_MultipleFeatures_RegistersAllAndReturnsIds()
    {
        var service = new RestrictedAreaService();
        var ids = service.AddBlockAreaFromGml(ThreeFeaturesGml);
        Assert.Equal(3, ids.Length);
        Assert.Equal(3, service.ListAll().Count);
        // すべて異なる ID
        Assert.Equal(3, new HashSet<RestrictedAreaId>(ids).Count);
    }

    [Fact]
    public void AddDifficultyAreaFromGml_WithTag_AllFeaturesShareSameTag_And_RemoveByTag_Removes_All()
    {
        var service = new RestrictedAreaService();
        service.AddDifficultyAreaFromGml(ThreeFeaturesGml, DifficultyTypes.Flooding, tag: "flood-batch");
        Assert.Equal(3, service.ListAll().Count);
        Assert.All(service.ListAll(), a => Assert.Equal("flood-batch", a.Tag));

        service.RemoveByTag("flood-batch");
        Assert.Empty(service.ListAll());
    }

    [Fact]
    public void AddBlockAreaFromGmlStream_RoundtripsSameAsString()
    {
        var service = new RestrictedAreaService();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ThreeFeaturesGml));
        var ids = service.AddBlockAreaFromGmlStream(stream);
        Assert.Equal(3, ids.Length);
    }

    [Fact]
    public void AddBlockAreaFromGml_PropagatesMultiSurfaceException()
    {
        const string multiSurfaceGml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <ksj:Dataset xmlns:ksj="http://nlftp.mlit.go.jp/ksj/schemas/ksj-app"
                         xmlns:gml="http://www.opengis.net/gml/3.2">
              <gml:MultiSurface gml:id="ms1"/>
            </ksj:Dataset>
            """;
        var service = new RestrictedAreaService();
        Assert.Throws<NotSupportedException>(() => service.AddBlockAreaFromGml(multiSurfaceGml));
    }

    [Fact]
    public void AddBlockAreaFromGml_PropagatesInvalidGmlException()
    {
        const string malformed = "<ksj:Dataset xmlns:ksj=\"http://nlftp.mlit.go.jp/ksj/schemas/ksj-app\"><unclosed";
        var service = new RestrictedAreaService();
        Assert.Throws<InvalidGmlException>(() => service.AddBlockAreaFromGml(malformed));
    }

    [Fact]
    public void AddBlockAreaFromGml_WithMapBounds_FiltersFeaturesOutsideBounds()
    {
        // ThreeFeaturesGml: f1=(35.0-35.1, 139.0-139.1), f2=(35.2-35.3, 139.2-139.3), f3=(35.4-35.5, 139.4-139.5)
        // f1 だけを含むマップ範囲を指定
        var bounds = new MapBounds(
            new GeoCoordinate(35.0, 139.0),
            new GeoCoordinate(35.15, 139.15));
        var service = new RestrictedAreaService();
        var ids = service.AddBlockAreaFromGml(ThreeFeaturesGml, mapBounds: bounds);

        // f1 のみ採用される（f2, f3 は外周頂点が全て範囲外でスキップ）
        Assert.Single(ids);
        Assert.Single(service.ListAll());
        // f1 の頂点 (35.0, 139.0) を含むことを確認
        var area = Assert.IsType<BlockArea>(service.ListAll()[0]);
        Assert.NotNull(area.Polygon);
        Assert.Equal(35.0, area.Polygon!.OuterBoundary[0].Latitude);
    }

    [Fact]
    public void AddDifficultyAreaFromGml_MapBoundsAllOutOfRange_ReturnsEmpty()
    {
        // 全フィーチャが範囲外 (40-41 緯度、140-141 経度) → 0 件
        var bounds = new MapBounds(
            new GeoCoordinate(40.0, 140.0),
            new GeoCoordinate(41.0, 141.0));
        var service = new RestrictedAreaService();
        var ids = service.AddDifficultyAreaFromGml(ThreeFeaturesGml, DifficultyTypes.Flooding, mapBounds: bounds);

        Assert.Empty(ids);
        Assert.Empty(service.ListAll());
    }

    [Fact]
    public void AddBlockAreaFromGml_MapBoundsBoundaryVertex_IsIncludedAsInside()
    {
        // f1 の頂点 (35.0, 139.0) がマップ範囲の SW 境界とぴったり一致 → 内側扱いで採用される
        var bounds = new MapBounds(
            new GeoCoordinate(35.0, 139.0),   // f1 の SW 頂点と一致
            new GeoCoordinate(35.05, 139.05));  // 他フィーチャは範囲外
        var service = new RestrictedAreaService();
        var ids = service.AddBlockAreaFromGml(ThreeFeaturesGml, mapBounds: bounds);

        // 境界線上の頂点があれば内側扱い → f1 採用
        Assert.Single(ids);
    }

    /// <summary>
    /// 実データ統合テスト: 国土数値情報 A31「浸水想定区域」サンプルを `Stream` で読込み、
    /// フィーチャ数 ≥ 1 とエラー無しを確認する。サンプルが存在しない環境では Skip 扱いとする。
    /// </summary>
    [Fact]
    public void AddDifficultyAreaFromGmlFile_RealKsjSample_LoadsAtLeastOneFeature()
    {
        const string realDataPath = @"D:\ハザードデータ\A31-12_24_GML\A31-12_24.xml";
        if (!File.Exists(realDataPath))
        {
            // 実データ未配置の環境ではテストを通す（CI ・他開発機での実行を阻害しないため）
            return;
        }

        var service = new RestrictedAreaService();
        // 1.6GB ファイルを Stream で逐次読込。フル読込でも GmlParser はストリーミング XmlReader 経由で実装
        var ids = service.AddDifficultyAreaFromGmlFile(realDataPath, DifficultyTypes.Flooding, tag: "mie-a31");
        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, service.ListAll().Count);
        Assert.All(service.ListAll(), a =>
        {
            var d = Assert.IsType<DifficultyArea>(a);
            Assert.Equal(DifficultyTypes.Flooding, d.DifficultyType);
            Assert.Equal("mie-a31", d.Tag);
        });
    }
}
