using System;
using System.IO;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Tests.Pbf.Osm;

public class OsmNodeParserTests
{
    [Fact]
    public void Parse_MinimalNode_IdLonLatRead()
    {
        // 津島市付近の座標: lon=136.7, lat=35.16
        // granularity=100, offset=0 のとき encodedLon = 1_367_000_000, encodedLat = 351_600_000
        byte[] nodeBytes = BuildNode(
            id: 12345L,
            encodedLat: 351_600_000L,
            encodedLon: 1_367_000_000L);

        PrimitiveBlock block = CreateDefaultBlock();
        OsmNode node = OsmNodeParser.Parse(nodeBytes, block);

        Assert.Equal(12345L, node.Id);
        Assert.Equal(136.7, node.Lon, precision: 9);
        Assert.Equal(35.16, node.Lat, precision: 9);
        Assert.Empty(node.TagKeys);
        Assert.Empty(node.TagValues);
    }

    [Fact]
    public void Parse_NodeWithTags_TagsRead()
    {
        byte[] nodeBytes = BuildNode(
            id: 999L,
            encodedLat: 100_000_000L,
            encodedLon: 200_000_000L,
            tagKeys: new[] { 1u, 3u, 5u },
            tagVals: new[] { 2u, 4u, 6u });

        OsmNode node = OsmNodeParser.Parse(nodeBytes, CreateDefaultBlock());

        Assert.Equal(new[] { 1, 3, 5 }, node.TagKeys);
        Assert.Equal(new[] { 2, 4, 6 }, node.TagValues);
    }

    [Fact]
    public void Parse_NegativeNodeId_PreservedViaZigzag()
    {
        byte[] nodeBytes = BuildNode(
            id: -42L,
            encodedLat: 0L,
            encodedLon: 0L);

        OsmNode node = OsmNodeParser.Parse(nodeBytes, CreateDefaultBlock());

        Assert.Equal(-42L, node.Id);
    }

    [Fact]
    public void Parse_NegativeCoordinates_DecodedCorrectly()
    {
        // 南半球: encodedLat = -340_000_000 (granularity=100 → -34°)
        byte[] nodeBytes = BuildNode(
            id: 1L,
            encodedLat: -340_000_000L,
            encodedLon: -1_800_000_000L);

        OsmNode node = OsmNodeParser.Parse(nodeBytes, CreateDefaultBlock());

        Assert.Equal(-34.0, node.Lat, precision: 9);
        Assert.Equal(-180.0, node.Lon, precision: 9);
    }

    [Fact]
    public void Parse_AppliesBlockOffsets()
    {
        // granularity=100, lonOffset=136_700_000_000 (= 136.7°), encodedLon=0 → 136.7°
        var block = new PrimitiveBlock(
            new OsmStringTable(Array.Empty<byte[]>()),
            granularity: 100,
            latOffset: 35_160_000_000L,
            lonOffset: 136_700_000_000L,
            dateGranularity: 1000);

        byte[] nodeBytes = BuildNode(id: 1L, encodedLat: 0L, encodedLon: 0L);

        OsmNode node = OsmNodeParser.Parse(nodeBytes, block);

        Assert.Equal(136.7, node.Lon, precision: 9);
        Assert.Equal(35.16, node.Lat, precision: 9);
    }

    [Fact]
    public void Parse_InfoFieldSkipped()
    {
        // Info (field 4) を含む Node → スキップされて他フィールドは正しく読まれる
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(7L));
        // Info: 適当な内部内容 (field 1 = 5)
        var info = new MemoryStream();
        WriteTag(info, 1, WireType.Varint);
        WriteVarint(info, 5);
        byte[] infoBytes = info.ToArray();
        WriteTag(ms, 4, WireType.LengthDelimited);
        WriteVarint(ms, (ulong)infoBytes.Length);
        ms.Write(infoBytes);
        WriteTag(ms, 8, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(100L));
        WriteTag(ms, 9, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(200L));

        OsmNode node = OsmNodeParser.Parse(ms.ToArray(), CreateDefaultBlock());

        Assert.Equal(7L, node.Id);
    }

    [Fact]
    public void Parse_UnknownFieldNumber_Skipped()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(1L));
        WriteTag(ms, 99, WireType.Varint); // 未知 field
        WriteVarint(ms, 12345UL);
        WriteTag(ms, 8, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));
        WriteTag(ms, 9, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));

        OsmNode node = OsmNodeParser.Parse(ms.ToArray(), CreateDefaultBlock());

        Assert.Equal(1L, node.Id);
    }

    [Fact]
    public void Parse_EmptyKeysAndVals_BothArrayEmpty()
    {
        // keys / vals 共に length-0 の packed → 空配列
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(1L));
        WriteTag(ms, 2, WireType.LengthDelimited);
        WriteVarint(ms, 0); // 空 packed
        WriteTag(ms, 3, WireType.LengthDelimited);
        WriteVarint(ms, 0);
        WriteTag(ms, 8, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));
        WriteTag(ms, 9, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));

        OsmNode node = OsmNodeParser.Parse(ms.ToArray(), CreateDefaultBlock());

        Assert.Empty(node.TagKeys);
        Assert.Empty(node.TagValues);
    }

    [Fact]
    public void Parse_MissingId_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 8, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));
        WriteTag(ms, 9, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));

        Assert.Throws<InvalidDataException>(() =>
            OsmNodeParser.Parse(ms.ToArray(), CreateDefaultBlock()));
    }

    [Fact]
    public void Parse_MissingLat_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(1L));
        WriteTag(ms, 9, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));

        Assert.Throws<InvalidDataException>(() =>
            OsmNodeParser.Parse(ms.ToArray(), CreateDefaultBlock()));
    }

    [Fact]
    public void Parse_MissingLon_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(1L));
        WriteTag(ms, 8, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));

        Assert.Throws<InvalidDataException>(() =>
            OsmNodeParser.Parse(ms.ToArray(), CreateDefaultBlock()));
    }

    [Fact]
    public void Parse_KeysCountMismatch_Throws()
    {
        // keys 3 個、vals 2 個 → 不一致
        byte[] nodeBytes = BuildNode(
            id: 1L,
            encodedLat: 0L,
            encodedLon: 0L,
            tagKeys: new[] { 1u, 2u, 3u },
            tagVals: new[] { 1u, 2u });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            OsmNodeParser.Parse(nodeBytes, CreateDefaultBlock()));
        Assert.Contains("keys=3", ex.Message);
        Assert.Contains("vals=2", ex.Message);
    }

    [Fact]
    public void Parse_NonPackedKeys_Throws()
    {
        // keys を Varint (個別形式) で送る → packed 専用なのでエラー
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(1L));
        WriteTag(ms, 2, WireType.Varint); // packed のはず
        WriteVarint(ms, 100UL);
        WriteTag(ms, 8, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));
        WriteTag(ms, 9, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));

        Assert.Throws<InvalidDataException>(() =>
            OsmNodeParser.Parse(ms.ToArray(), CreateDefaultBlock()));
    }

    [Fact]
    public void Parse_IdWrongWireType_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.LengthDelimited); // id は Varint のはず
        WriteVarint(ms, 1);
        ms.WriteByte(0);
        WriteTag(ms, 8, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));
        WriteTag(ms, 9, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(0L));

        Assert.Throws<InvalidDataException>(() =>
            OsmNodeParser.Parse(ms.ToArray(), CreateDefaultBlock()));
    }

    [Fact]
    public void Parse_NullBlock_Throws()
    {
        byte[] nodeBytes = BuildNode(id: 1L, encodedLat: 0L, encodedLon: 0L);
        Assert.Throws<ArgumentNullException>(() =>
            OsmNodeParser.Parse(nodeBytes, null!));
    }

    // --- 補助メソッド --------------------------------------------------------

    private static PrimitiveBlock CreateDefaultBlock()
    {
        return new PrimitiveBlock(
            new OsmStringTable(Array.Empty<byte[]>()),
            granularity: 100,
            latOffset: 0,
            lonOffset: 0,
            dateGranularity: 1000);
    }

    private static byte[] BuildNode(
        long id, long encodedLat, long encodedLon,
        uint[]? tagKeys = null, uint[]? tagVals = null)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(id));

        if (tagKeys is not null && tagKeys.Length > 0)
        {
            WriteTag(ms, 2, WireType.LengthDelimited);
            byte[] keysPacked = PackUint32(tagKeys);
            WriteVarint(ms, (ulong)keysPacked.Length);
            ms.Write(keysPacked);
        }

        if (tagVals is not null && tagVals.Length > 0)
        {
            WriteTag(ms, 3, WireType.LengthDelimited);
            byte[] valsPacked = PackUint32(tagVals);
            WriteVarint(ms, (ulong)valsPacked.Length);
            ms.Write(valsPacked);
        }

        WriteTag(ms, 8, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(encodedLat));
        WriteTag(ms, 9, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(encodedLon));

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
