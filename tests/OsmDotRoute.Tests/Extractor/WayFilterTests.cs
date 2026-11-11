using System;
using System.Collections.Generic;
using System.Text;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.2 — <see cref="WayFilter.IsRoadWay"/> の判定テスト。
/// </summary>
public sealed class WayFilterTests
{
    private static OsmStringTable BuildTable(params string[] entries)
    {
        var bytes = new byte[entries.Length][];
        for (int i = 0; i < entries.Length; i++)
            bytes[i] = Encoding.UTF8.GetBytes(entries[i]);
        return new OsmStringTable(bytes);
    }

    /// <summary>
    /// テーブル先頭エントリは空文字列 (PBF 慣習)。tag を引数で渡せば対応する key/value インデックス配列を構築。
    /// </summary>
    private static (OsmWay way, OsmStringTable table) BuildWay(params (string Key, string Value)[] tags)
    {
        var entries = new List<string> { "" };
        var keyIdx = new int[tags.Length];
        var valIdx = new int[tags.Length];

        for (int i = 0; i < tags.Length; i++)
        {
            keyIdx[i] = entries.Count;
            entries.Add(tags[i].Key);
            valIdx[i] = entries.Count;
            entries.Add(tags[i].Value);
        }

        var table = BuildTable(entries.ToArray());
        var way = new OsmWay(Id: 1, NodeRefs: Array.Empty<long>(), TagKeys: keyIdx, TagValues: valIdx);
        return (way, table);
    }

    [Theory]
    [InlineData("residential")]
    [InlineData("service")]
    [InlineData("motorway")]
    [InlineData("track")]
    [InlineData("footway")]
    [InlineData("proposed")]  // 計画書 §5.6-17: highway=* は値を問わず採用（プロファイル側で絞る）
    public void IsRoadWay_HighwayAnyValue_ReturnsTrue(string highwayValue)
    {
        var (way, table) = BuildWay(("highway", highwayValue));
        Assert.True(WayFilter.IsRoadWay(way, table));
    }

    [Fact]
    public void IsRoadWay_NoHighway_ReturnsFalse()
    {
        var (way, table) = BuildWay(("name", "Main St"), ("building", "yes"));
        Assert.False(WayFilter.IsRoadWay(way, table));
    }

    [Fact]
    public void IsRoadWay_NoTags_ReturnsFalse()
    {
        var (way, table) = BuildWay();
        Assert.False(WayFilter.IsRoadWay(way, table));
    }

    [Fact]
    public void IsRoadWay_HighwayWithAccessNo_ReturnsFalse()
    {
        var (way, table) = BuildWay(("highway", "service"), ("access", "no"));
        Assert.False(WayFilter.IsRoadWay(way, table));
    }

    [Fact]
    public void IsRoadWay_HighwayWithAccessNoBeforeHighway_ReturnsFalse()
    {
        var (way, table) = BuildWay(("access", "no"), ("highway", "service"));
        Assert.False(WayFilter.IsRoadWay(way, table));
    }

    [Fact]
    public void IsRoadWay_HighwayWithAreaYes_ReturnsFalse()
    {
        var (way, table) = BuildWay(("highway", "pedestrian"), ("area", "yes"));
        Assert.False(WayFilter.IsRoadWay(way, table));
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("permissive")]
    [InlineData("destination")]
    [InlineData("private")]   // access=private は除外しない（広めに取り、プロファイル側で判定する方針）
    public void IsRoadWay_AccessNonNoValues_NotExcluded(string accessValue)
    {
        var (way, table) = BuildWay(("highway", "residential"), ("access", accessValue));
        Assert.True(WayFilter.IsRoadWay(way, table));
    }

    [Fact]
    public void IsRoadWay_AreaNo_NotExcluded()
    {
        var (way, table) = BuildWay(("highway", "service"), ("area", "no"));
        Assert.True(WayFilter.IsRoadWay(way, table));
    }

    [Fact]
    public void IsRoadWay_RealWorldRoadTags_ReturnsTrue()
    {
        var (way, table) = BuildWay(
            ("highway", "residential"),
            ("name", "本町通り"),
            ("maxspeed", "40"),
            ("oneway", "yes"),
            ("lit", "yes"));
        Assert.True(WayFilter.IsRoadWay(way, table));
    }

    [Fact]
    public void IsRoadWay_NullWay_Throws()
    {
        var table = BuildTable("");
        Assert.Throws<ArgumentNullException>(() => WayFilter.IsRoadWay(null!, table));
    }

    [Fact]
    public void IsRoadWay_NullTable_Throws()
    {
        var way = new OsmWay(1, Array.Empty<long>(), Array.Empty<int>(), Array.Empty<int>());
        Assert.Throws<ArgumentNullException>(() => WayFilter.IsRoadWay(way, null!));
    }
}
