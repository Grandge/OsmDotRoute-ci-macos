using System;
using System.IO;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Tests.Pbf.Osm;

public class DenseNodesParserTests
{
    [Fact]
    public void Parse_EmptyMessage_ReturnsEmptyArray()
    {
        OsmNode[] nodes = DenseNodesParser.Parse(ReadOnlySpan<byte>.Empty, CreateDefaultBlock());
        Assert.Empty(nodes);
    }

    [Fact]
    public void Parse_SingleNode_DecodedCorrectly()
    {
        // 1 ノード: id=100, encodedLat=200, encodedLon=300、tag なし
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 100L },
            lats: new[] { 200L },
            lons: new[] { 300L },
            keysVals: null);

        OsmNode[] nodes = DenseNodesParser.Parse(bytes, CreateDefaultBlock());

        Assert.Single(nodes);
        Assert.Equal(100L, nodes[0].Id);
        // granularity=100 → 200 * 100 * 1e-9 = 2e-5
        Assert.Equal(2e-5, nodes[0].Lat, precision: 12);
        Assert.Equal(3e-5, nodes[0].Lon, precision: 12);
        Assert.Empty(nodes[0].TagKeys);
        Assert.Empty(nodes[0].TagValues);
    }

    [Fact]
    public void Parse_MultipleNodes_DeltaDecoded()
    {
        // 絶対値 [100, 105, 110] → エンコード時の delta = [100, 5, 5] (zigzag varint)
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 100L, 105L, 110L },
            lats: new[] { 1_000_000_000L, 1_000_000_100L, 1_000_000_200L },
            lons: new[] { 2_000_000_000L, 2_000_000_100L, 2_000_000_200L },
            keysVals: null);

        OsmNode[] nodes = DenseNodesParser.Parse(bytes, CreateDefaultBlock());

        Assert.Equal(3, nodes.Length);
        Assert.Equal(new[] { 100L, 105L, 110L }, new[] { nodes[0].Id, nodes[1].Id, nodes[2].Id });
        Assert.Equal(100.0, nodes[0].Lat, precision: 9);
        Assert.Equal(200.0, nodes[0].Lon, precision: 9);
        // granularity=100 → 100 * 100 * 1e-9 = 1e-5 度の差分
        Assert.Equal(100.00001, nodes[1].Lat, precision: 9);
    }

    [Fact]
    public void Parse_NegativeIds_HandledViaZigzag()
    {
        // 絶対値 [-10, -5, 5] → delta = [-10, 5, 10]
        byte[] bytes = BuildDenseNodes(
            ids: new[] { -10L, -5L, 5L },
            lats: new[] { 0L, 0L, 0L },
            lons: new[] { 0L, 0L, 0L },
            keysVals: null);

        OsmNode[] nodes = DenseNodesParser.Parse(bytes, CreateDefaultBlock());

        Assert.Equal(new[] { -10L, -5L, 5L }, new[] { nodes[0].Id, nodes[1].Id, nodes[2].Id });
    }

    [Fact]
    public void Parse_NegativeCoordinates_DeltaDecoded()
    {
        // 南半球: lat absolute = -34°, -35°（delta -34 から -1）
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 1L, 2L },
            lats: new[] { -340_000_000L, -350_000_000L },
            lons: new[] { -1_800_000_000L, -1_790_000_000L },
            keysVals: null);

        OsmNode[] nodes = DenseNodesParser.Parse(bytes, CreateDefaultBlock());

        Assert.Equal(-34.0, nodes[0].Lat, precision: 9);
        Assert.Equal(-180.0, nodes[0].Lon, precision: 9);
        Assert.Equal(-35.0, nodes[1].Lat, precision: 9);
        Assert.Equal(-179.0, nodes[1].Lon, precision: 9);
    }

    [Fact]
    public void Parse_TagsDistributedToNodes()
    {
        // 3 ノード: Node0 = (1,2),(3,4) / Node1 = (5,6) / Node2 = tagless
        // keys_vals = [1, 2, 3, 4, 0,  5, 6, 0,  0]
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 1L, 2L, 3L },
            lats: new[] { 0L, 0L, 0L },
            lons: new[] { 0L, 0L, 0L },
            keysVals: new[] { 1, 2, 3, 4, 0, 5, 6, 0, 0 });

        OsmNode[] nodes = DenseNodesParser.Parse(bytes, CreateDefaultBlock());

        Assert.Equal(new[] { 1, 3 }, nodes[0].TagKeys);
        Assert.Equal(new[] { 2, 4 }, nodes[0].TagValues);
        Assert.Equal(new[] { 5 }, nodes[1].TagKeys);
        Assert.Equal(new[] { 6 }, nodes[1].TagValues);
        Assert.Empty(nodes[2].TagKeys);
        Assert.Empty(nodes[2].TagValues);
    }

    [Fact]
    public void Parse_AllTaglessEmptyKeysVals_AllNodesTagless()
    {
        // keys_vals フィールド自体を出さない（省略形）
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 1L, 2L, 3L },
            lats: new[] { 0L, 0L, 0L },
            lons: new[] { 0L, 0L, 0L },
            keysVals: null);

        OsmNode[] nodes = DenseNodesParser.Parse(bytes, CreateDefaultBlock());

        Assert.Equal(3, nodes.Length);
        foreach (var n in nodes)
        {
            Assert.Empty(n.TagKeys);
            Assert.Empty(n.TagValues);
        }
    }

    [Fact]
    public void Parse_AllTaglessExplicitZeros_AllNodesTagless()
    {
        // keys_vals = [0, 0, 0] (各ノードの区切りだけ)
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 1L, 2L, 3L },
            lats: new[] { 0L, 0L, 0L },
            lons: new[] { 0L, 0L, 0L },
            keysVals: new[] { 0, 0, 0 });

        OsmNode[] nodes = DenseNodesParser.Parse(bytes, CreateDefaultBlock());

        Assert.Equal(3, nodes.Length);
        foreach (var n in nodes)
        {
            Assert.Empty(n.TagKeys);
            Assert.Empty(n.TagValues);
        }
    }

    [Fact]
    public void Parse_IdLatLonLengthMismatch_Throws()
    {
        // id 3 個、lat 2 個 → 長さ不一致
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.LengthDelimited);
        byte[] idsPacked = PackZigzag(new long[] { 1L, 2L, 3L });
        WriteVarint(ms, (ulong)idsPacked.Length);
        ms.Write(idsPacked);

        WriteTag(ms, 8, WireType.LengthDelimited);
        byte[] latsPacked = PackZigzag(new long[] { 1L, 2L });
        WriteVarint(ms, (ulong)latsPacked.Length);
        ms.Write(latsPacked);

        WriteTag(ms, 9, WireType.LengthDelimited);
        byte[] lonsPacked = PackZigzag(new long[] { 1L, 2L, 3L });
        WriteVarint(ms, (ulong)lonsPacked.Length);
        ms.Write(lonsPacked);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            DenseNodesParser.Parse(ms.ToArray(), CreateDefaultBlock()));
        Assert.Contains("id=3", ex.Message);
        Assert.Contains("lat=2", ex.Message);
    }

    [Fact]
    public void Parse_DenseInfoFieldSkipped()
    {
        // DenseInfo (field 5) のダミーバイトを混入 → スキップ
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.LengthDelimited);
        byte[] idsPacked = PackZigzag(new long[] { 7L });
        WriteVarint(ms, (ulong)idsPacked.Length);
        ms.Write(idsPacked);

        WriteTag(ms, 5, WireType.LengthDelimited);
        byte[] dummyInfo = new byte[] { 0x08, 0x05 }; // field 1 varint 5 (DenseInfo.version)
        WriteVarint(ms, (ulong)dummyInfo.Length);
        ms.Write(dummyInfo);

        WriteTag(ms, 8, WireType.LengthDelimited);
        byte[] latsPacked = PackZigzag(new long[] { 0L });
        WriteVarint(ms, (ulong)latsPacked.Length);
        ms.Write(latsPacked);

        WriteTag(ms, 9, WireType.LengthDelimited);
        byte[] lonsPacked = PackZigzag(new long[] { 0L });
        WriteVarint(ms, (ulong)lonsPacked.Length);
        ms.Write(lonsPacked);

        OsmNode[] nodes = DenseNodesParser.Parse(ms.ToArray(), CreateDefaultBlock());

        Assert.Single(nodes);
        Assert.Equal(7L, nodes[0].Id);
    }

    [Fact]
    public void Parse_UnknownFieldSkipped()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.LengthDelimited);
        byte[] idsPacked = PackZigzag(new long[] { 1L });
        WriteVarint(ms, (ulong)idsPacked.Length);
        ms.Write(idsPacked);

        WriteTag(ms, 99, WireType.Varint);
        WriteVarint(ms, 12345UL);

        WriteTag(ms, 8, WireType.LengthDelimited);
        byte[] latsPacked = PackZigzag(new long[] { 0L });
        WriteVarint(ms, (ulong)latsPacked.Length);
        ms.Write(latsPacked);

        WriteTag(ms, 9, WireType.LengthDelimited);
        byte[] lonsPacked = PackZigzag(new long[] { 0L });
        WriteVarint(ms, (ulong)lonsPacked.Length);
        ms.Write(lonsPacked);

        OsmNode[] nodes = DenseNodesParser.Parse(ms.ToArray(), CreateDefaultBlock());

        Assert.Single(nodes);
    }

    [Fact]
    public void Parse_KeysValsTruncatedMidTag_Throws()
    {
        // keys_vals = [1] (k のみで v 欠落) → truncated mid-tag
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 1L },
            lats: new[] { 0L },
            lons: new[] { 0L },
            keysVals: new[] { 1 });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            DenseNodesParser.Parse(bytes, CreateDefaultBlock()));
        Assert.Contains("truncated", ex.Message);
    }

    [Fact]
    public void Parse_KeysValsMissingSeparator_Throws()
    {
        // 2 ノード、keys_vals = [1, 2] (区切り 0 なし)
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 1L, 2L },
            lats: new[] { 0L, 0L },
            lons: new[] { 0L, 0L },
            keysVals: new[] { 1, 2 });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            DenseNodesParser.Parse(bytes, CreateDefaultBlock()));
        Assert.Contains("missing 0-separator", ex.Message);
    }

    [Fact]
    public void Parse_KeysValsTrailingData_Throws()
    {
        // 1 ノード、keys_vals = [0, 0] (1 つ目で完了、残りはゴミ)
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 1L },
            lats: new[] { 0L },
            lons: new[] { 0L },
            keysVals: new[] { 0, 0 });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            DenseNodesParser.Parse(bytes, CreateDefaultBlock()));
        Assert.Contains("trailing", ex.Message);
    }

    [Fact]
    public void Parse_AppliesBlockOffsets()
    {
        // granularity=100, lonOffset=136.7e9 nanodegree、encodedLon delta=0 → 136.7°
        var block = new PrimitiveBlock(
            new OsmStringTable(Array.Empty<byte[]>()),
            granularity: 100,
            latOffset: 35_160_000_000L,
            lonOffset: 136_700_000_000L,
            dateGranularity: 1000);

        byte[] bytes = BuildDenseNodes(
            ids: new[] { 1L, 2L },
            lats: new[] { 0L, 0L },
            lons: new[] { 0L, 0L },
            keysVals: null);

        OsmNode[] nodes = DenseNodesParser.Parse(bytes, block);

        Assert.Equal(136.7, nodes[0].Lon, precision: 9);
        Assert.Equal(35.16, nodes[0].Lat, precision: 9);
        Assert.Equal(136.7, nodes[1].Lon, precision: 9);
        Assert.Equal(35.16, nodes[1].Lat, precision: 9);
    }

    [Fact]
    public void Parse_IdWrongWireType_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint); // id は packed (LengthDelimited) のはず
        WriteVarint(ms, 100UL);

        Assert.Throws<InvalidDataException>(() =>
            DenseNodesParser.Parse(ms.ToArray(), CreateDefaultBlock()));
    }

    [Fact]
    public void Parse_NullBlock_Throws()
    {
        byte[] bytes = BuildDenseNodes(
            ids: new[] { 1L }, lats: new[] { 0L }, lons: new[] { 0L }, keysVals: null);
        Assert.Throws<ArgumentNullException>(() =>
            DenseNodesParser.Parse(bytes, null!));
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

    private static byte[] BuildDenseNodes(long[] ids, long[] lats, long[] lons, int[]? keysVals)
    {
        using var ms = new MemoryStream();
        if (ids.Length > 0)
        {
            WriteTag(ms, 1, WireType.LengthDelimited);
            byte[] idsPacked = PackZigzag(ToDeltas(ids));
            WriteVarint(ms, (ulong)idsPacked.Length);
            ms.Write(idsPacked);
        }
        if (lats.Length > 0)
        {
            WriteTag(ms, 8, WireType.LengthDelimited);
            byte[] latsPacked = PackZigzag(ToDeltas(lats));
            WriteVarint(ms, (ulong)latsPacked.Length);
            ms.Write(latsPacked);
        }
        if (lons.Length > 0)
        {
            WriteTag(ms, 9, WireType.LengthDelimited);
            byte[] lonsPacked = PackZigzag(ToDeltas(lons));
            WriteVarint(ms, (ulong)lonsPacked.Length);
            ms.Write(lonsPacked);
        }
        if (keysVals is not null)
        {
            WriteTag(ms, 10, WireType.LengthDelimited);
            byte[] kvPacked = PackUint32(keysVals);
            WriteVarint(ms, (ulong)kvPacked.Length);
            ms.Write(kvPacked);
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

    private static byte[] PackUint32(int[] values)
    {
        using var ms = new MemoryStream();
        foreach (int v in values)
        {
            WriteVarint(ms, (ulong)(uint)v);
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
