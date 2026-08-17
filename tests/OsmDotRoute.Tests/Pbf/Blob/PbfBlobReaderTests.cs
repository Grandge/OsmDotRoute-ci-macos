using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using OsmDotRoute.Pbf.Blob;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Tests.Pbf.Blob;

public class PbfBlobReaderTests
{
    [Fact]
    public void MoveNext_OnEmptyStream_ReturnsFalse()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        using var reader = new PbfBlobReader(ms);
        Assert.False(reader.MoveNext());
    }

    [Fact]
    public void MoveNext_SingleRawHeader_ReturnsHeaderWithPayload()
    {
        byte[] payload = Encoding.UTF8.GetBytes("HEADER-PAYLOAD");
        byte[] bytes = BuildPbf((PbfBlobType.Header, payload, Compression.Raw));
        using var ms = new MemoryStream(bytes);
        using var reader = new PbfBlobReader(ms);

        Assert.True(reader.MoveNext());
        Assert.Equal(PbfBlobType.Header, reader.CurrentType);
        Assert.Equal(payload, reader.CurrentPayload.ToArray());
        Assert.False(reader.MoveNext());
    }

    [Fact]
    public void MoveNext_SingleZlibData_DecompressesCorrectly()
    {
        byte[] payload = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("OSM-DATA-", 100)));
        byte[] bytes = BuildPbf((PbfBlobType.Data, payload, Compression.Zlib));
        using var ms = new MemoryStream(bytes);
        using var reader = new PbfBlobReader(ms);

        Assert.True(reader.MoveNext());
        Assert.Equal(PbfBlobType.Data, reader.CurrentType);
        Assert.Equal(payload, reader.CurrentPayload.ToArray());
    }

    [Fact]
    public void MoveNext_MultipleBlobs_IteratesInOrder()
    {
        byte[] headerPayload = Encoding.UTF8.GetBytes("H");
        byte[] data1 = Encoding.UTF8.GetBytes("D1-data-here");
        byte[] data2 = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("D2-", 200)));

        byte[] bytes = BuildPbf(
            (PbfBlobType.Header, headerPayload, Compression.Raw),
            (PbfBlobType.Data, data1, Compression.Zlib),
            (PbfBlobType.Data, data2, Compression.Zlib));

        using var ms = new MemoryStream(bytes);
        using var reader = new PbfBlobReader(ms);

        Assert.True(reader.MoveNext());
        Assert.Equal(PbfBlobType.Header, reader.CurrentType);
        Assert.Equal(headerPayload, reader.CurrentPayload.ToArray());

        Assert.True(reader.MoveNext());
        Assert.Equal(PbfBlobType.Data, reader.CurrentType);
        Assert.Equal(data1, reader.CurrentPayload.ToArray());

        Assert.True(reader.MoveNext());
        Assert.Equal(PbfBlobType.Data, reader.CurrentType);
        Assert.Equal(data2, reader.CurrentPayload.ToArray());

        Assert.False(reader.MoveNext());
        Assert.False(reader.MoveNext()); // 2 回目以降も false
    }

    [Fact]
    public void MoveNext_UnknownBlobTypeString_ReturnsUnknown()
    {
        byte[] payload = Encoding.UTF8.GetBytes("custom");
        byte[] bytes = BuildPbfWithCustomType("FutureType", payload, Compression.Raw);
        using var ms = new MemoryStream(bytes);
        using var reader = new PbfBlobReader(ms);

        Assert.True(reader.MoveNext());
        Assert.Equal(PbfBlobType.Unknown, reader.CurrentType);
        Assert.Equal(payload, reader.CurrentPayload.ToArray());
    }

    [Fact]
    public void MoveNext_TruncatedSizePrefix_Throws()
    {
        using var ms = new MemoryStream(new byte[] { 0x00, 0x00 });
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_TruncatedBlobHeader_Throws()
    {
        byte[] payload = Encoding.UTF8.GetBytes("data");
        byte[] full = BuildPbf((PbfBlobType.Data, payload, Compression.Raw));
        byte[] truncated = full.AsSpan(0, 4 + 2).ToArray(); // size prefix + 2 bytes of header

        using var ms = new MemoryStream(truncated);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_TruncatedBlobBody_Throws()
    {
        byte[] payload = Encoding.UTF8.GetBytes("payload-data-bytes");
        byte[] full = BuildPbf((PbfBlobType.Data, payload, Compression.Raw));
        byte[] truncated = full.AsSpan(0, full.Length - 5).ToArray();

        using var ms = new MemoryStream(truncated);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_BlobHeaderSizeTooLarge_Throws()
    {
        // 4-byte BE size = 100 MB (over 64 KB cap)
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 100 * 1024 * 1024);
        using var ms = new MemoryStream(bytes);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_BlobHeaderSizeZero_Throws()
    {
        byte[] bytes = new byte[4]; // すべて 0 → size = 0
        using var ms = new MemoryStream(bytes);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_BlobHeaderSizeNegative_Throws()
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, -1);
        using var ms = new MemoryStream(bytes);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_BlobHeaderMissingType_Throws()
    {
        byte[] payload = Encoding.UTF8.GetBytes("x");
        byte[] blobBytes = BuildBlobRaw(payload);

        // BlobHeader: datasize only (no type)
        using var headerMs = new MemoryStream();
        WriteTag(headerMs, fieldNumber: 3, WireType.Varint);
        WriteVarint(headerMs, (ulong)blobBytes.Length);
        byte[] headerBytes = headerMs.ToArray();

        byte[] full = AssembleBlock(headerBytes, blobBytes);
        using var ms = new MemoryStream(full);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_BlobHeaderMissingDatasize_Throws()
    {
        // BlobHeader: type only (no datasize)
        using var headerMs = new MemoryStream();
        WriteTag(headerMs, fieldNumber: 1, WireType.LengthDelimited);
        byte[] typeBytes = Encoding.UTF8.GetBytes("OSMHeader");
        WriteVarint(headerMs, (ulong)typeBytes.Length);
        headerMs.Write(typeBytes);
        byte[] headerBytes = headerMs.ToArray();

        // Size prefix + header (no blob follows; reader should fail before reading blob)
        using var ms = new MemoryStream();
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(size, headerBytes.Length);
        ms.Write(size);
        ms.Write(headerBytes);
        ms.Position = 0;

        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_BlobWithZlibButNoRawSize_Throws()
    {
        byte[] payload = Encoding.UTF8.GetBytes("zlib-source");
        byte[] zlibBytes = ZlibCompress(payload);

        // Blob: zlib_data only (no raw_size)
        using var blobMs = new MemoryStream();
        WriteTag(blobMs, fieldNumber: 3, WireType.LengthDelimited);
        WriteVarint(blobMs, (ulong)zlibBytes.Length);
        blobMs.Write(zlibBytes);
        byte[] blobBytes = blobMs.ToArray();

        byte[] headerBytes = BuildBlobHeader("OSMData", blobBytes.Length);
        byte[] full = AssembleBlock(headerBytes, blobBytes);

        using var ms = new MemoryStream(full);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_ZlibRawSizeSmallerThanActual_Throws()
    {
        byte[] payload = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("xy", 50))); // 100 bytes
        byte[] zlibBytes = ZlibCompress(payload);

        // Blob: raw_size declared as 50 (half of actual 100)
        using var blobMs = new MemoryStream();
        WriteTag(blobMs, fieldNumber: 2, WireType.Varint);
        WriteVarint(blobMs, 50);
        WriteTag(blobMs, fieldNumber: 3, WireType.LengthDelimited);
        WriteVarint(blobMs, (ulong)zlibBytes.Length);
        blobMs.Write(zlibBytes);
        byte[] blobBytes = blobMs.ToArray();

        byte[] headerBytes = BuildBlobHeader("OSMData", blobBytes.Length);
        byte[] full = AssembleBlock(headerBytes, blobBytes);

        using var ms = new MemoryStream(full);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_ZlibRawSizeLargerThanActual_Throws()
    {
        byte[] payload = Encoding.UTF8.GetBytes("short"); // 5 bytes
        byte[] zlibBytes = ZlibCompress(payload);

        using var blobMs = new MemoryStream();
        WriteTag(blobMs, fieldNumber: 2, WireType.Varint);
        WriteVarint(blobMs, 999); // 過大な raw_size
        WriteTag(blobMs, fieldNumber: 3, WireType.LengthDelimited);
        WriteVarint(blobMs, (ulong)zlibBytes.Length);
        blobMs.Write(zlibBytes);
        byte[] blobBytes = blobMs.ToArray();

        byte[] headerBytes = BuildBlobHeader("OSMData", blobBytes.Length);
        byte[] full = AssembleBlock(headerBytes, blobBytes);

        using var ms = new MemoryStream(full);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void MoveNext_LzmaCompression_ThrowsNotSupported()
    {
        AssertUnsupportedCompression(fieldNumber: 4);
    }

    [Fact]
    public void MoveNext_Lz4Compression_ThrowsNotSupported()
    {
        AssertUnsupportedCompression(fieldNumber: 6);
    }

    [Fact]
    public void MoveNext_ZstdCompression_ThrowsNotSupported()
    {
        AssertUnsupportedCompression(fieldNumber: 7);
    }

    [Fact]
    public void MoveNext_BlobMissingAllPayload_Throws()
    {
        // Blob protobuf with no fields set
        byte[] blobBytes = Array.Empty<byte>();
        byte[] headerBytes = BuildBlobHeader("OSMData", blobBytes.Length);
        byte[] full = AssembleBlock(headerBytes, blobBytes);

        using var ms = new MemoryStream(full);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<InvalidDataException>(() => reader.MoveNext());
    }

    [Fact]
    public void Dispose_WithoutIterating_DoesNotThrow()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        var reader = new PbfBlobReader(ms);
        reader.Dispose();
        reader.Dispose(); // 二重 Dispose も OK
    }

    [Fact]
    public void CurrentType_AfterDispose_Throws()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        var reader = new PbfBlobReader(ms);
        reader.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = reader.CurrentType);
    }

    [Fact]
    public void Constructor_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PbfBlobReader(null!));
    }

    [Fact]
    public void Constructor_WriteOnlyStream_Throws()
    {
        using var stream = new WriteOnlyStream();
        Assert.Throws<ArgumentException>(() => new PbfBlobReader(stream));
    }

    [Fact]
    public void CurrentPayload_IsRecycledOnNextMoveNext()
    {
        byte[] p1 = Encoding.UTF8.GetBytes("first");
        byte[] p2 = Encoding.UTF8.GetBytes("second");
        byte[] bytes = BuildPbf(
            (PbfBlobType.Data, p1, Compression.Raw),
            (PbfBlobType.Data, p2, Compression.Raw));

        using var ms = new MemoryStream(bytes);
        using var reader = new PbfBlobReader(ms);

        Assert.True(reader.MoveNext());
        Assert.Equal("first", Encoding.UTF8.GetString(reader.CurrentPayload));

        Assert.True(reader.MoveNext());
        Assert.Equal("second", Encoding.UTF8.GetString(reader.CurrentPayload));
    }

    // --- 補助メソッド --------------------------------------------------------

    private enum Compression { Raw, Zlib }

    private static byte[] BuildPbf(params (PbfBlobType type, byte[] payload, Compression compression)[] blobs)
    {
        using var ms = new MemoryStream();
        foreach (var (type, payload, compression) in blobs)
        {
            string typeString = type switch
            {
                PbfBlobType.Header => "OSMHeader",
                PbfBlobType.Data => "OSMData",
                _ => "Unknown",
            };
            byte[] blobBytes = compression == Compression.Zlib
                ? BuildBlobZlib(payload)
                : BuildBlobRaw(payload);
            byte[] headerBytes = BuildBlobHeader(typeString, blobBytes.Length);
            byte[] block = AssembleBlock(headerBytes, blobBytes);
            ms.Write(block);
        }
        return ms.ToArray();
    }

    private static byte[] BuildPbfWithCustomType(string type, byte[] payload, Compression compression)
    {
        byte[] blobBytes = compression == Compression.Zlib
            ? BuildBlobZlib(payload)
            : BuildBlobRaw(payload);
        byte[] headerBytes = BuildBlobHeader(type, blobBytes.Length);
        return AssembleBlock(headerBytes, blobBytes);
    }

    private static byte[] AssembleBlock(byte[] headerBytes, byte[] blobBytes)
    {
        using var ms = new MemoryStream();
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(size, headerBytes.Length);
        ms.Write(size);
        ms.Write(headerBytes);
        ms.Write(blobBytes);
        return ms.ToArray();
    }

    private static byte[] BuildBlobHeader(string type, int dataSize)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, fieldNumber: 1, WireType.LengthDelimited);
        byte[] typeBytes = Encoding.UTF8.GetBytes(type);
        WriteVarint(ms, (ulong)typeBytes.Length);
        ms.Write(typeBytes);

        WriteTag(ms, fieldNumber: 3, WireType.Varint);
        WriteVarint(ms, (ulong)dataSize);
        return ms.ToArray();
    }

    private static byte[] BuildBlobRaw(byte[] payload)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, fieldNumber: 1, WireType.LengthDelimited);
        WriteVarint(ms, (ulong)payload.Length);
        ms.Write(payload);
        return ms.ToArray();
    }

    private static byte[] BuildBlobZlib(byte[] payload)
    {
        byte[] compressed = ZlibCompress(payload);
        using var ms = new MemoryStream();
        WriteTag(ms, fieldNumber: 2, WireType.Varint);
        WriteVarint(ms, (ulong)payload.Length);
        WriteTag(ms, fieldNumber: 3, WireType.LengthDelimited);
        WriteVarint(ms, (ulong)compressed.Length);
        ms.Write(compressed);
        return ms.ToArray();
    }

    private static byte[] ZlibCompress(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(payload, 0, payload.Length);
        }
        return output.ToArray();
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

    private void AssertUnsupportedCompression(int fieldNumber)
    {
        // ダミー圧縮データを fieldNumber に書き込み
        using var blobMs = new MemoryStream();
        WriteTag(blobMs, fieldNumber: 2, WireType.Varint);
        WriteVarint(blobMs, 10);
        WriteTag(blobMs, fieldNumber: fieldNumber, WireType.LengthDelimited);
        byte[] dummy = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        WriteVarint(blobMs, (ulong)dummy.Length);
        blobMs.Write(dummy);
        byte[] blobBytes = blobMs.ToArray();

        byte[] headerBytes = BuildBlobHeader("OSMData", blobBytes.Length);
        byte[] full = AssembleBlock(headerBytes, blobBytes);

        using var ms = new MemoryStream(full);
        using var reader = new PbfBlobReader(ms);
        Assert.Throws<NotSupportedException>(() => reader.MoveNext());
    }

    private sealed class WriteOnlyStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }
}
