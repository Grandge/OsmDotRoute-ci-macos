using System;
using System.Collections.Generic;
using System.Text;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.5 — <see cref="EdgeFlagsBaker.Bake(int[], int[], OsmStringTable)"/> の bake テスト。
/// </summary>
public sealed class EdgeFlagsBakerTests
{
    private static EdgeFlags BakeFromTags(params (string Key, string Value)[] tags)
    {
        var entries = new List<string> { "" };
        var keys = new int[tags.Length];
        var values = new int[tags.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            keys[i] = entries.Count;
            entries.Add(tags[i].Key);
            values[i] = entries.Count;
            entries.Add(tags[i].Value);
        }
        var bytes = new byte[entries.Count][];
        for (int i = 0; i < entries.Count; i++)
            bytes[i] = Encoding.UTF8.GetBytes(entries[i]);
        return EdgeFlagsBaker.Bake(keys, values, new OsmStringTable(bytes));
    }

    [Fact]
    public void NoTags_NoFlags() => Assert.Equal(EdgeFlags.None, BakeFromTags());

    [Theory]
    [InlineData("yes")]
    [InlineData("viaduct")]
    [InlineData("aqueduct")]
    public void BridgeAnyValueExceptNo_SetsIsBridge(string value)
    {
        var flags = BakeFromTags(("bridge", value));
        Assert.True(flags.HasFlag(EdgeFlags.IsBridge));
    }

    [Fact]
    public void BridgeNo_NotSet() =>
        Assert.False(BakeFromTags(("bridge", "no")).HasFlag(EdgeFlags.IsBridge));

    [Fact]
    public void BridgeViaduct_AlsoSetsElevated()
    {
        var flags = BakeFromTags(("bridge", "viaduct"));
        Assert.True(flags.HasFlag(EdgeFlags.IsBridge));
        Assert.True(flags.HasFlag(EdgeFlags.IsElevated));
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("culvert", true)]
    [InlineData("no", false)]
    public void Tunnel(string value, bool expected)
    {
        var flags = BakeFromTags(("tunnel", value));
        Assert.Equal(expected, flags.HasFlag(EdgeFlags.IsTunnel));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("2", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]  // パース失敗
    public void Layer_ElevatedIfGE1(string layer, bool expected)
    {
        var flags = BakeFromTags(("layer", layer));
        Assert.Equal(expected, flags.HasFlag(EdgeFlags.IsElevated));
    }

    [Fact]
    public void JunctionRoundabout_SetsRoundaboutAndImplicitOnewayForward()
    {
        var flags = BakeFromTags(("junction", "roundabout"));
        Assert.True(flags.HasFlag(EdgeFlags.IsRoundabout));
        Assert.True(flags.HasFlag(EdgeFlags.IsOnewayForward));
    }

    [Fact]
    public void JunctionRoundabout_WithExplicitOnewayNo_NoImplicitForward()
    {
        var flags = BakeFromTags(("junction", "roundabout"), ("oneway", "no"));
        Assert.True(flags.HasFlag(EdgeFlags.IsRoundabout));
        Assert.False(flags.HasFlag(EdgeFlags.IsOnewayForward));
    }

    [Fact]
    public void JunctionRoundabout_WithExplicitOnewayMinusOne_BackwardWins()
    {
        var flags = BakeFromTags(("junction", "roundabout"), ("oneway", "-1"));
        Assert.True(flags.HasFlag(EdgeFlags.IsRoundabout));
        Assert.True(flags.HasFlag(EdgeFlags.IsOnewayBackward));
        Assert.False(flags.HasFlag(EdgeFlags.IsOnewayForward));
    }

    // EdgeFlags は internal のため InlineData では underlying ushort で渡し、内部でキャスト
    [Theory]
    [InlineData("toll", "yes", (ushort)(1 << 4))]                 // IsToll
    [InlineData("toll", "no", (ushort)0)]
    [InlineData("access", "private", (ushort)(1 << 5))]           // IsPrivateAccess
    [InlineData("access", "yes", (ushort)0)]
    [InlineData("highway", "service", (ushort)(1 << 6))]          // IsServiceWay
    [InlineData("highway", "track", (ushort)(1 << 7))]            // IsTrack
    [InlineData("highway", "living_street", (ushort)(1 << 8))]    // IsLivingStreet
    [InlineData("highway", "residential", (ushort)0)]
    [InlineData("sidewalk", "yes", (ushort)(1 << 9))]             // IsPedestrianSeparated
    [InlineData("sidewalk", "both", (ushort)(1 << 9))]
    [InlineData("sidewalk", "no", (ushort)0)]
    [InlineData("seasonal", "winter", (ushort)(1 << 10))]         // IsWinterClosed
    [InlineData("winter_road", "no", (ushort)(1 << 10))]
    [InlineData("oneway", "yes", (ushort)(1 << 12))]              // IsOnewayForward
    [InlineData("oneway", "-1", (ushort)(1 << 13))]               // IsOnewayBackward
    [InlineData("oneway", "no", (ushort)0)]
    [InlineData("oneway", "false", (ushort)0)]
    public void SingleTagMapping(string key, string value, ushort expectedRaw) =>
        Assert.Equal((EdgeFlags)expectedRaw, BakeFromTags((key, value)));

    [Fact]
    public void RealisticHighway_BridgedOnewayService_AllExpectedBits()
    {
        var flags = BakeFromTags(
            ("highway", "service"),
            ("bridge", "yes"),
            ("layer", "1"),
            ("oneway", "yes"),
            ("name", "高架駐車場入口"));

        Assert.True(flags.HasFlag(EdgeFlags.IsServiceWay));
        Assert.True(flags.HasFlag(EdgeFlags.IsBridge));
        Assert.True(flags.HasFlag(EdgeFlags.IsElevated));
        Assert.True(flags.HasFlag(EdgeFlags.IsOnewayForward));
        Assert.False(flags.HasFlag(EdgeFlags.IsTunnel));
        Assert.False(flags.HasFlag(EdgeFlags.IsRoundabout));
    }

    [Fact]
    public void SchoolZone_NotSetEvenIfHazardTag()
    {
        // bit 11 (IsSchoolZone) は v0.2 では予約で 0 固定
        var flags = BakeFromTags(("hazard", "school_zone"));
        Assert.False(flags.HasFlag(EdgeFlags.IsSchoolZone));
    }

    [Fact]
    public void NullArgs_Throw()
    {
        var emptyTable = new OsmStringTable(new[] { Array.Empty<byte>() });
        Assert.Throws<ArgumentNullException>(() =>
            EdgeFlagsBaker.Bake((int[])null!, Array.Empty<int>(), emptyTable));
        Assert.Throws<ArgumentNullException>(() =>
            EdgeFlagsBaker.Bake(Array.Empty<int>(), null!, emptyTable));
        Assert.Throws<ArgumentNullException>(() =>
            EdgeFlagsBaker.Bake(Array.Empty<int>(), Array.Empty<int>(), null!));
        Assert.Throws<ArgumentNullException>(() => EdgeFlagsBaker.Bake((EdgeRecord)null!));
    }
}
