using OsmDotRoute.Extractor.Pipeline;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.6 — <see cref="BakedProfileEntry"/> の flags pack/unpack テスト。
/// </summary>
public sealed class BakedProfileEntryTests
{
    [Fact]
    public void Create_AllTrue_FlagsBitsSet()
    {
        var e = BakedProfileEntry.Create(canPass: true, speedKmh: 50f, forward: true, backward: true);
        Assert.True(e.CanPass);
        Assert.True(e.Forward);
        Assert.True(e.Backward);
        Assert.Equal(50f, e.SpeedKmh);
        Assert.Equal((byte)0b111, e.Flags);
    }

    [Fact]
    public void Create_CanPassFalse_SpeedZeroed()
    {
        // CanPass=false なら速度は意味を持たないので 0 に正規化
        var e = BakedProfileEntry.Create(canPass: false, speedKmh: 50f, forward: false, backward: false);
        Assert.False(e.CanPass);
        Assert.Equal(0f, e.SpeedKmh);
        Assert.Equal((byte)0, e.Flags);
    }

    [Fact]
    public void Create_OnewayForward_ForwardBitOnly()
    {
        var e = BakedProfileEntry.Create(canPass: true, speedKmh: 30f, forward: true, backward: false);
        Assert.True(e.Forward);
        Assert.False(e.Backward);
        Assert.Equal((byte)0b011, e.Flags);  // CanPass + Forward
    }

    [Fact]
    public void Create_OnewayBackward_BackwardBitOnly()
    {
        var e = BakedProfileEntry.Create(canPass: true, speedKmh: 30f, forward: false, backward: true);
        Assert.False(e.Forward);
        Assert.True(e.Backward);
        Assert.Equal((byte)0b101, e.Flags);  // CanPass + Backward
    }
}
