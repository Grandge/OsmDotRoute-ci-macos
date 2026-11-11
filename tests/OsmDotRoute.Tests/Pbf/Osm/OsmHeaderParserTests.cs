using System;
using System.IO;
using System.Text;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Tests.Pbf.Osm;

public class OsmHeaderParserTests
{
    [Fact]
    public void Parse_EmptyPayload_ReturnsEmptyHeader()
    {
        OsmHeader header = OsmHeaderParser.Parse(ReadOnlySpan<byte>.Empty);

        Assert.Null(header.BoundingBox);
        Assert.Empty(header.RequiredFeatures);
        Assert.Empty(header.OptionalFeatures);
        Assert.Null(header.WritingProgram);
        Assert.Null(header.Source);
    }

    [Fact]
    public void Parse_MinimalRequiredFeatures_ParsesAll()
    {
        using var ms = new MemoryStream();
        WriteString(ms, fieldNumber: 4, "OsmSchema-V0.6");
        WriteString(ms, fieldNumber: 4, "DenseNodes");
        byte[] bytes = ms.ToArray();

        OsmHeader header = OsmHeaderParser.Parse(bytes);

        Assert.Equal(2, header.RequiredFeatures.Count);
        Assert.Equal("OsmSchema-V0.6", header.RequiredFeatures[0]);
        Assert.Equal("DenseNodes", header.RequiredFeatures[1]);
    }

    [Fact]
    public void Parse_BoundingBox_ConvertsNanodegreesToDegrees()
    {
        // 津島市付近: 136.7 / 35.16 / 136.78 / 35.20 を nanodegree (1e9) で
        long leftNano = 136_700_000_000L;     // MinLon
        long rightNano = 136_780_000_000L;    // MaxLon
        long topNano = 35_200_000_000L;       // MaxLat
        long bottomNano = 35_160_000_000L;    // MinLat

        byte[] bboxBytes = BuildBoundingBox(leftNano, rightNano, topNano, bottomNano);
        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, bboxBytes);
        byte[] bytes = ms.ToArray();

        OsmHeader header = OsmHeaderParser.Parse(bytes);

        Assert.NotNull(header.BoundingBox);
        OsmBoundingBox bbox = header.BoundingBox!.Value;
        Assert.Equal(136.7, bbox.MinLon, precision: 9);
        Assert.Equal(136.78, bbox.MaxLon, precision: 9);
        Assert.Equal(35.16, bbox.MinLat, precision: 9);
        Assert.Equal(35.2, bbox.MaxLat, precision: 9);
    }

    [Fact]
    public void Parse_BoundingBox_NegativeCoordinates_Handled()
    {
        // 南半球 / 西半球の例: シドニー付近の負の lon を仮定
        long leftNano = -180_000_000_000L;
        long rightNano = -179_000_000_000L;
        long topNano = -33_000_000_000L;
        long bottomNano = -34_000_000_000L;

        byte[] bboxBytes = BuildBoundingBox(leftNano, rightNano, topNano, bottomNano);
        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, bboxBytes);

        OsmHeader header = OsmHeaderParser.Parse(ms.ToArray());

        OsmBoundingBox bbox = header.BoundingBox!.Value;
        Assert.Equal(-180.0, bbox.MinLon, precision: 9);
        Assert.Equal(-179.0, bbox.MaxLon, precision: 9);
        Assert.Equal(-34.0, bbox.MinLat, precision: 9);
        Assert.Equal(-33.0, bbox.MaxLat, precision: 9);
    }

    [Fact]
    public void Parse_WritingProgramAndSource_ParsedAsStrings()
    {
        using var ms = new MemoryStream();
        WriteString(ms, fieldNumber: 16, "Osmosis 0.48.3");
        WriteString(ms, fieldNumber: 17, "http://download.geofabrik.de/asia/japan-latest.osm.pbf");
        byte[] bytes = ms.ToArray();

        OsmHeader header = OsmHeaderParser.Parse(bytes);

        Assert.Equal("Osmosis 0.48.3", header.WritingProgram);
        Assert.Equal("http://download.geofabrik.de/asia/japan-latest.osm.pbf", header.Source);
    }

    [Fact]
    public void Parse_OptionalFeatures_AllCollected()
    {
        using var ms = new MemoryStream();
        WriteString(ms, fieldNumber: 5, "Has_Metadata");
        WriteString(ms, fieldNumber: 5, "Sort.Type_then_ID");
        byte[] bytes = ms.ToArray();

        OsmHeader header = OsmHeaderParser.Parse(bytes);

        Assert.Equal(new[] { "Has_Metadata", "Sort.Type_then_ID" }, header.OptionalFeatures);
    }

    [Fact]
    public void Parse_MultipleRequiredFeatures_OrderPreserved()
    {
        using var ms = new MemoryStream();
        WriteString(ms, fieldNumber: 4, "B");
        WriteString(ms, fieldNumber: 4, "A");
        WriteString(ms, fieldNumber: 4, "C");
        byte[] bytes = ms.ToArray();

        OsmHeader header = OsmHeaderParser.Parse(bytes);

        Assert.Equal(new[] { "B", "A", "C" }, header.RequiredFeatures);
    }

    [Fact]
    public void Parse_ReplicationFields_Skipped()
    {
        // field 32 (replication_timestamp, int64 varint) と field 33 を書く → 無視される
        using var ms = new MemoryStream();
        WriteString(ms, fieldNumber: 4, "OsmSchema-V0.6");
        WriteTag(ms, fieldNumber: 32, WireType.Varint);
        WriteVarint(ms, 1672531200UL); // 2023-01-01 UTC
        WriteTag(ms, fieldNumber: 33, WireType.Varint);
        WriteVarint(ms, 12345UL);
        byte[] bytes = ms.ToArray();

        OsmHeader header = OsmHeaderParser.Parse(bytes);

        Assert.Single(header.RequiredFeatures);
        Assert.Equal("OsmSchema-V0.6", header.RequiredFeatures[0]);
    }

    [Fact]
    public void Parse_FullHeader_AllFieldsCorrect()
    {
        byte[] bboxBytes = BuildBoundingBox(
            leftNano: 136_700_000_000L,
            rightNano: 136_780_000_000L,
            topNano: 35_200_000_000L,
            bottomNano: 35_160_000_000L);

        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, bboxBytes);
        WriteString(ms, fieldNumber: 4, "OsmSchema-V0.6");
        WriteString(ms, fieldNumber: 4, "DenseNodes");
        WriteString(ms, fieldNumber: 5, "Has_Metadata");
        WriteString(ms, fieldNumber: 16, "OsmDotRoute.Extractor 0.2.0");
        WriteString(ms, fieldNumber: 17, "test://source");

        OsmHeader header = OsmHeaderParser.Parse(ms.ToArray());

        Assert.NotNull(header.BoundingBox);
        Assert.Equal(2, header.RequiredFeatures.Count);
        Assert.Single(header.OptionalFeatures);
        Assert.Equal("OsmDotRoute.Extractor 0.2.0", header.WritingProgram);
        Assert.Equal("test://source", header.Source);
    }

    [Fact]
    public void Parse_BoundingBoxMissingLeft_Throws()
    {
        // bbox に right/top/bottom のみ (left 欠落)
        using var bboxMs = new MemoryStream();
        WriteTag(bboxMs, fieldNumber: 2, WireType.Varint);
        WriteVarint(bboxMs, ZigZagEncode(0));
        WriteTag(bboxMs, fieldNumber: 3, WireType.Varint);
        WriteVarint(bboxMs, ZigZagEncode(0));
        WriteTag(bboxMs, fieldNumber: 4, WireType.Varint);
        WriteVarint(bboxMs, ZigZagEncode(0));
        byte[] bboxBytes = bboxMs.ToArray();

        using var ms = new MemoryStream();
        WriteLengthDelimited(ms, fieldNumber: 1, bboxBytes);

        Assert.Throws<InvalidDataException>(() => OsmHeaderParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_BBoxWrongWireType_Throws()
    {
        // field 1 (bbox) を Varint で送る → wire type 不一致
        using var ms = new MemoryStream();
        WriteTag(ms, fieldNumber: 1, WireType.Varint);
        WriteVarint(ms, 42);

        Assert.Throws<InvalidDataException>(() => OsmHeaderParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_RequiredFeatureWrongWireType_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, fieldNumber: 4, WireType.Varint);
        WriteVarint(ms, 99);

        Assert.Throws<InvalidDataException>(() => OsmHeaderParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void EnsureSupported_KnownFeatures_DoesNotThrow()
    {
        var header = new OsmHeader(
            BoundingBox: null,
            RequiredFeatures: new[] { "OsmSchema-V0.6", "DenseNodes" },
            OptionalFeatures: Array.Empty<string>(),
            WritingProgram: null,
            Source: null);

        OsmHeaderParser.EnsureSupported(header); // 例外が出ないこと
    }

    [Fact]
    public void EnsureSupported_UnknownRequiredFeature_Throws()
    {
        var header = new OsmHeader(
            BoundingBox: null,
            RequiredFeatures: new[] { "OsmSchema-V0.6", "HistoricalInformation" },
            OptionalFeatures: Array.Empty<string>(),
            WritingProgram: null,
            Source: null);

        NotSupportedException ex = Assert.Throws<NotSupportedException>(
            () => OsmHeaderParser.EnsureSupported(header));
        Assert.Contains("HistoricalInformation", ex.Message);
    }

    [Fact]
    public void EnsureSupported_OptionalFeaturesNeverChecked()
    {
        var header = new OsmHeader(
            BoundingBox: null,
            RequiredFeatures: new[] { "OsmSchema-V0.6" },
            OptionalFeatures: new[] { "TotallyMadeUpFeature" },
            WritingProgram: null,
            Source: null);

        OsmHeaderParser.EnsureSupported(header);
    }

    [Fact]
    public void EnsureSupported_NullHeader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OsmHeaderParser.EnsureSupported(null!));
    }

    [Fact]
    public void EnsureSupported_EmptyRequiredFeatures_DoesNotThrow()
    {
        // PBF 仕様上は required_features が空でも valid (ただし実際の OSM PBF は OsmSchema-V0.6 を必ず含む)
        var header = new OsmHeader(
            BoundingBox: null,
            RequiredFeatures: Array.Empty<string>(),
            OptionalFeatures: Array.Empty<string>(),
            WritingProgram: null,
            Source: null);

        OsmHeaderParser.EnsureSupported(header);
    }

    [Fact]
    public void EnsureSupported_CaseSensitive()
    {
        // SupportedRequiredFeatures は StringComparer.Ordinal で比較するので小文字違いは reject される
        var header = new OsmHeader(
            BoundingBox: null,
            RequiredFeatures: new[] { "osmschema-v0.6" }, // 小文字
            OptionalFeatures: Array.Empty<string>(),
            WritingProgram: null,
            Source: null);

        Assert.Throws<NotSupportedException>(() => OsmHeaderParser.EnsureSupported(header));
    }

    // --- 補助メソッド --------------------------------------------------------

    private static byte[] BuildBoundingBox(long leftNano, long rightNano, long topNano, long bottomNano)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, fieldNumber: 1, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(leftNano));
        WriteTag(ms, fieldNumber: 2, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(rightNano));
        WriteTag(ms, fieldNumber: 3, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(topNano));
        WriteTag(ms, fieldNumber: 4, WireType.Varint);
        WriteVarint(ms, ZigZagEncode(bottomNano));
        return ms.ToArray();
    }

    private static void WriteString(Stream output, int fieldNumber, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteLengthDelimited(output, fieldNumber, bytes);
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

    private static ulong ZigZagEncode(long value)
    {
        return (ulong)((value << 1) ^ (value >> 63));
    }
}
