using System;
using System.IO;
using System.Text;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Tests.Pbf.Osm;

public class PrimitiveBlockParserTests
{
    [Fact]
    public void Parse_MinimalStringTable_ParsesAndDefaultsUsed()
    {
        // StringTable のみ（granularity / offsets は default）
        byte[] stringTableBytes = BuildStringTable("", "highway", "primary");
        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, stringTableBytes);

        PrimitiveBlock block = PrimitiveBlockParser.Parse(ms.ToArray());

        Assert.Equal(3, block.StringTable.Count);
        Assert.Equal(string.Empty, block.StringTable.GetString(0));
        Assert.Equal("highway", block.StringTable.GetString(1));
        Assert.Equal("primary", block.StringTable.GetString(2));

        // Defaults
        Assert.Equal(PrimitiveBlockParser.DefaultGranularity, block.Granularity);
        Assert.Equal(0L, block.LatOffset);
        Assert.Equal(0L, block.LonOffset);
        Assert.Equal(PrimitiveBlockParser.DefaultDateGranularity, block.DateGranularity);
    }

    [Fact]
    public void Parse_CustomGranularityAndOffsets_ReadCorrectly()
    {
        byte[] stringTableBytes = BuildStringTable("");

        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, stringTableBytes);
        WriteTag(ms, fieldNumber: 17, WireType.Varint);
        WriteVarint(ms, 1000UL); // granularity = 1000 ナノ度 = 1e-6 度
        WriteTag(ms, fieldNumber: 18, WireType.Varint);
        WriteVarint(ms, 2000UL); // date_granularity = 2000 ms
        WriteTag(ms, fieldNumber: 19, WireType.Varint);
        WriteVarintSigned(ms, 35_160_000_000L); // lat_offset
        WriteTag(ms, fieldNumber: 20, WireType.Varint);
        WriteVarintSigned(ms, 136_700_000_000L); // lon_offset

        PrimitiveBlock block = PrimitiveBlockParser.Parse(ms.ToArray());

        Assert.Equal(1000, block.Granularity);
        Assert.Equal(2000, block.DateGranularity);
        Assert.Equal(35_160_000_000L, block.LatOffset);
        Assert.Equal(136_700_000_000L, block.LonOffset);
    }

    [Fact]
    public void Parse_NegativeOffsets_PreservedAsSigned()
    {
        byte[] stringTableBytes = BuildStringTable("");

        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, stringTableBytes);
        WriteTag(ms, fieldNumber: 19, WireType.Varint);
        WriteVarintSigned(ms, -33_000_000_000L);
        WriteTag(ms, fieldNumber: 20, WireType.Varint);
        WriteVarintSigned(ms, -180_000_000_000L);

        PrimitiveBlock block = PrimitiveBlockParser.Parse(ms.ToArray());

        Assert.Equal(-33_000_000_000L, block.LatOffset);
        Assert.Equal(-180_000_000_000L, block.LonOffset);
    }

    [Fact]
    public void Parse_PrimitiveGroupsField_Skipped()
    {
        // field 2 (primitivegroup) のダミーバイトを混入 → スキップされる
        byte[] stringTableBytes = BuildStringTable("", "k");

        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, stringTableBytes);
        WriteLengthDelimited(ms, fieldNumber: 2, new byte[] { 0x01, 0x02, 0x03, 0x04 });
        WriteTag(ms, fieldNumber: 17, WireType.Varint);
        WriteVarint(ms, 50UL);

        PrimitiveBlock block = PrimitiveBlockParser.Parse(ms.ToArray());

        Assert.Equal(2, block.StringTable.Count);
        Assert.Equal(50, block.Granularity);
    }

    [Fact]
    public void Parse_UnknownFieldNumber_Skipped()
    {
        byte[] stringTableBytes = BuildStringTable("");
        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, stringTableBytes);
        WriteTag(ms, fieldNumber: 99, WireType.Varint);
        WriteVarint(ms, 12345UL);

        PrimitiveBlock block = PrimitiveBlockParser.Parse(ms.ToArray());

        Assert.Equal(1, block.StringTable.Count);
        Assert.Equal(PrimitiveBlockParser.DefaultGranularity, block.Granularity);
    }

    [Fact]
    public void Parse_MissingStringTable_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, fieldNumber: 17, WireType.Varint);
        WriteVarint(ms, 100UL);

        Assert.Throws<InvalidDataException>(() => PrimitiveBlockParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_GranularityZero_Throws()
    {
        byte[] stringTableBytes = BuildStringTable("");
        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, stringTableBytes);
        WriteTag(ms, fieldNumber: 17, WireType.Varint);
        WriteVarint(ms, 0UL);

        Assert.Throws<InvalidDataException>(() => PrimitiveBlockParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_GranularityNegative_Throws()
    {
        // -1 as int32 → varint encoded as 10-byte (sign-extended)。読込時 uint32 範囲超で別例外、
        // または checked cast で OverflowException → InvalidDataException 化されない。
        // ここでは ProtoReader 段で uint32 範囲超を InvalidDataException として投げる挙動を検証
        byte[] stringTableBytes = BuildStringTable("");
        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, stringTableBytes);
        WriteTag(ms, fieldNumber: 17, WireType.Varint);
        WriteVarintSigned(ms, -1L); // 10 バイト、uint32 範囲超

        // ReadVarint32() が uint32 範囲超で InvalidDataException を投げる
        Assert.Throws<InvalidDataException>(() => PrimitiveBlockParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_StringTableWrongWireType_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, fieldNumber: 1, WireType.Varint); // stringtable は LengthDelimited のはず
        WriteVarint(ms, 42UL);

        Assert.Throws<InvalidDataException>(() => PrimitiveBlockParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_GranularityWrongWireType_Throws()
    {
        byte[] stringTableBytes = BuildStringTable("");
        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, stringTableBytes);
        WriteLengthDelimited(ms, fieldNumber: 17, new byte[] { 0x01 });

        Assert.Throws<InvalidDataException>(() => PrimitiveBlockParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void ParseStringTable_EmptyMessage_ReturnsEmpty()
    {
        OsmStringTable table = PrimitiveBlockParser.ParseStringTable(ReadOnlySpan<byte>.Empty);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void ParseStringTable_MultipleStrings_OrderPreserved()
    {
        byte[] bytes = BuildStringTable("", "highway", "residential", "name", "本町通り");

        OsmStringTable table = PrimitiveBlockParser.ParseStringTable(bytes);

        Assert.Equal(5, table.Count);
        Assert.Equal(string.Empty, table.GetString(0));
        Assert.Equal("highway", table.GetString(1));
        Assert.Equal("residential", table.GetString(2));
        Assert.Equal("name", table.GetString(3));
        Assert.Equal("本町通り", table.GetString(4));
    }

    [Fact]
    public void ParseStringTable_GetBytes_ReturnsUtf8Bytes()
    {
        byte[] bytes = BuildStringTable("highway");
        OsmStringTable table = PrimitiveBlockParser.ParseStringTable(bytes);

        ReadOnlySpan<byte> span = table.GetBytes(0);

        Assert.Equal(new byte[] { (byte)'h', (byte)'i', (byte)'g', (byte)'h', (byte)'w', (byte)'a', (byte)'y' },
            span.ToArray());
    }

    [Fact]
    public void ParseStringTable_UnknownFieldSkipped()
    {
        // s フィールド以外も混入 → スキップされる
        using var ms = new MemoryStream();
        WriteTag(ms, fieldNumber: 1, WireType.LengthDelimited);
        WriteVarint(ms, 3UL);
        ms.Write(Encoding.UTF8.GetBytes("abc"));
        WriteTag(ms, fieldNumber: 99, WireType.Varint);
        WriteVarint(ms, 12345UL);
        WriteTag(ms, fieldNumber: 1, WireType.LengthDelimited);
        WriteVarint(ms, 3UL);
        ms.Write(Encoding.UTF8.GetBytes("def"));

        OsmStringTable table = PrimitiveBlockParser.ParseStringTable(ms.ToArray());

        Assert.Equal(2, table.Count);
        Assert.Equal("abc", table.GetString(0));
        Assert.Equal("def", table.GetString(1));
    }

    [Fact]
    public void PrimitiveBlock_ToLonLat_DefaultGranularity()
    {
        // default granularity=100, offsets=0
        var block = new PrimitiveBlock(
            new OsmStringTable(Array.Empty<byte[]>()),
            granularity: 100,
            latOffset: 0,
            lonOffset: 0,
            dateGranularity: 1000);

        // encoded = 1_367_000_000 → 1e-9 * 100 * 1_367_000_000 = 136.7
        Assert.Equal(136.7, block.ToLon(1_367_000_000L), precision: 9);
        Assert.Equal(35.16, block.ToLat(351_600_000L), precision: 9);
    }

    [Fact]
    public void PrimitiveBlock_ToLonLat_WithOffsets()
    {
        // granularity=100, lonOffset = 136_700_000_000 (= 136.7°), encodedLon = 0 → result = 136.7°
        var block = new PrimitiveBlock(
            new OsmStringTable(Array.Empty<byte[]>()),
            granularity: 100,
            latOffset: 35_160_000_000L,
            lonOffset: 136_700_000_000L,
            dateGranularity: 1000);

        Assert.Equal(136.7, block.ToLon(0), precision: 9);
        Assert.Equal(35.16, block.ToLat(0), precision: 9);

        // encoded = 100_000 → 1e-9 * (136_700_000_000 + 100 * 100_000) = 136.71
        Assert.Equal(136.71, block.ToLon(100_000L), precision: 9);
    }

    [Fact]
    public void PrimitiveBlock_ToLonLat_NegativeCoordinates()
    {
        var block = new PrimitiveBlock(
            new OsmStringTable(Array.Empty<byte[]>()),
            granularity: 100,
            latOffset: -33_000_000_000L,
            lonOffset: 0,
            dateGranularity: 1000);

        // encodedLat=0, offset=-33e9 → -33.0
        Assert.Equal(-33.0, block.ToLat(0), precision: 9);
        // encodedLon=-1_800_000_000, no offset → 1e-9 * 100 * -1_800_000_000 = -180.0
        Assert.Equal(-180.0, block.ToLon(-1_800_000_000L), precision: 9);
    }

    [Fact]
    public void PrimitiveBlock_NullStringTable_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PrimitiveBlock(null!, 100, 0, 0, 1000));
    }

    [Fact]
    public void OsmStringTable_NullItems_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OsmStringTable(null!));
    }

    // --- 補助メソッド --------------------------------------------------------

    private static byte[] BuildStringTable(params string[] strings)
    {
        using var ms = new MemoryStream();
        foreach (string s in strings)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            WriteTag(ms, fieldNumber: 1, WireType.LengthDelimited);
            WriteVarint(ms, (ulong)bytes.Length);
            ms.Write(bytes);
        }
        return ms.ToArray();
    }

    private static void WriteLengthDelimited(Stream output, int fieldNumber, byte[] payload)
    {
        WriteTag(output, fieldNumber, WireType.LengthDelimited);
        WriteVarint(output, (ulong)payload.Length);
        output.Write(payload);
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

    /// <summary>signed int64 を varint で書き込む（int64 ワイヤ形式：負値は 10 バイト sign-extended）。</summary>
    private static void WriteVarintSigned(Stream output, long value)
    {
        WriteVarint(output, unchecked((ulong)value));
    }
}
