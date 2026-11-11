using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OsmDotRoute.Pbf;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Tests.Pbf;

public class PbfReaderTests
{
    [Fact]
    public void Read_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PbfReader.Read(null!));
    }

    [Fact]
    public void Read_EmptyStream_Throws()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        Assert.Throws<InvalidDataException>(() => PbfReader.Read(ms));
    }

    [Fact]
    public void Read_FirstBlobNotHeader_Throws()
    {
        // 先頭 blob を OSMData にする → エラー
        byte[] pbf = BuildPbfBytes(
            (BlobKind.Data, BuildMinimalPrimitiveBlock()));

        using var ms = new MemoryStream(pbf);
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => PbfReader.Read(ms));
        Assert.Contains("First PBF blob must be OSMHeader", ex.Message);
    }

    [Fact]
    public void Read_DuplicateHeaderBlob_Throws()
    {
        byte[] pbf = BuildPbfBytes(
            (BlobKind.Header, BuildMinimalHeader()),
            (BlobKind.Header, BuildMinimalHeader()));

        using var ms = new MemoryStream(pbf);
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => PbfReader.Read(ms));
        Assert.Contains("duplicate OSMHeader blob", ex.Message);
    }

    [Fact]
    public void Read_UnsupportedRequiredFeature_Throws()
    {
        byte[] headerPayload = BuildHeaderPayload("OsmSchema-V0.6", "HistoricalInformation");
        byte[] pbf = BuildPbfBytes((BlobKind.Header, headerPayload));

        using var ms = new MemoryStream(pbf);
        Assert.Throws<NotSupportedException>(() => PbfReader.Read(ms));
    }

    [Fact]
    public void Read_HeaderOnly_ReturnsHeaderNoCallbacks()
    {
        byte[] pbf = BuildPbfBytes((BlobKind.Header, BuildMinimalHeader()));

        int nodeCount = 0, wayCount = 0, relationCount = 0;
        using var ms = new MemoryStream(pbf);
        OsmHeader header = PbfReader.Read(ms,
            onNode: (_, _) => nodeCount++,
            onWay: (_, _) => wayCount++,
            onRelation: (_, _) => relationCount++);

        Assert.Contains("OsmSchema-V0.6", header.RequiredFeatures);
        Assert.Equal(0, nodeCount);
        Assert.Equal(0, wayCount);
        Assert.Equal(0, relationCount);
    }

    [Fact]
    public void Read_HeaderAndEmptyDataBlock_NoCallbacks()
    {
        byte[] pbf = BuildPbfBytes(
            (BlobKind.Header, BuildMinimalHeader()),
            (BlobKind.Data, BuildMinimalPrimitiveBlock()));

        int totalCallbacks = 0;
        using var ms = new MemoryStream(pbf);
        PbfReader.Read(ms,
            onNode: (_, _) => totalCallbacks++,
            onWay: (_, _) => totalCallbacks++,
            onRelation: (_, _) => totalCallbacks++);

        Assert.Equal(0, totalCallbacks);
    }

    [Fact]
    public void Read_HeaderAndWayBlock_WayCallbackInvoked()
    {
        // PrimitiveBlock + PrimitiveGroup with 2 Ways
        byte[] way1 = BuildWayMessage(id: 100L, refs: new[] { 1L, 2L, 3L });
        byte[] way2 = BuildWayMessage(id: 200L, refs: new[] { 4L, 5L });
        byte[] group = BuildPrimitiveGroup(wayBytes: new[] { way1, way2 });
        byte[] block = BuildPrimitiveBlock(groups: new[] { group });

        byte[] pbf = BuildPbfBytes(
            (BlobKind.Header, BuildMinimalHeader()),
            (BlobKind.Data, block));

        var seenWayIds = new List<long>();
        using var ms = new MemoryStream(pbf);
        PbfReader.Read(ms, onWay: (way, _) => seenWayIds.Add(way.Id));

        Assert.Equal(new[] { 100L, 200L }, seenWayIds);
    }

    [Fact]
    public void Read_HeaderAndRelationBlock_RelationCallbackInvoked()
    {
        byte[] rel1 = BuildRelationMessage(id: 500L);
        byte[] group = BuildPrimitiveGroup(relationBytes: new[] { rel1 });
        byte[] block = BuildPrimitiveBlock(groups: new[] { group });

        byte[] pbf = BuildPbfBytes(
            (BlobKind.Header, BuildMinimalHeader()),
            (BlobKind.Data, block));

        var seenIds = new List<long>();
        using var ms = new MemoryStream(pbf);
        PbfReader.Read(ms, onRelation: (rel, _) => seenIds.Add(rel.Id));

        Assert.Equal(new[] { 500L }, seenIds);
    }

    [Fact]
    public void Read_NullCallbackForSection_SkipsSilently()
    {
        // way 入りブロックを onWay = null で読む → way は飛ばし、他は invoke される
        byte[] way1 = BuildWayMessage(id: 100L);
        byte[] group = BuildPrimitiveGroup(wayBytes: new[] { way1 });
        byte[] block = BuildPrimitiveBlock(groups: new[] { group });

        byte[] pbf = BuildPbfBytes(
            (BlobKind.Header, BuildMinimalHeader()),
            (BlobKind.Data, block));

        int nodeCount = 0;
        using var ms = new MemoryStream(pbf);
        PbfReader.Read(ms,
            onNode: (_, _) => nodeCount++,
            onWay: null,
            onRelation: null);

        Assert.Equal(0, nodeCount);
        // 例外なく完了
    }

    [Fact]
    public void Read_MultipleDataBlocks_AllProcessed()
    {
        byte[] way1 = BuildWayMessage(id: 1L);
        byte[] way2 = BuildWayMessage(id: 2L);
        byte[] way3 = BuildWayMessage(id: 3L);

        byte[] block1 = BuildPrimitiveBlock(groups: new[] { BuildPrimitiveGroup(wayBytes: new[] { way1 }) });
        byte[] block2 = BuildPrimitiveBlock(groups: new[] { BuildPrimitiveGroup(wayBytes: new[] { way2, way3 }) });

        byte[] pbf = BuildPbfBytes(
            (BlobKind.Header, BuildMinimalHeader()),
            (BlobKind.Data, block1),
            (BlobKind.Data, block2));

        var ids = new List<long>();
        using var ms = new MemoryStream(pbf);
        PbfReader.Read(ms, onWay: (w, _) => ids.Add(w.Id));

        Assert.Equal(new[] { 1L, 2L, 3L }, ids);
    }

    [Fact]
    public void Read_LeaveOpen_StreamStaysOpen()
    {
        byte[] pbf = BuildPbfBytes((BlobKind.Header, BuildMinimalHeader()));
        var ms = new MemoryStream(pbf);

        PbfReader.Read(ms, leaveOpen: true);

        // Dispose 後はアクセス時例外、leaveOpen=true なら可
        Assert.True(ms.CanRead);
        ms.Dispose();
    }

    // ------------- 補助：PBF バイト合成 -------------------------------------

    private enum BlobKind { Header, Data }

    private static byte[] BuildPbfBytes(params (BlobKind kind, byte[] payload)[] blobs)
    {
        using var ms = new MemoryStream();
        Span<byte> size = stackalloc byte[4];
        foreach (var (kind, payload) in blobs)
        {
            string type = kind == BlobKind.Header ? "OSMHeader" : "OSMData";
            byte[] blobBytes = BuildRawBlob(payload);
            byte[] headerBytes = BuildBlobHeader(type, blobBytes.Length);

            BinaryPrimitives.WriteInt32BigEndian(size, headerBytes.Length);
            ms.Write(size);
            ms.Write(headerBytes);
            ms.Write(blobBytes);
        }
        return ms.ToArray();
    }

    private static byte[] BuildRawBlob(byte[] payload)
    {
        // Blob.raw のみ (field 1 length-delimited)
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.LengthDelimited);
        WriteVarint(ms, (ulong)payload.Length);
        ms.Write(payload);
        return ms.ToArray();
    }

    private static byte[] BuildBlobHeader(string type, int dataSize)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.LengthDelimited);
        byte[] typeBytes = Encoding.UTF8.GetBytes(type);
        WriteVarint(ms, (ulong)typeBytes.Length);
        ms.Write(typeBytes);
        WriteTag(ms, 3, WireType.Varint);
        WriteVarint(ms, (ulong)dataSize);
        return ms.ToArray();
    }

    // ------------- 補助：OsmHeader / PrimitiveBlock 合成 --------------------

    private static byte[] BuildMinimalHeader()
        => BuildHeaderPayload("OsmSchema-V0.6", "DenseNodes");

    private static byte[] BuildHeaderPayload(params string[] requiredFeatures)
    {
        using var ms = new MemoryStream();
        foreach (var f in requiredFeatures)
        {
            WriteString(ms, 4, f);
        }
        return ms.ToArray();
    }

    private static byte[] BuildMinimalPrimitiveBlock()
    {
        // StringTable (空でないと無効: 最低 1 要素を入れる)
        return BuildPrimitiveBlock(groups: Array.Empty<byte[]>());
    }

    private static byte[] BuildPrimitiveBlock(byte[][] groups)
    {
        using var ms = new MemoryStream();
        // StringTable (field 1, length-delimited)
        byte[] stringTable = BuildStringTable("");
        WriteTag(ms, 1, WireType.LengthDelimited);
        WriteVarint(ms, (ulong)stringTable.Length);
        ms.Write(stringTable);

        // PrimitiveGroup (field 2, repeated)
        foreach (var group in groups)
        {
            WriteTag(ms, 2, WireType.LengthDelimited);
            WriteVarint(ms, (ulong)group.Length);
            ms.Write(group);
        }

        return ms.ToArray();
    }

    private static byte[] BuildStringTable(params string[] strings)
    {
        using var ms = new MemoryStream();
        foreach (var s in strings)
        {
            byte[] sb = Encoding.UTF8.GetBytes(s);
            WriteTag(ms, 1, WireType.LengthDelimited);
            WriteVarint(ms, (ulong)sb.Length);
            ms.Write(sb);
        }
        return ms.ToArray();
    }

    private static byte[] BuildPrimitiveGroup(
        byte[][]? wayBytes = null,
        byte[][]? relationBytes = null)
    {
        using var ms = new MemoryStream();
        if (wayBytes is not null)
        {
            foreach (var w in wayBytes)
            {
                WriteTag(ms, 3, WireType.LengthDelimited); // ways (field 3)
                WriteVarint(ms, (ulong)w.Length);
                ms.Write(w);
            }
        }
        if (relationBytes is not null)
        {
            foreach (var r in relationBytes)
            {
                WriteTag(ms, 4, WireType.LengthDelimited); // relations (field 4)
                WriteVarint(ms, (ulong)r.Length);
                ms.Write(r);
            }
        }
        return ms.ToArray();
    }

    private static byte[] BuildWayMessage(long id, long[]? refs = null)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint); // id (int64 plain varint)
        WriteVarint(ms, unchecked((ulong)id));

        if (refs is not null && refs.Length > 0)
        {
            WriteTag(ms, 8, WireType.LengthDelimited);
            byte[] packed = PackZigzag(ToDeltas(refs));
            WriteVarint(ms, (ulong)packed.Length);
            ms.Write(packed);
        }
        return ms.ToArray();
    }

    private static byte[] BuildRelationMessage(long id)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, unchecked((ulong)id));
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
        foreach (long v in values) WriteVarint(ms, ZigZagEncode(v));
        return ms.ToArray();
    }

    private static void WriteString(Stream output, int fieldNumber, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteTag(output, fieldNumber, WireType.LengthDelimited);
        WriteVarint(output, (ulong)bytes.Length);
        output.Write(bytes);
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
