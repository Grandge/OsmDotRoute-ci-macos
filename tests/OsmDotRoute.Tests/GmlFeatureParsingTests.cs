using System.Text;
using OsmDotRoute.Gml;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// <see cref="GmlParser.ParseFeaturesString"/> / <see cref="GmlParser.ParseFeaturesStream"/> の検証（REQ-RST-041）。
/// KSJ A51「雨水出水浸水想定区域」相当の構造で、フィーチャごとの「形状＋属性」抽出を確認する。
/// </summary>
public class GmlFeatureParsingTests
{
    /// <summary>A51 相当: 属性複数＋Hole＋複合要素・空要素・xlink 参照要素（属性対象外）を含む 2 フィーチャ。</summary>
    private const string A51LikeGml =
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
          <gml:Curve gml:id="c2">
            <gml:segments><gml:LineStringSegment><gml:posList>
              34.0 138.0  34.0 138.1  34.1 138.1  34.1 138.0  34.0 138.0
            </gml:posList></gml:LineStringSegment></gml:segments>
          </gml:Curve>
          <gml:Surface gml:id="a1">
            <gml:patches><gml:PolygonPatch>
              <gml:exterior><gml:Ring><gml:curveMember xlink:href="#cOuter"/></gml:Ring></gml:exterior>
              <gml:interior><gml:Ring><gml:curveMember xlink:href="#cHole"/></gml:Ring></gml:interior>
            </gml:PolygonPatch></gml:patches>
          </gml:Surface>
          <gml:Surface gml:id="a2">
            <gml:patches><gml:PolygonPatch>
              <gml:exterior><gml:Ring><gml:curveMember xlink:href="#c2"/></gml:Ring></gml:exterior>
            </gml:PolygonPatch></gml:patches>
          </gml:Surface>
          <ksj:InlandFloodingArea gml:id="f1">
            <ksj:bounds xlink:href="#a1"/>
            <ksj:A51_001>2</ksj:A51_001>
            <ksj:A51_002>town-A</ksj:A51_002>
            <ksj:emptyElement/>
            <ksj:complexElement><ksj:nested>x</ksj:nested></ksj:complexElement>
          </ksj:InlandFloodingArea>
          <ksj:InlandFloodingArea gml:id="f2">
            <ksj:bounds xlink:href="#a2"/>
            <ksj:A51_001>3</ksj:A51_001>
          </ksj:InlandFloodingArea>
        </ksj:Dataset>
        """;

    /// <summary>属性が 1 つも無い（形状参照のみの）フィーチャ。</summary>
    private const string NoAttributeGml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <ksj:Dataset xmlns:ksj="http://nlftp.mlit.go.jp/ksj/schemas/ksj-app"
                     xmlns:gml="http://www.opengis.net/gml/3.2"
                     xmlns:xlink="http://www.w3.org/1999/xlink">
          <gml:Curve gml:id="c1"><gml:segments><gml:LineStringSegment><gml:posList>
            35.0 139.0  35.0 139.1  35.1 139.1  35.1 139.0  35.0 139.0
          </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve>
          <gml:Surface gml:id="a1"><gml:patches><gml:PolygonPatch>
            <gml:exterior><gml:Ring><gml:curveMember xlink:href="#c1"/></gml:Ring></gml:exterior>
          </gml:PolygonPatch></gml:patches></gml:Surface>
          <ksj:ExpectedFloodArea gml:id="f1"><ksj:bounds xlink:href="#a1"/></ksj:ExpectedFloodArea>
        </ksj:Dataset>
        """;

    [Fact]
    public void ParseFeaturesString_ReturnsShapeAndAttributesPerFeature()
    {
        var features = GmlParser.ParseFeaturesString(A51LikeGml);

        Assert.Equal(2, features.Count);

        // f1: 外周 + Hole、属性 2 件（key は名前空間 prefix を剥がしたローカル名）
        var f1 = features[0];
        Assert.Equal(5, f1.Polygon.OuterBoundary.Count);
        Assert.Single(f1.Polygon.Holes);
        Assert.Equal(35.0, f1.Polygon.OuterBoundary[0].Latitude);
        Assert.Equal("2", f1.Attributes["A51_001"]);
        Assert.Equal("town-A", f1.Attributes["A51_002"]);

        // f2: 属性 1 件
        var f2 = features[1];
        Assert.Empty(f2.Polygon.Holes);
        Assert.Equal(34.0, f2.Polygon.OuterBoundary[0].Latitude);
        Assert.Equal("3", f2.Attributes["A51_001"]);
        Assert.Single(f2.Attributes);
    }

    [Fact]
    public void ParseFeaturesString_ComplexAndEmptyAndReferenceElements_AreNotAttributes()
    {
        var f1 = GmlParser.ParseFeaturesString(A51LikeGml)[0];

        Assert.Equal(2, f1.Attributes.Count);
        Assert.False(f1.Attributes.ContainsKey("bounds"));           // xlink 参照要素は属性ではない
        Assert.False(f1.Attributes.ContainsKey("emptyElement"));     // 空要素は属性ではない
        Assert.False(f1.Attributes.ContainsKey("complexElement"));   // 子要素を持つ複合要素は属性ではない
        Assert.False(f1.Attributes.ContainsKey("nested"));           // 複合要素配下の孫要素も対象外
    }

    [Fact]
    public void ParseFeaturesString_FeatureWithoutAttributes_ReturnsEmptyDictionary()
    {
        var features = GmlParser.ParseFeaturesString(NoAttributeGml);
        Assert.Single(features);
        Assert.NotNull(features[0].Attributes);
        Assert.Empty(features[0].Attributes);
    }

    [Fact]
    public void ParseFeaturesStream_ReturnsSameResultAsStringVariant()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(A51LikeGml));
        var fromStream = GmlParser.ParseFeaturesStream(stream);
        var fromString = GmlParser.ParseFeaturesString(A51LikeGml);

        Assert.Equal(fromString.Count, fromStream.Count);
        for (var i = 0; i < fromString.Count; i++)
        {
            Assert.Equal(fromString[i].Attributes, fromStream[i].Attributes);
            Assert.Equal(
                fromString[i].Polygon.OuterBoundary[0],
                fromStream[i].Polygon.OuterBoundary[0]);
        }
    }

    [Fact]
    public void ParseString_ReturnsSamePolygonsAsParseFeatures()
    {
        // 既存 API（形状のみ）と新 API のポリゴンが個数・順序で一致する（既存挙動の維持）
        var polygons = GmlParser.ParseString(A51LikeGml);
        var features = GmlParser.ParseFeaturesString(A51LikeGml);

        Assert.Equal(features.Count, polygons.Count);
        for (var i = 0; i < polygons.Count; i++)
        {
            Assert.Equal(features[i].Polygon.OuterBoundary.Count, polygons[i].OuterBoundary.Count);
        }
    }

    [Fact]
    public void ParseFeaturesString_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GmlParser.ParseFeaturesString(null!));
    }
}