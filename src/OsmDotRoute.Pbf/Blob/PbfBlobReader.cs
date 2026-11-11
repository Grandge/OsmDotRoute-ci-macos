using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Pbf.Blob;

/// <summary>
/// OSM PBF の Blob 層を順次読み出すリーダー。
/// 4 バイト BE プレフィックス → BlobHeader (protobuf) → Blob (protobuf) のシーケンスを <see cref="MoveNext"/> で 1 ブロックずつ進める。
/// </summary>
/// <remarks>
/// <para>サポート対象:</para>
/// <list type="bullet">
///   <item>raw (非圧縮)</item>
///   <item>zlib_data (RFC 1950 zlib, <see cref="ZLibStream"/> で解凍)</item>
/// </list>
/// <para>未対応: lzma_data / bzip2_data (deprecated) / lz4_data / zstd_data — 検出時は <see cref="NotSupportedException"/>。</para>
/// <para>PBF 仕様: https://wiki.openstreetmap.org/wiki/PBF_Format</para>
/// </remarks>
internal sealed class PbfBlobReader : IDisposable
{
    /// <summary>BlobHeader の上限サイズ (PBF 仕様: 64 KB)。</summary>
    public const int MaxBlobHeaderSize = 64 * 1024;

    /// <summary>Blob protobuf の上限サイズ (PBF 仕様: 32 MB)。</summary>
    public const int MaxBlobSize = 32 * 1024 * 1024;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly byte[] _sizePrefixBuffer = new byte[4];
    private byte[]? _payloadBuffer;
    private int _payloadLength;
    private PbfBlobType _currentType;
    private bool _disposed;

    public PbfBlobReader(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    /// <summary>直前に読み込んだ Blob の種別。<see cref="MoveNext"/> が true を返した直後のみ有効。</summary>
    public PbfBlobType CurrentType
    {
        get
        {
            ThrowIfDisposed();
            return _currentType;
        }
    }

    /// <summary>
    /// 直前に読み込んだ Blob の解凍後ペイロード。
    /// 次の <see cref="MoveNext"/> 呼出または <see cref="Dispose"/> までの間有効。
    /// </summary>
    public ReadOnlySpan<byte> CurrentPayload
    {
        get
        {
            ThrowIfDisposed();
            if (_payloadBuffer is null) return ReadOnlySpan<byte>.Empty;
            return new ReadOnlySpan<byte>(_payloadBuffer, 0, _payloadLength);
        }
    }

    /// <summary>次の Blob を読み込む。EOF なら false。</summary>
    public bool MoveNext()
    {
        ThrowIfDisposed();

        if (!TryReadBlobHeaderSize(out int blobHeaderSize))
        {
            ReturnPayloadBuffer();
            return false;
        }

        if (blobHeaderSize <= 0 || blobHeaderSize > MaxBlobHeaderSize)
            throw new InvalidDataException(
                $"BlobHeader size {blobHeaderSize} out of range (1..{MaxBlobHeaderSize}).");

        PbfBlobType blobType;
        int dataSize;
        byte[] headerBuffer = ArrayPool<byte>.Shared.Rent(blobHeaderSize);
        try
        {
            ReadExactly(headerBuffer, 0, blobHeaderSize);
            ParseBlobHeader(new ReadOnlySpan<byte>(headerBuffer, 0, blobHeaderSize), out blobType, out dataSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }

        if (dataSize <= 0 || dataSize > MaxBlobSize)
            throw new InvalidDataException(
                $"BlobHeader.datasize {dataSize} out of range (1..{MaxBlobSize}).");

        byte[] blobBuffer = ArrayPool<byte>.Shared.Rent(dataSize);
        try
        {
            ReadExactly(blobBuffer, 0, dataSize);
            ReturnPayloadBuffer();
            DecodeBlobAndDecompress(blobBuffer, dataSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(blobBuffer);
        }

        _currentType = blobType;
        return true;
    }

    private bool TryReadBlobHeaderSize(out int size)
    {
        int totalRead = 0;
        while (totalRead < 4)
        {
            int n = _stream.Read(_sizePrefixBuffer, totalRead, 4 - totalRead);
            if (n == 0)
            {
                if (totalRead == 0)
                {
                    size = 0;
                    return false;
                }
                throw new InvalidDataException(
                    $"Unexpected EOF while reading BlobHeader size prefix (got {totalRead} of 4 bytes).");
            }
            totalRead += n;
        }
        size = BinaryPrimitives.ReadInt32BigEndian(_sizePrefixBuffer);
        return true;
    }

    private void ReadExactly(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int n = _stream.Read(buffer, offset + totalRead, count - totalRead);
            if (n == 0)
                throw new InvalidDataException(
                    $"Unexpected EOF (read {totalRead} of {count} expected bytes).");
            totalRead += n;
        }
    }

    private static void ParseBlobHeader(
        ReadOnlySpan<byte> headerBytes, out PbfBlobType blobType, out int dataSize)
    {
        var reader = new ProtoReader(headerBytes);
        string? typeString = null;
        int parsedDataSize = -1;

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // type (required, string)
                    EnsureWireType(tag, WireType.LengthDelimited, "BlobHeader.type");
                    typeString = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                case 2: // indexdata (optional, bytes) — Phase 2 では未使用、スキップ
                    reader.SkipField(tag.WireType);
                    break;
                case 3: // datasize (required, int32 as varint)
                    EnsureWireType(tag, WireType.Varint, "BlobHeader.datasize");
                    parsedDataSize = checked((int)reader.ReadVarint32());
                    break;
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        if (typeString is null)
            throw new InvalidDataException("BlobHeader is missing required field 'type'.");
        if (parsedDataSize < 0)
            throw new InvalidDataException("BlobHeader is missing required field 'datasize'.");

        dataSize = parsedDataSize;
        blobType = typeString switch
        {
            "OSMHeader" => PbfBlobType.Header,
            "OSMData" => PbfBlobType.Data,
            _ => PbfBlobType.Unknown,
        };
    }

    private void DecodeBlobAndDecompress(byte[] blobBuffer, int blobLength)
    {
        var blobSpan = new ReadOnlySpan<byte>(blobBuffer, 0, blobLength);
        var reader = new ProtoReader(blobSpan);

        int rawSize = -1;
        int rawOffset = -1;
        int rawLength = -1;
        int zlibOffset = -1;
        int zlibLength = -1;
        string? unsupportedCompression = null;

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // raw (optional, bytes)
                    EnsureWireType(tag, WireType.LengthDelimited, "Blob.raw");
                    ReadOnlySpan<byte> rawSpan = reader.ReadLengthDelimited();
                    rawLength = rawSpan.Length;
                    rawOffset = reader.Position - rawLength;
                    break;
                case 2: // raw_size (optional, int32 as varint)
                    EnsureWireType(tag, WireType.Varint, "Blob.raw_size");
                    rawSize = checked((int)reader.ReadVarint32());
                    break;
                case 3: // zlib_data (optional, bytes)
                    EnsureWireType(tag, WireType.LengthDelimited, "Blob.zlib_data");
                    ReadOnlySpan<byte> zlibSpan = reader.ReadLengthDelimited();
                    zlibLength = zlibSpan.Length;
                    zlibOffset = reader.Position - zlibLength;
                    break;
                case 4: // lzma_data
                    unsupportedCompression ??= "lzma";
                    reader.SkipField(tag.WireType);
                    break;
                case 5: // OBSOLETE_bzip2_data (deprecated)
                    unsupportedCompression ??= "bzip2 (deprecated)";
                    reader.SkipField(tag.WireType);
                    break;
                case 6: // lz4_data
                    unsupportedCompression ??= "lz4";
                    reader.SkipField(tag.WireType);
                    break;
                case 7: // zstd_data
                    unsupportedCompression ??= "zstd";
                    reader.SkipField(tag.WireType);
                    break;
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        if (zlibOffset >= 0)
        {
            if (rawSize < 0)
                throw new InvalidDataException("Blob has zlib_data but missing raw_size.");
            if (rawSize == 0 || rawSize > MaxBlobSize)
                throw new InvalidDataException(
                    $"Blob.raw_size {rawSize} out of range (1..{MaxBlobSize}).");

            _payloadBuffer = ArrayPool<byte>.Shared.Rent(rawSize);
            _payloadLength = rawSize;
            DecompressZlib(blobBuffer, zlibOffset, zlibLength, rawSize, _payloadBuffer);
            return;
        }

        if (rawOffset >= 0)
        {
            _payloadBuffer = ArrayPool<byte>.Shared.Rent(rawLength);
            _payloadLength = rawLength;
            Buffer.BlockCopy(blobBuffer, rawOffset, _payloadBuffer, 0, rawLength);
            return;
        }

        if (unsupportedCompression is not null)
            throw new NotSupportedException(
                $"Blob compression '{unsupportedCompression}' is not supported. " +
                "OsmDotRoute.Pbf supports only raw and zlib_data.");

        throw new InvalidDataException("Blob contains no recognized payload field (raw or zlib_data).");
    }

    private static void DecompressZlib(
        byte[] sourceBuffer, int zlibOffset, int zlibLength,
        int expectedRawSize, byte[] destinationBuffer)
    {
        using var input = new MemoryStream(sourceBuffer, zlibOffset, zlibLength, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        int totalRead = 0;
        while (totalRead < expectedRawSize)
        {
            int n = zlib.Read(destinationBuffer, totalRead, expectedRawSize - totalRead);
            if (n == 0)
                throw new InvalidDataException(
                    $"zlib stream ended after {totalRead} bytes, expected raw_size {expectedRawSize}.");
            totalRead += n;
        }
        if (zlib.ReadByte() != -1)
            throw new InvalidDataException(
                $"zlib decompressed more than declared raw_size {expectedRawSize}.");
    }

    private static void EnsureWireType(ProtoTag tag, WireType expected, string fieldName)
    {
        if (tag.WireType != expected)
            throw new InvalidDataException(
                $"{fieldName} expected wire-type {expected} but got {tag.WireType}.");
    }

    private void ReturnPayloadBuffer()
    {
        if (_payloadBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_payloadBuffer);
            _payloadBuffer = null;
            _payloadLength = 0;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PbfBlobReader));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReturnPayloadBuffer();
        if (!_leaveOpen)
            _stream.Dispose();
    }
}
