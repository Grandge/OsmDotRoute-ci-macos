using System;
using System.IO;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Tests.Pbf.Osm;

public class OsmWayParserTests
{
    [Fact]
    public void Parse_MinimalWay_IdOnly()
    {
        byte[] bytes = BuildWay(id: 123456789L);

        OsmWay way = OsmWayParser.Parse(bytes);

        Assert.Equal(123456789L, way.Id);
        Assert.Empty(way.NodeRefs);
        Assert.Empty(way.TagKeys);
        Assert.Empty(way.TagValues);
    }

    [Fact]
    public void Parse_WayWithRefs_DeltaDecoded()
    {
        // 絶対値 [100, 105, 110, 115] → delta = [100, 5, 5, 5]
        byte[] bytes = BuildWay(
            id: 1L,
            refs: new[] { 100L, 105L, 110L, 115L });

        OsmWay way = OsmWayParser.Parse(bytes);

        Assert.Equal(new[] { 100L, 105L, 110L, 115L }, way.NodeRefs);
    }

    [Fact]
    public void Parse_WayWithTags_TagsRead()
    {
        byte[] bytes = BuildWay(
            id: 1L,
            tagKeys: new[] { 1u, 3u, 5u },
            tagVals: new[] { 2u, 4u, 6u });

        OsmWay way = OsmWayParser.Parse(bytes);

        Assert.Equal(new[] { 1, 3, 5 }, way.TagKeys);
        Assert.Equal(new[] { 2, 4, 6 }, way.TagValues);
    }

    [Fact]
    public void Parse_FullWay_AllFieldsRead()
    {
        byte[] bytes = BuildWay(
            id: 42L,
            refs: new[] { 1000L, 1010L, 1020L },
            tagKeys: new[] { 1u, 3u },
            tagVals: new[] { 2u, 4u });

        OsmWay way = OsmWayParser.Parse(bytes);

        Assert.Equal(42L, way.Id);
        Assert.Equal(new[] { 1000L, 1010L, 1020L }, way.NodeRefs);
        Assert.Equal(new[] { 1, 3 }, way.TagKeys);
        Assert.Equal(new[] { 2, 4 }, way.TagValues);
    }

    [Fact]
    public void Parse_NegativeRefDeltas_AbsoluteValuesPreserved()
    {
        // refs が降順: 絶対値 [200, 150, 100, 80] → delta = [200, -50, -50, -20]
        byte[] bytes = BuildWay(
            id: 1L,
            refs: new[] { 200L, 150L, 100L, 80L });

        OsmWay way = OsmWayParser.Parse(bytes);

        Assert.Equal(new[] { 200L, 150L, 100L, 80L }, way.NodeRefs);
    }

    [Fact]
    public void Parse_LargeWayId_PreservedAsLong()
    {
        // Way ID は実用上 ~10^10 程度になる（OSM の Way ID 連番）
        byte[] bytes = BuildWay(id: 10_000_000_000L);

        OsmWay way = OsmWayParser.Parse(bytes);

        Assert.Equal(10_000_000_000L, way.Id);
    }

    [Fact]
    public void Parse_InfoFieldSkipped()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 7UL);
        // Info: 適当な内部内容
        var info = new MemoryStream();
        WriteTag(info, 1, WireType.Varint);
        WriteVarint(info, 5);
        byte[] infoBytes = info.ToArray();
        WriteTag(ms, 4, WireType.LengthDelimited);
        WriteVarint(ms, (ulong)infoBytes.Length);
        ms.Write(infoBytes);

        OsmWay way = OsmWayParser.Parse(ms.ToArray());

        Assert.Equal(7L, way.Id);
    }

    [Fact]
    public void Parse_LocationsOnWaysExtension_Skipped()
    {
        // field 9 (lat) と field 10 (lon) を含めても無視される
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);

        // field 9: packed lat（ダミー）
        WriteTag(ms, 9, WireType.LengthDelimited);
        byte[] latBytes = PackZigzag(new long[] { 100L, 5L });
        WriteVarint(ms, (ulong)latBytes.Length);
        ms.Write(latBytes);

        // field 10: packed lon
        WriteTag(ms, 10, WireType.LengthDelimited);
        byte[] lonBytes = PackZigzag(new long[] { 200L, 5L });
        WriteVarint(ms, (ulong)lonBytes.Length);
        ms.Write(lonBytes);

        OsmWay way = OsmWayParser.Parse(ms.ToArray());

        Assert.Equal(1L, way.Id);
        Assert.Empty(way.NodeRefs);
    }

    [Fact]
    public void Parse_UnknownFieldSkipped()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);
        WriteTag(ms, 99, WireType.Varint);
        WriteVarint(ms, 12345UL);

        OsmWay way = OsmWayParser.Parse(ms.ToArray());

        Assert.Equal(1L, way.Id);
    }

    [Fact]
    public void Parse_EmptyRefs_EmptyArray()
    {
        // refs フィールド有り、ただし length=0
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);
        WriteTag(ms, 8, WireType.LengthDelimited);
        WriteVarint(ms, 0); // empty packed

        OsmWay way = OsmWayParser.Parse(ms.ToArray());

        Assert.Empty(way.NodeRefs);
    }

    [Fact]
    public void Parse_EmptyKeysAndVals_BothEmpty()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);
        WriteTag(ms, 2, WireType.LengthDelimited);
        WriteVarint(ms, 0);
        WriteTag(ms, 3, WireType.LengthDelimited);
        WriteVarint(ms, 0);

        OsmWay way = OsmWayParser.Parse(ms.ToArray());

        Assert.Empty(way.TagKeys);
        Assert.Empty(way.TagValues);
    }

    [Fact]
    public void Parse_MissingId_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 8, WireType.LengthDelimited);
        WriteVarint(ms, 0);

        Assert.Throws<InvalidDataException>(() => OsmWayParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_KeysVsValsLengthMismatch_Throws()
    {
        byte[] bytes = BuildWay(
            id: 1L,
            tagKeys: new[] { 1u, 2u, 3u },
            tagVals: new[] { 1u, 2u });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            OsmWayParser.Parse(bytes));
        Assert.Contains("keys=3", ex.Message);
        Assert.Contains("vals=2", ex.Message);
    }

    [Fact]
    public void Parse_IdWrongWireType_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.LengthDelimited); // id は Varint のはず
        WriteVarint(ms, 1);
        ms.WriteByte(0);

        Assert.Throws<InvalidDataException>(() => OsmWayParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_RefsWrongWireType_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);
        WriteTag(ms, 8, WireType.Varint); // packed のはず
        WriteVarint(ms, 100UL);

        Assert.Throws<InvalidDataException>(() => OsmWayParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_KeysWrongWireType_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);
        WriteTag(ms, 2, WireType.Varint); // packed のはず
        WriteVarint(ms, 100UL);

        Assert.Throws<InvalidDataException>(() => OsmWayParser.Parse(ms.ToArray()));
    }

    // --- 補助メソッド --------------------------------------------------------

    private static byte[] BuildWay(
        long id,
        long[]? refs = null,
        uint[]? tagKeys = null,
        uint[]? tagVals = null)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, unchecked((ulong)id)); // int64 plain varint

        if (tagKeys is not null && tagKeys.Length > 0)
        {
            WriteTag(ms, 2, WireType.LengthDelimited);
            byte[] kp = PackUint32(tagKeys);
            WriteVarint(ms, (ulong)kp.Length);
            ms.Write(kp);
        }

        if (tagVals is not null && tagVals.Length > 0)
        {
            WriteTag(ms, 3, WireType.LengthDelimited);
            byte[] vp = PackUint32(tagVals);
            WriteVarint(ms, (ulong)vp.Length);
            ms.Write(vp);
        }

        if (refs is not null && refs.Length > 0)
        {
            WriteTag(ms, 8, WireType.LengthDelimited);
            byte[] rp = PackZigzag(ToDeltas(refs));
            WriteVarint(ms, (ulong)rp.Length);
            ms.Write(rp);
        }

        return ms.ToArray();
    }

    private static long[] ToDeltas(long[] absoluteValues)
    {
        var deltas = new long[absoluteValues.Length];
        long prev = 0;
        for (int i = 0; i < absoluteValues.Length; i++)
        {
            deltas[i] = absoluteValues[i] - prev;
            prev = absoluteValues[i];
        }
        return deltas;
    }

    private static byte[] PackZigzag(long[] values)
    {
        using var ms = new MemoryStream();
        foreach (long v in values)
        {
            WriteVarint(ms, ZigZagEncode(v));
        }
        return ms.ToArray();
    }

    private static byte[] PackUint32(uint[] values)
    {
        using var ms = new MemoryStream();
        foreach (uint v in values)
        {
            WriteVarint(ms, v);
        }
        return ms.ToArray();
    }

    private static void WriteTag(Stream output, int fieldNumber, WireType wireType)
    {
        ulong tag = ((ulong)(uint)fieldNumber << 3) | (uint)wireType;
        WriteVarint(output, tag);
    }

    private static void WriteVarint(Stream output, ulong value)
    {
        while (value >= 0x80)
        {
            output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        output.WriteByte((byte)value);
    }

    private static ulong ZigZagEncode(long value)
    {
        return (ulong)((value << 1) ^ (value >> 63));
    }
}
