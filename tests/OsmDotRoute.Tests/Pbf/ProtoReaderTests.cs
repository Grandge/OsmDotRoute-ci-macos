using System;
using System.IO;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Tests.Pbf;

public class ProtoReaderTests
{
    [Fact]
    public void ReadVarint64_SingleByte()
    {
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x00 };
        var r = new ProtoReader(buf);
        Assert.Equal(0UL, r.ReadVarint64());
        Assert.False(r.HasMore);
    }

    [Fact]
    public void ReadVarint64_TwoBytes()
    {
        // 300 = 0xAC 0x02 (binary: 10101100 00000010)
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0xAC, 0x02 };
        var r = new ProtoReader(buf);
        Assert.Equal(300UL, r.ReadVarint64());
    }

    [Fact]
    public void ReadVarint64_MaxValue()
    {
        // ulong.MaxValue = 10 bytes of varint
        ReadOnlySpan<byte> buf = stackalloc byte[]
        {
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01,
        };
        var r = new ProtoReader(buf);
        Assert.Equal(ulong.MaxValue, r.ReadVarint64());
    }

    [Fact]
    public void ReadVarint64_UnexpectedEndOfBuffer_Throws()
    {
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x80 }; // 継続ビット立ったまま終わる
        var r = new ProtoReader(buf);
        bool thrown = false;
        try { _ = r.ReadVarint64(); }
        catch (InvalidDataException) { thrown = true; }
        Assert.True(thrown);
    }

    [Fact]
    public void ReadVarint32_OverflowingValue_Throws()
    {
        // 5 GB varint (uint32 範囲超え)
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x1F };
        var r = new ProtoReader(buf);
        bool thrown = false;
        try { _ = r.ReadVarint32(); }
        catch (InvalidDataException) { thrown = true; }
        Assert.True(thrown);
    }

    [Theory]
    [InlineData(0u, 0)]
    [InlineData(1u, -1)]
    [InlineData(2u, 1)]
    [InlineData(3u, -2)]
    [InlineData(4u, 2)]
    public void ReadZigzag32(uint encoded, int expected)
    {
        // Encode the value directly as a single-byte varint when it fits
        if (encoded > 127)
            throw new InvalidOperationException("Test input must fit in 1 byte for simplicity.");
        Span<byte> buf = stackalloc byte[1];
        buf[0] = (byte)encoded;
        var r = new ProtoReader(buf);
        Assert.Equal(expected, r.ReadZigzag32());
    }

    [Theory]
    [InlineData(0UL, 0L)]
    [InlineData(1UL, -1L)]
    [InlineData(2UL, 1L)]
    [InlineData(0x7FUL, -64L)]
    [InlineData(0x80UL, 64L)]
    public void ReadZigzag64(ulong encoded, long expected)
    {
        Span<byte> buf = stackalloc byte[10];
        int len = WriteVarint(buf, encoded);
        var r = new ProtoReader(buf.Slice(0, len));
        Assert.Equal(expected, r.ReadZigzag64());
    }

    [Fact]
    public void ReadTag_ValidFieldAndWireType()
    {
        // field_number = 1, wire_type = LengthDelimited (2): tag = (1 << 3) | 2 = 10 = 0x0A
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x0A };
        var r = new ProtoReader(buf);
        var tag = r.ReadTag();
        Assert.Equal(1, tag.FieldNumber);
        Assert.Equal(WireType.LengthDelimited, tag.WireType);
        Assert.False(tag.IsEnd);
    }

    [Fact]
    public void ReadTag_BufferEnd_ReturnsIsEnd()
    {
        ReadOnlySpan<byte> buf = ReadOnlySpan<byte>.Empty;
        var r = new ProtoReader(buf);
        Assert.True(r.ReadTag().IsEnd);
    }

    [Fact]
    public void ReadTag_FieldNumberZero_Throws()
    {
        // tag = 0 (field_number=0, wire_type=Varint) は不正
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x00 };
        var r = new ProtoReader(buf);
        bool thrown = false;
        try { _ = r.ReadTag(); }
        catch (InvalidDataException) { thrown = true; }
        Assert.True(thrown);
    }

    [Fact]
    public void ReadFixed32_LittleEndian()
    {
        // 0x12345678 little-endian = 78 56 34 12
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x78, 0x56, 0x34, 0x12 };
        var r = new ProtoReader(buf);
        Assert.Equal(0x12345678u, r.ReadFixed32());
        Assert.False(r.HasMore);
    }

    [Fact]
    public void ReadFixed64_LittleEndian()
    {
        ReadOnlySpan<byte> buf = stackalloc byte[]
        {
            0xEF, 0xCD, 0xAB, 0x90, 0x78, 0x56, 0x34, 0x12,
        };
        var r = new ProtoReader(buf);
        Assert.Equal(0x1234567890ABCDEFUL, r.ReadFixed64());
    }

    [Fact]
    public void ReadLengthDelimited_ReturnsSlice()
    {
        // length = 3, then 3 bytes
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x03, 0xAA, 0xBB, 0xCC };
        var r = new ProtoReader(buf);
        var slice = r.ReadLengthDelimited();
        Assert.Equal(3, slice.Length);
        Assert.Equal(0xAA, slice[0]);
        Assert.Equal(0xBB, slice[1]);
        Assert.Equal(0xCC, slice[2]);
        Assert.False(r.HasMore);
    }

    [Fact]
    public void ReadLengthDelimited_LengthExceedsBuffer_Throws()
    {
        // length = 10 だが実体は 1 バイトしかない
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x0A, 0xAA };
        var r = new ProtoReader(buf);
        bool thrown = false;
        try { _ = r.ReadLengthDelimited(); }
        catch (InvalidDataException) { thrown = true; }
        Assert.True(thrown);
    }

    [Fact]
    public void SkipField_Varint()
    {
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0xAC, 0x02, 0x55 };
        var r = new ProtoReader(buf);
        r.SkipField(WireType.Varint);
        Assert.Equal(2, r.Position);
        Assert.Equal(0x55u, r.ReadVarint32());
    }

    [Fact]
    public void SkipField_Fixed32()
    {
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x01, 0x02, 0x03, 0x04, 0x55 };
        var r = new ProtoReader(buf);
        r.SkipField(WireType.Fixed32);
        Assert.Equal(4, r.Position);
        Assert.Equal(0x55u, r.ReadVarint32());
    }

    [Fact]
    public void SkipField_LengthDelimited()
    {
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x02, 0xAA, 0xBB, 0x55 };
        var r = new ProtoReader(buf);
        r.SkipField(WireType.LengthDelimited);
        Assert.Equal(3, r.Position);
        Assert.Equal(0x55u, r.ReadVarint32());
    }

    [Fact]
    public void SkipField_StartGroup_Throws()
    {
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x00 };
        var r = new ProtoReader(buf);
        bool thrown = false;
        try { r.SkipField(WireType.StartGroup); }
        catch (NotSupportedException) { thrown = true; }
        Assert.True(thrown);
    }

    [Fact]
    public void TagFollowedByValue_RealisticSequence()
    {
        // field 1, varint, value=42; field 2, length-delimited, "hi"
        // tag1=0x08, value=0x2A; tag2=0x12, length=0x02, 'h'=0x68, 'i'=0x69
        ReadOnlySpan<byte> buf = stackalloc byte[]
        {
            0x08, 0x2A,
            0x12, 0x02, 0x68, 0x69,
        };
        var r = new ProtoReader(buf);

        var t1 = r.ReadTag();
        Assert.Equal(1, t1.FieldNumber);
        Assert.Equal(WireType.Varint, t1.WireType);
        Assert.Equal(42u, r.ReadVarint32());

        var t2 = r.ReadTag();
        Assert.Equal(2, t2.FieldNumber);
        Assert.Equal(WireType.LengthDelimited, t2.WireType);
        var slice = r.ReadLengthDelimited();
        Assert.Equal(2, slice.Length);
        Assert.Equal((byte)'h', slice[0]);
        Assert.Equal((byte)'i', slice[1]);

        Assert.True(r.ReadTag().IsEnd);
    }

    /// <summary>varint を素朴に書き込むテスト用ヘルパー。</summary>
    private static int WriteVarint(Span<byte> dest, ulong value)
    {
        int pos = 0;
        while (value >= 0x80)
        {
            dest[pos++] = (byte)(value | 0x80);
            value >>= 7;
        }
        dest[pos++] = (byte)value;
        return pos;
    }
}
