using System;
using OsmDotRoute.Extractor.Cli;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.1 — <see cref="Bbox.Parse(string)"/> の単体テスト。
/// </summary>
public sealed class BboxTests
{
    [Fact]
    public void Parse_ValidString_ReturnsBbox()
    {
        var bbox = Bbox.Parse("136.70,35.16,136.78,35.20");

        Assert.Equal(136.70, bbox.MinLon);
        Assert.Equal(35.16, bbox.MinLat);
        Assert.Equal(136.78, bbox.MaxLon);
        Assert.Equal(35.20, bbox.MaxLat);
    }

    [Fact]
    public void Parse_NegativeValues_ParsedCorrectly()
    {
        var bbox = Bbox.Parse("-74.05,-33.50,-73.95,-33.40");

        Assert.Equal(-74.05, bbox.MinLon);
        Assert.Equal(-33.50, bbox.MinLat);
    }

    [Theory]
    [InlineData("136.70,35.16,136.78")]               // 3 値しかない
    [InlineData("136.70,35.16,136.78,35.20,extra")]  // 5 値
    [InlineData("")]                                   // 空
    public void Parse_WrongFieldCount_Throws(string text)
    {
        var ex = Assert.Throws<FormatException>(() => Bbox.Parse(text));
        Assert.Contains("4 値カンマ区切り", ex.Message);
    }

    [Fact]
    public void Parse_NonNumericComponent_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => Bbox.Parse("136.70,abc,136.78,35.20"));
        Assert.Contains("浮動小数点", ex.Message);
    }

    [Theory]
    [InlineData("181,0,182,1")]      // lon 範囲外
    [InlineData("0,-91,1,90")]       // lat 範囲外
    public void Parse_OutOfRangeCoordinate_Throws(string text)
    {
        Assert.Throws<FormatException>(() => Bbox.Parse(text));
    }

    [Theory]
    [InlineData("136.78,35.16,136.70,35.20")]  // minLon >= maxLon
    [InlineData("136.70,35.20,136.78,35.16")]  // minLat >= maxLat
    public void Parse_InvertedRange_Throws(string text)
    {
        Assert.Throws<FormatException>(() => Bbox.Parse(text));
    }
}
