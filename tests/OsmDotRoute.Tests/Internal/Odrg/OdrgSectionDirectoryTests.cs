using System.Buffers.Binary;
using System.IO;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Tests.TestData;

namespace OsmDotRoute.Tests.Internal.Odrg;

/// <summary>
/// Phase 3 ステップ 3A.1 — <see cref="OdrgSectionDirectory"/> パーステスト。
/// </summary>
/// <remarks>
/// 正常ケースは <see cref="OdrgReader.Parse"/> を参照真値として field-by-field 一致を確認する。
/// 異常ケースは合成 HEADER バイト列で個別の検証パスを叩く。
/// </remarks>
public sealed class OdrgSectionDirectoryTests
{
    [Fact]
    public void Parse_TsushimaOdrg_MatchesOdrgReaderHeaderAndSections()
    {
        if (!File.Exists(TestPaths.TsushimaOdrg))
        {
            Assert.Fail($"リポジトリ同梱の津島.odrg が見つかりません: {TestPaths.TsushimaOdrg}");
        }

        var bytes = File.ReadAllBytes(TestPaths.TsushimaOdrg);
        var expected = OdrgReader.Parse(bytes);
        var actual = OdrgSectionDirectory.Parse(bytes);

        // Header field-by-field
        Assert.Equal(expected.Header.VersionMajor, actual.Header.VersionMajor);
        Assert.Equal(expected.Header.VersionMinor, actual.Header.VersionMinor);
        Assert.Equal(expected.Header.Flags, actual.Header.Flags);
        Assert.Equal(expected.Header.VertexCount, actual.Header.VertexCount);
        Assert.Equal(expected.Header.EdgeCount, actual.Header.EdgeCount);
        Assert.Equal(expected.Header.Bbox.MinLon, actual.Header.Bbox.MinLon);
        Assert.Equal(expected.Header.Bbox.MinLat, actual.Header.Bbox.MinLat);
        Assert.Equal(expected.Header.Bbox.MaxLon, actual.Header.Bbox.MaxLon);
        Assert.Equal(expected.Header.Bbox.MaxLat, actual.Header.Bbox.MaxLat);
        Assert.Equal(expected.Header.ProfileCount, actual.Header.ProfileCount);
        Assert.Equal(expected.Header.EdgeFlagBytes, actual.Header.EdgeFlagBytes);
        Assert.Equal(expected.Header.SectionTableOffset, actual.Header.SectionTableOffset);
        Assert.Equal(expected.Header.SectionCount, actual.Header.SectionCount);

        // Section table field-by-field
        Assert.Equal(9, actual.Sections.Length);
        Assert.Equal(expected.SectionTable.Length, actual.Sections.Length);
        for (int i = 0; i < expected.SectionTable.Length; i++)
        {
            Assert.Equal(expected.SectionTable[i].Kind, actual.Sections[i].Kind);
            Assert.Equal(expected.SectionTable[i].Flags, actual.Sections[i].Flags);
            Assert.Equal(expected.SectionTable[i].Offset, actual.Sections[i].Offset);
            Assert.Equal(expected.SectionTable[i].Length, actual.Sections[i].Length);
        }

        // FindSection 高速引きが 9 セクション全て解決できる
        for (ushort kind = 0x0001; kind <= 0x0009; kind++)
        {
            var entry = actual.FindSection(kind);
            Assert.Equal(kind, entry.Kind);
        }
    }

    [Fact]
    public void Parse_InvalidMagic_ThrowsOdrgFormatException()
    {
        var bytes = BuildSyntheticOdrg();
        // 先頭マジックを破壊
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'O';
        bytes[2] = (byte)'G';
        bytes[3] = (byte)'U';

        var ex = Assert.Throws<OdrgFormatException>(() => OdrgSectionDirectory.Parse(bytes));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_UnsupportedVersionMajor_ThrowsOdrgFormatException()
    {
        var bytes = BuildSyntheticOdrg(versionMajor: 2);

        var ex = Assert.Throws<OdrgFormatException>(() => OdrgSectionDirectory.Parse(bytes));
        Assert.Contains("VersionMajor", ex.Message);
    }

    [Fact]
    public void Parse_SectionCountNotNine_ThrowsOdrgFormatException()
    {
        var bytes = BuildSyntheticOdrg(sectionCount: 10);

        var ex = Assert.Throws<OdrgFormatException>(() => OdrgSectionDirectory.Parse(bytes));
        Assert.Contains("SectionCount", ex.Message);
    }

    [Fact]
    public void Parse_SectionEntryOffsetOutOfBounds_ThrowsOdrgFormatException()
    {
        // 9 セクション、Section[0] の Length をファイル全長以上に設定して越境
        var bytes = BuildSyntheticOdrg(firstSectionLength: 100000UL);

        var ex = Assert.Throws<OdrgFormatException>(() => OdrgSectionDirectory.Parse(bytes));
        Assert.Contains("past EOF", ex.Message);
    }

    /// <summary>
    /// 検証ロジック単体テスト用の最小 `.odrg` バイト列（HEADER 256B + SECTION TABLE 9×24B = 472B）。
    /// 各セクション本体は length=0 とし、Section[0] のみ <paramref name="firstSectionLength"/> で越境を作れる。
    /// </summary>
    private static byte[] BuildSyntheticOdrg(
        ushort versionMajor = 1,
        uint edgeFlagBytes = 2,
        uint sectionCount = 9,
        ulong firstSectionLength = 0)
    {
        const int headerSize = OdrgFormat.HeaderSize;
        const int entrySize = OdrgFormat.SectionTableEntrySize;
        int sectionTableOffset = headerSize;
        int fileLen = headerSize + (int)sectionCount * entrySize;
        var bytes = new byte[fileLen];

        // Magic "ODRG\0\0\0\0"
        bytes[0] = (byte)'O';
        bytes[1] = (byte)'D';
        bytes[2] = (byte)'R';
        bytes[3] = (byte)'G';
        // bytes 4..7: 0 (default)

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), versionMajor);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), 0); // VersionMinor
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 0); // Flags
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16, 8), 0); // VertexCount
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24, 8), 0); // EdgeCount
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(32, 8), 0); // bbox.minLon
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(40, 8), 0); // bbox.minLat
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(48, 8), 0); // bbox.maxLon
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(56, 8), 0); // bbox.maxLat
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64, 4), 0); // ProfileCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68, 4), edgeFlagBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(72, 8), (ulong)sectionTableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80, 4), sectionCount);

        // Section table: kind=0x0001..., flags=0, offset=0, length=0 (但し Section[0] のみ可変)
        for (int i = 0; i < (int)sectionCount; i++)
        {
            int o = sectionTableOffset + i * entrySize;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(o + 0, 2), (ushort)(i + 1));
            // bytes 2..3: reserved 0
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(o + 4, 4), 0);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(o + 8, 8), 0);
            ulong len = (i == 0) ? firstSectionLength : 0UL;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(o + 16, 8), len);
        }

        return bytes;
    }
}
