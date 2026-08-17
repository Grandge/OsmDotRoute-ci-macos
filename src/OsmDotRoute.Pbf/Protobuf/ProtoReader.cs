using System;
using System.Buffers.Binary;
using System.IO;

namespace OsmDotRoute.Pbf.Protobuf;

/// <summary>
/// protobuf ワイヤ形式デコーダー (System.* のみ、ゼロアロケート)。
/// バッファに対するカーソルとして動作し、各種プリミティブ読込で位置を前進させる。
/// </summary>
/// <remarks>
/// <para>本実装は OsmDotRoute.Pbf が必要とする範囲に限定:</para>
/// <list type="bullet">
///   <item>varint (uint32/uint64)</item>
///   <item>zigzag (sint32/sint64)</item>
///   <item>length-delimited (string/bytes/embedded message/packed repeated)</item>
///   <item>fixed32/fixed64 (リトルエンディアン)</item>
///   <item>start_group/end_group は廃止扱い (PBF では使われない)</item>
/// </list>
/// <para>参考: https://protobuf.dev/programming-guides/encoding/</para>
/// </remarks>
internal ref struct ProtoReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public ProtoReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    /// <summary>バッファ全体の長さ。</summary>
    public int Length => _buffer.Length;

    /// <summary>現在位置 (バイトオフセット)。</summary>
    public int Position => _position;

    /// <summary>未読バイトが残っているか。</summary>
    public bool HasMore => _position < _buffer.Length;

    /// <summary>未読の残りバイト数。</summary>
    public int Remaining => _buffer.Length - _position;

    /// <summary>
    /// 次のタグ (field_number + wire_type) を読む。バッファ末尾なら <see cref="ProtoTag.IsEnd"/> が true。
    /// </summary>
    public ProtoTag ReadTag()
    {
        if (!HasMore) return default;
        uint tag = ReadVarint32();
        int fieldNumber = checked((int)(tag >> 3));
        if (fieldNumber == 0)
            throw new InvalidDataException("Invalid protobuf tag: field_number must be >= 1.");
        return new ProtoTag(fieldNumber, (WireType)(tag & 0x7));
    }

    /// <summary>varint を 64bit 符号なし整数として読む。最大 10 バイト。</summary>
    public ulong ReadVarint64()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            if (_position >= _buffer.Length)
                throw new InvalidDataException("Unexpected end of buffer while reading varint.");
            if (shift >= 70)
                throw new InvalidDataException("Varint exceeds 10 bytes (malformed).");
            byte b = _buffer[_position++];
            result |= ((ulong)(b & 0x7F)) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
    }

    /// <summary>varint を 32bit 符号なし整数として読む。値が 32bit を超えたら例外。</summary>
    public uint ReadVarint32()
    {
        ulong v = ReadVarint64();
        if (v > uint.MaxValue)
            throw new InvalidDataException($"Varint value {v} exceeds uint32 range.");
        return (uint)v;
    }

    /// <summary>zigzag エンコードされた sint32 を読む。</summary>
    public int ReadZigzag32()
    {
        uint n = ReadVarint32();
        return (int)(n >> 1) ^ -(int)(n & 1);
    }

    /// <summary>zigzag エンコードされた sint64 を読む。</summary>
    public long ReadZigzag64()
    {
        ulong n = ReadVarint64();
        return (long)(n >> 1) ^ -((long)n & 1L);
    }

    /// <summary>fixed32 (リトルエンディアン uint) を読む。</summary>
    public uint ReadFixed32()
    {
        EnsureRemaining(4);
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_position, 4));
        _position += 4;
        return v;
    }

    /// <summary>fixed64 (リトルエンディアン ulong) を読む。</summary>
    public ulong ReadFixed64()
    {
        EnsureRemaining(8);
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Slice(_position, 8));
        _position += 8;
        return v;
    }

    /// <summary>length-delimited フィールドの中身を <see cref="ReadOnlySpan{T}"/> として返す (バッファのスライス、コピーなし)。</summary>
    public ReadOnlySpan<byte> ReadLengthDelimited()
    {
        uint length = ReadVarint32();
        if (length > int.MaxValue)
            throw new InvalidDataException($"length-delimited size {length} exceeds Int32.MaxValue.");
        int len = (int)length;
        EnsureRemaining(len);
        ReadOnlySpan<byte> slice = _buffer.Slice(_position, len);
        _position += len;
        return slice;
    }

    /// <summary>現在のフィールドをスキップする (タグの wire_type に応じて適切なバイト数を進める)。</summary>
    public void SkipField(WireType wireType)
    {
        switch (wireType)
        {
            case WireType.Varint:
                _ = ReadVarint64();
                break;
            case WireType.Fixed64:
                EnsureRemaining(8);
                _position += 8;
                break;
            case WireType.LengthDelimited:
                _ = ReadLengthDelimited();
                break;
            case WireType.Fixed32:
                EnsureRemaining(4);
                _position += 4;
                break;
            case WireType.StartGroup:
            case WireType.EndGroup:
                throw new NotSupportedException("protobuf group wire-types are deprecated and not supported.");
            default:
                throw new InvalidDataException($"Unknown wire-type: {(int)wireType}");
        }
    }

    private void EnsureRemaining(int needed)
    {
        if (_buffer.Length - _position < needed)
            throw new InvalidDataException($"Unexpected end of buffer (need {needed} bytes, have {Remaining}).");
    }
}
