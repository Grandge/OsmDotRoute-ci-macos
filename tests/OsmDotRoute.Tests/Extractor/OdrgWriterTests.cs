using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.8 — <see cref="OdrgWriter.Write"/> のバイナリ出力テスト。
/// </summary>
public sealed class OdrgWriterTests
{
    private static readonly OsmStringTable EmptyStringTable = new(new[] { Array.Empty<byte>() });

    /// <summary>テスト用の最小入力 (頂点 2 / エッジ 1 / プロファイル 1)。</summary>
    private static OdrgWriteInput MinimalInput()
    {
        var vertices = new[]
        {
            new GeoCoordinate(35.16, 136.70),
            new GeoCoordinate(35.20, 136.78),
        };
        var edges = new[]
        {
            new EdgeRecord(
                OsmWayId: 1,
                FromVertexId: 0,
                ToVertexId: 1,
                ShapeNodeRefs: Array.Empty<long>(),
                TagKeys: Array.Empty<int>(),
                TagValues: Array.Empty<int>(),
                StringTable: EmptyStringTable),
        };
        var aabbs = new[] { new Aabb(136.70, 35.16, 136.78, 35.20) };
        var flags = new[] { EdgeFlags.None };
        var rtree = new StrRTree(
            Nodes: new[]
            {
                RTreeNode.Create(new Aabb(136.70, 35.16, 136.78, 35.20),
                    firstChildIndex: 0, childCount: 1, isLeaf: true),
            },
            RootIndex: 0,
            BranchingFactor: 16,
            TreeHeight: 1,
            EdgePermutation: new[] { 0 });

        var table = new BakedProfileTable(
            new[] { "car" },
            new[] { new[] { BakedProfileEntry.Create(true, 50f, true, true) } });

        return new OdrgWriteInput(
            Vertices: vertices,
            Edges: edges,
            EdgeAabbs: aabbs,
            EdgeFlags: flags,
            RTree: rtree,
            ProfileTable: table,
            NodeCoordLookup: _ => default,
            Bbox: new Aabb(136.70, 35.16, 136.78, 35.20),
            MetadataJson: "{}");
    }

    private static byte[] WriteToBytes(OdrgWriteInput input)
    {
        using var ms = new MemoryStream();
        OdrgWriter.Write(ms, input);
        return ms.ToArray();
    }

    [Fact]
    public void Write_Magic_StartsWithODRG()
    {
        var bytes = WriteToBytes(MinimalInput());
        Assert.Equal((byte)'O', bytes[0]);
        Assert.Equal((byte)'D', bytes[1]);
        Assert.Equal((byte)'R', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
        Assert.Equal((byte)0, bytes[4]);
        Assert.Equal((byte)0, bytes[5]);
        Assert.Equal((byte)0, bytes[6]);
        Assert.Equal((byte)0, bytes[7]);
    }

    [Fact]
    public void Write_HeaderVersions_AreMajor1Minor1()
    {
        // v0.3 で VersionMinor 0 → 1 に bump (bboxRequested 追加)
        var bytes = WriteToBytes(MinimalInput());
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10, 2)));
    }

    [Fact]
    public void Write_HeaderCounts_MatchInput()
    {
        var bytes = WriteToBytes(MinimalInput());
        Assert.Equal(2u, (uint)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(16, 8)));  // vertexCount
        Assert.Equal(1u, (uint)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(24, 8)));  // edgeCount
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(64, 4)));        // profileCount
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(68, 4)));        // edgeFlagBytes
    }

    [Fact]
    public void Write_HeaderBbox_MatchInput()
    {
        var bytes = WriteToBytes(MinimalInput());
        Assert.Equal(136.70, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(32, 8)));
        Assert.Equal(35.16, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(40, 8)));
        Assert.Equal(136.78, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(48, 8)));
        Assert.Equal(35.20, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(56, 8)));
    }

    [Fact]
    public void Write_HeaderRequestedBbox_DefaultIsZero()
    {
        // MinimalInput では RequestedBbox 未指定 → default(Aabb) = 全 0
        var bytes = WriteToBytes(MinimalInput());
        Assert.Equal(0.0, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(88, 8)));
        Assert.Equal(0.0, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(96, 8)));
        Assert.Equal(0.0, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(104, 8)));
        Assert.Equal(0.0, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(112, 8)));
    }

    [Fact]
    public void Write_HeaderRequestedBbox_Explicit_RoundTrips()
    {
        var input = MinimalInput() with { RequestedBbox = new Aabb(136.50, 35.10, 137.00, 35.30) };
        var bytes = WriteToBytes(input);
        Assert.Equal(136.50, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(88, 8)));
        Assert.Equal(35.10, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(96, 8)));
        Assert.Equal(137.00, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(104, 8)));
        Assert.Equal(35.30, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(112, 8)));

        // OdrgReader でも読めること
        var result = OdrgReader.Parse(bytes);
        Assert.True(result.Header.HasRequestedBbox);
        Assert.Equal(136.50, result.Header.RequestedBbox.MinLon);
        Assert.Equal(35.10, result.Header.RequestedBbox.MinLat);
        Assert.Equal(137.00, result.Header.RequestedBbox.MaxLon);
        Assert.Equal(35.30, result.Header.RequestedBbox.MaxLat);
    }

    [Fact]
    public void Write_SectionTableOffset_Is256()
    {
        var bytes = WriteToBytes(MinimalInput());
        Assert.Equal(256u, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(72, 8)));
        Assert.Equal(9u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80, 4)));  // sectionCount
    }

    [Fact]
    public void Write_HeaderSize_Is256Bytes()
    {
        // 最初のセクションエントリは offset 256 から始まる
        var bytes = WriteToBytes(MinimalInput());
        ushort firstSectionKind = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(256, 2));
        Assert.Equal(OdrgFormat.SectionVertexTable, firstSectionKind);
    }

    [Fact]
    public void Write_SectionTable_ContainsAllNineSections()
    {
        var bytes = WriteToBytes(MinimalInput());
        ushort[] expected =
        {
            OdrgFormat.SectionVertexTable,
            OdrgFormat.SectionEdgeTable,
            OdrgFormat.SectionEdgeShapeBuffer,
            OdrgFormat.SectionEdgeAabbTable,
            OdrgFormat.SectionEdgeFlagTable,
            OdrgFormat.SectionEdgeSpatialIndex,
            OdrgFormat.SectionBakedProfileTable,
            OdrgFormat.SectionTurnRestrictionTable,
            OdrgFormat.SectionMetadata,
        };
        for (int i = 0; i < 9; i++)
        {
            int entryOffset = 256 + i * 24;
            ushort kind = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryOffset, 2));
            Assert.Equal(expected[i], kind);
        }
    }

    [Fact]
    public void Write_VertexSectionContent_MatchesInput()
    {
        var bytes = WriteToBytes(MinimalInput());
        // Vertex section offset = 256 + 9*24 = 472
        long offset = 472;
        ulong lengthFromSectionTable = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 16, 8));
        Assert.Equal(32UL, lengthFromSectionTable);  // 2 vertices × 16 bytes

        Assert.Equal(136.70, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)offset, 8)));
        Assert.Equal(35.16, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)offset + 8, 8)));
        Assert.Equal(136.78, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)offset + 16, 8)));
        Assert.Equal(35.20, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)offset + 24, 8)));
    }

    [Fact]
    public void Write_EdgeSectionContent_MatchesInput()
    {
        var bytes = WriteToBytes(MinimalInput());
        // Vertex section ends at 472 + 32 = 504, so edge section starts at 504
        // section table entry 1 (Edge Table) starts at 256 + 24 = 280
        long edgeOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(280 + 8, 8));
        Assert.Equal(504L, edgeOffset);  // 472 + 32

        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)edgeOffset, 4)));        // fromVertexId
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)edgeOffset + 4, 4)));    // toVertexId
        Assert.Equal(0UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan((int)edgeOffset + 8, 8)));   // shapeOffset
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)edgeOffset + 16, 4)));   // shapeLength
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)edgeOffset + 20, 4)));   // bakedProfileIndex == 0
    }

    [Fact]
    public void Write_ShapeBuffer_ResolvesCoordinates()
    {
        // 中間ノード 2 個を持つエッジ
        var input = MinimalInput() with
        {
            Edges = new[]
            {
                new EdgeRecord(
                    OsmWayId: 1,
                    FromVertexId: 0,
                    ToVertexId: 1,
                    ShapeNodeRefs: new long[] { 100, 200 },
                    TagKeys: Array.Empty<int>(),
                    TagValues: Array.Empty<int>(),
                    StringTable: EmptyStringTable),
            },
            NodeCoordLookup = id => id switch
            {
                100 => new GeoCoordinate(35.17, 136.72),
                200 => new GeoCoordinate(35.18, 136.74),
                _ => default,
            },
        };
        var bytes = WriteToBytes(input);

        // Edge Shape Buffer section offset (section table entry 2)
        long shapeOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 2 + 8, 8));
        ulong shapeLength = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 2 + 16, 8));
        Assert.Equal(32UL, shapeLength);  // 2 points × 16 bytes

        Assert.Equal(136.72, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)shapeOffset, 8)));
        Assert.Equal(35.17, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)shapeOffset + 8, 8)));
        Assert.Equal(136.74, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)shapeOffset + 16, 8)));
        Assert.Equal(35.18, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)shapeOffset + 24, 8)));
    }

    [Fact]
    public void Write_AabbSection_MatchesInput()
    {
        var bytes = WriteToBytes(MinimalInput());
        long off = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 3 + 8, 8));
        Assert.Equal(136.70, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)off, 8)));
        Assert.Equal(35.16, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)off + 8, 8)));
        Assert.Equal(136.78, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)off + 16, 8)));
        Assert.Equal(35.20, BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan((int)off + 24, 8)));
    }

    [Fact]
    public void Write_FlagSection_MatchesInput()
    {
        var input = MinimalInput() with
        {
            EdgeFlags = new[] { EdgeFlags.IsBridge | EdgeFlags.IsOnewayForward },  // bit0 + bit12 = 0x1001
        };
        var bytes = WriteToBytes(input);
        long off = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 4 + 8, 8));
        ushort flagsRead = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)off, 2));
        Assert.Equal((ushort)0x1001, flagsRead);
    }

    [Fact]
    public void Write_RTreeHeaderAndNode_MatchInput()
    {
        var bytes = WriteToBytes(MinimalInput());
        long off = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 5 + 8, 8));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)off, 4)));         // nodeCount
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)off + 4, 4)));     // rootIndex
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)off + 8, 4)));    // branching
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)off + 12, 4)));    // height

        long nodeOff = off + 16;  // header 16B 後
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)nodeOff + 32, 4))); // firstChild
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)nodeOff + 36, 4))); // childCount
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)nodeOff + 40, 4))); // flags (Leaf bit)
    }

    [Fact]
    public void Write_BakedProfileTable_MatchesInput()
    {
        var bytes = WriteToBytes(MinimalInput());
        long off = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 6 + 8, 8));

        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)off, 4)));      // profileCount
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)off + 4, 4)));  // entrySize

        // name table: nameOffset=0, nameLength=3 ("car")
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)off + 8, 4)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)off + 12, 4)));

        // name buffer
        string name = Encoding.UTF8.GetString(bytes.AsSpan((int)off + 16, 3));
        Assert.Equal("car", name);

        // entry table: speed=50, flags=0b111 (CanPass + Forward + Backward)
        long entryOff = off + 16 + 3;
        Assert.Equal(50f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan((int)entryOff, 4)));
        Assert.Equal((byte)0b111, bytes[(int)entryOff + 4]);
    }

    [Fact]
    public void Write_TurnRestriction_HasZeroLength()
    {
        var bytes = WriteToBytes(MinimalInput());
        ulong length = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 7 + 16, 8));
        Assert.Equal(0UL, length);  // v0.2 では予約
    }

    [Fact]
    public void Write_MetadataSection_ContainsJson()
    {
        var input = MinimalInput() with { MetadataJson = "{\"hello\":\"world\"}" };
        var bytes = WriteToBytes(input);
        long off = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 8 + 8, 8));
        ulong length = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 8 + 16, 8));

        string json = Encoding.UTF8.GetString(bytes.AsSpan((int)off, (int)length));
        Assert.Equal("{\"hello\":\"world\"}", json);
    }

    [Fact]
    public void Write_TotalSize_MatchesSectionTable()
    {
        var bytes = WriteToBytes(MinimalInput());
        // last section の end は ファイル末尾と一致するはず
        long lastOff = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 8 + 8, 8));
        long lastLen = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 8 + 16, 8));
        Assert.Equal(bytes.Length, lastOff + lastLen);
    }

    [Fact]
    public void Write_NullArgs_Throws()
    {
        using var ms = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => OdrgWriter.Write(null!, MinimalInput()));
        Assert.Throws<ArgumentNullException>(() => OdrgWriter.Write(ms, null!));
    }
}
