using System.Text;
using OsmDotRoute.Gml;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 1 ステップ 10「GML 入力対応」の <see cref="GmlParser"/> 単体テスト。
/// 最小 GML・Hole 込み Surface・複数フィーチャ・MultiSurface 例外・不正 GML・フィーチャ要素名非依存性
/// を検証する（REQ-RST-020〜028）。
/// </summary>
public class GmlParserTests
{
    /// <summary>1 Curve + 1 Surface + 1 ExpectedFloodArea の最小 KSJ GML。</summary>
    private const string MinimalGml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <ksj:Dataset xmlns:ksj="http://nlftp.mlit.go.jp/ksj/schemas/ksj-app"
                     xmlns:gml="http://www.opengis.net/gml/3.2"
                     xmlns:xlink="http://www.w3.org/1999/xlink">
          <gml:Curve gml:id="c1">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>
                  35.0 139.0
                  35.0 140.0
                  36.0 140.0
                  36.0 139.0
                  35.0 139.0
                </gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </gml:Curve>
          <gml:Surface gml:id="a1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:Ring>
                    <gml:curveMember xlink:href="#c1"/>
                  </gml:Ring>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
          <ksj:ExpectedFloodArea gml:id="f1">
            <ksj:bounds xlink:href="#a1"/>
            <ksj:waterDepth>11</ksj:waterDepth>
          </ksj:ExpectedFloodArea>
        </ksj:Dataset>
        """;

    /// <summary>外周 1 + Hole 1 + 1 フィーチャ。</summary>
    private const string GmlWithHole =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <ksj:Dataset xmlns:ksj="http://nlftp.mlit.go.jp/ksj/schemas/ksj-app"
                     xmlns:gml="http://www.opengis.net/gml/3.2"
                     xmlns:xlink="http://www.w3.org/1999/xlink">
          <gml:Curve gml:id="cOuter">
            <gml:segments><gml:LineStringSegment><gml:posList>
              35.0 139.0  35.0 140.0  36.0 140.0  36.0 139.0  35.0 139.0
            </gml:posList></gml:LineStringSegment></gml:segments>
          </gml:Curve>
          <gml:Curve gml:id="cHole">
            <gml:segments><gml:LineStringSegment><gml:posList>
              35.4 139.4  35.4 139.6  35.6 139.6  35.6 139.4  35.4 139.4
            </gml:posList></gml:LineStringSegment></gml:segments>
          </gml:Curve>
          <gml:Surface gml:id="a1">
            <gml:patches><gml:PolygonPatch>
              <gml:exterior><gml:Ring><gml:curveMember xlink:href="#cOuter"/></gml:Ring></gml:exterior>
              <gml:interior><gml:Ring><gml:curveMember xlink:href="#cHole"/></gml:Ring></gml:interior>
            </gml:PolygonPatch></gml:patches>
          </gml:Surface>
          <ksj:ExpectedFloodArea gml:id="f1">
            <ksj:bounds xlink:href="#a1"/>
            <ksj:waterDepth>5</ksj:waterDepth>
          </ksj:ExpectedFloodArea>
        </ksj:Dataset>
        """;

    /// <summary>3 Curve + 3 Surface + 3 フィーチャ。</summary>
    private const string MultiFeatureGml =
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

    /// <summary>架空のフィーチャ要素名（フィーチャ要素名非依存性の検証用）。</summary>
    private const string DummyFeatureNameGml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <root xmlns:test="http://example.com/test"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink">
          <gml:Curve gml:id="c1"><gml:segments><gml:LineStringSegment><gml:posList>
            35.0 139.0  35.0 139.1  35.1 139.1  35.1 139.0  35.0 139.0
          </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve>
          <gml:Surface gml:id="a1"><gml:patches><gml:PolygonPatch>
            <gml:exterior><gml:Ring><gml:curveMember xlink:href="#c1"/></gml:Ring></gml:exterior>
          </gml:PolygonPatch></gml:patches></gml:Surface>
          <test:DummyArea>
            <test:reference xlink:href="#a1"/>
            <test:customAttribute>arbitrary value</test:customAttribute>
          </test:DummyArea>
        </root>
        """;

    /// <summary>MultiSurface 検出用。</summary>
    private const string MultiSurfaceGml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <ksj:Dataset xmlns:ksj="http://nlftp.mlit.go.jp/ksj/schemas/ksj-app"
                     xmlns:gml="http://www.opengis.net/gml/3.2"
                     xmlns:xlink="http://www.w3.org/1999/xlink">
          <gml:MultiSurface gml:id="ms1">
            <gml:surfaceMember xlink:href="#a1"/>
          </gml:MultiSurface>
        </ksj:Dataset>
        """;

    [Fact]
    public void ParseString_Minimal_ReturnsSinglePolygon()
    {
        var polygons = GmlParser.ParseString(MinimalGml);
        Assert.Single(polygons);
        Assert.Equal(5, polygons[0].OuterBoundary.Count);
        Assert.Empty(polygons[0].Holes);
        // 「緯度 経度」順で読まれているか: 1 頂点目は (35.0, 139.0)
        Assert.Equal(35.0, polygons[0].OuterBoundary[0].Latitude);
        Assert.Equal(139.0, polygons[0].OuterBoundary[0].Longitude);
    }

    [Fact]
    public void ParseString_WithHole_ReturnsPolygonWithOneInteriorRing()
    {
        var polygons = GmlParser.ParseString(GmlWithHole);
        Assert.Single(polygons);
        Assert.Equal(5, polygons[0].OuterBoundary.Count);
        Assert.Single(polygons[0].Holes);
        Assert.Equal(5, polygons[0].Holes[0].Count);
        Assert.Equal(35.4, polygons[0].Holes[0][0].Latitude);
        Assert.Equal(139.4, polygons[0].Holes[0][0].Longitude);
    }

    [Fact]
    public void ParseString_MultipleFeatures_ReturnsPolygonsInOrder()
    {
        var polygons = GmlParser.ParseString(MultiFeatureGml);
        Assert.Equal(3, polygons.Count);
        // 各フィーチャの 1 頂点目で順序を確認
        Assert.Equal(35.0, polygons[0].OuterBoundary[0].Latitude);
        Assert.Equal(35.2, polygons[1].OuterBoundary[0].Latitude);
        Assert.Equal(35.4, polygons[2].OuterBoundary[0].Latitude);
    }

    [Fact]
    public void ParseString_DummyFeatureName_StillResolvesViaXlink()
    {
        // フィーチャ要素名 `<test:DummyArea>` も、配下に xlink:href で Surface を参照していれば読める
        var polygons = GmlParser.ParseString(DummyFeatureNameGml);
        Assert.Single(polygons);
        Assert.Equal(35.0, polygons[0].OuterBoundary[0].Latitude);
    }

    [Fact]
    public void ParseString_MultiSurface_ThrowsNotSupported()
    {
        var ex = Assert.Throws<NotSupportedException>(() => GmlParser.ParseString(MultiSurfaceGml));
        Assert.Contains("MultiSurface", ex.Message);
    }

    [Fact]
    public void ParseString_MalformedXml_ThrowsInvalidGml()
    {
        const string malformed = "<ksj:Dataset xmlns:ksj=\"http://nlftp.mlit.go.jp/ksj/schemas/ksj-app\"><unclosed";
        Assert.Throws<InvalidGmlException>(() => GmlParser.ParseString(malformed));
    }

    [Fact]
    public void ParseString_UnresolvedSurfaceReference_ThrowsInvalidGml()
    {
        const string unresolved =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <ksj:Dataset xmlns:ksj="http://nlftp.mlit.go.jp/ksj/schemas/ksj-app"
                         xmlns:gml="http://www.opengis.net/gml/3.2"
                         xmlns:xlink="http://www.w3.org/1999/xlink">
              <gml:Curve gml:id="c1"><gml:segments><gml:LineStringSegment><gml:posList>
                35.0 139.0  35.0 140.0  36.0 140.0  35.0 139.0
              </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve>
              <gml:Surface gml:id="a1"><gml:patches><gml:PolygonPatch>
                <gml:exterior><gml:Ring><gml:curveMember xlink:href="#cMissing"/></gml:Ring></gml:exterior>
              </gml:PolygonPatch></gml:patches></gml:Surface>
              <ksj:ExpectedFloodArea><ksj:bounds xlink:href="#a1"/></ksj:ExpectedFloodArea>
            </ksj:Dataset>
            """;
        // Surface の外周が参照する Curve が存在しない → InvalidGmlException
        Assert.Throws<InvalidGmlException>(() => GmlParser.ParseString(unresolved));
    }

    [Fact]
    public void ParseString_FeatureWithoutXlinkHref_IsSilentlySkipped()
    {
        // フィーチャ要素配下に xlink:href が無ければスキップ（例外なし、空結果）
        const string noBounds =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <ksj:Dataset xmlns:ksj="http://nlftp.mlit.go.jp/ksj/schemas/ksj-app"
                         xmlns:gml="http://www.opengis.net/gml/3.2">
              <ksj:MetaInfo><ksj:title>テストデータ</ksj:title></ksj:MetaInfo>
            </ksj:Dataset>
            """;
        var polygons = GmlParser.ParseString(noBounds);
        Assert.Empty(polygons);
    }

    [Fact]
    public void ParseStream_Minimal_Roundtrip()
    {
        // string 版と Stream 版が同じ結果を返す
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(MinimalGml));
        var polygons = GmlParser.ParseStream(stream);
        Assert.Single(polygons);
        Assert.Equal(5, polygons[0].OuterBoundary.Count);
    }
}
