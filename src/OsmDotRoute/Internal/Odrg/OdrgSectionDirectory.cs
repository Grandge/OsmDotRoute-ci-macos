using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OsmDotRoute.Internal.Odrg;

/// <summary>
/// `.odrg` の AABB バイナリレイアウト（経度経度経度経度の double × 4、32 byte 固定）。
/// </summary>
/// <remarks>
/// 既存の <see cref="OsmDotRoute.Geometry.Aabb"/>（GeoCoordinate × 2、Lat/Lon 順）とは
/// レイアウトが異なる。本型はファイル形式（Lon/Lat 順）と一致しており、
/// Phase 3 ステップ 3A.2 で <c>MemoryMarshal.Cast&lt;byte, OdrgBbox&gt;</c> を行う際の
/// 受け皿になる。<c>OsmDotRoute.Extractor.Pipeline.Aabb</c>（Extractor アセンブリ）と論理同一だが
/// Core 独立定義（DRY 一時違反、3C で統一予定）。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly record struct OdrgBbox(
    double MinLon,
    double MinLat,
    double MaxLon,
    double MaxLat);

/// <summary>
/// `.odrg` HEADER（256 byte 固定）の論理表現（仕様書 §1）。
/// </summary>
internal readonly record struct OdrgFileHeader(
    ushort VersionMajor,
    ushort VersionMinor,
    uint Flags,
    ulong VertexCount,
    ulong EdgeCount,
    OdrgBbox Bbox,
    uint ProfileCount,
    uint EdgeFlagBytes,
    ulong SectionTableOffset,
    uint SectionCount,
    OdrgBbox RequestedBbox)
{
    /// <summary>VersionMinor=1 以降で <see cref="RequestedBbox"/> が明示的に書き込まれる。</summary>
    public bool HasRequestedBbox => VersionMinor >= OdrgFormat.VersionMinorRequestedBbox;
}

/// <summary>
/// セクションテーブルエントリ（24 byte 固定、仕様書 §2）。
/// </summary>
internal readonly record struct OdrgSectionEntry(
    ushort Kind,
    uint Flags,
    ulong Offset,
    ulong Length);

/// <summary>
/// `.odrg` 先頭 HEADER + 末尾 SECTION TABLE をパースした結果を保持し、
/// kind→エントリの高速引きを提供する読み取り専用ディレクトリ（Phase 3 ステップ 3A.1）。
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 ステップ 3A.1 のコア型。3A.2 以降で MMF 経由のセクション本体 Span 切出に
/// 本ディレクトリのオフセット情報を流用する。バイナリ全体を managed バッファに
/// 一旦コピーする実装は本ステップに限った素朴版（ホットパス化は 3A.2）。
/// </para>
/// <para>
/// 検証項目: マジック / VersionMajor / EdgeFlagBytes / SectionCount / オフセット越境。
/// 検証失敗はすべて <see cref="OdrgFormatException"/> として送出する。
/// </para>
/// </remarks>
internal sealed class OdrgSectionDirectory
{
    private readonly Dictionary<ushort, int> _kindIndex;

    public OdrgFileHeader Header { get; }

    public ImmutableArray<OdrgSectionEntry> Sections { get; }

    private OdrgSectionDirectory(OdrgFileHeader header, ImmutableArray<OdrgSectionEntry> sections)
    {
        Header = header;
        Sections = sections;
        _kindIndex = new Dictionary<ushort, int>(sections.Length);
        for (int i = 0; i < sections.Length; i++)
        {
            _kindIndex[sections[i].Kind] = i;
        }
    }

    /// <summary>
    /// セクション kind から対応エントリを取得する。
    /// </summary>
    /// <exception cref="OdrgFormatException">指定 kind が見つからない場合。</exception>
    public OdrgSectionEntry FindSection(ushort kind)
    {
        if (_kindIndex.TryGetValue(kind, out var index))
        {
            return Sections[index];
        }
        throw new OdrgFormatException($"Section kind 0x{kind:X4} missing from section table.");
    }

    /// <summary>
    /// MMF ハンドルからファイル全体を managed バッファに読み込み、HEADER + SECTION TABLE をパースする。
    /// </summary>
    /// <param name="handle">読み取り専用の MMF ビューハンドル。</param>
    /// <param name="fileLength">ファイル全長（バイト）。</param>
    public static OdrgSectionDirectory Read(SafeMemoryMappedViewHandle handle, long fileLength)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (fileLength < OdrgFormat.HeaderSize)
        {
            throw new OdrgFormatException(
                $"File too small: {fileLength} bytes < {OdrgFormat.HeaderSize} byte header.");
        }
        if (fileLength > int.MaxValue)
        {
            throw new OdrgFormatException(
                $"File too large for Phase 3 step 3A.1 (managed-copy parse): {fileLength} bytes > int.MaxValue.");
        }

        var buffer = new byte[fileLength];
        handle.ReadArray<byte>(0, buffer, 0, (int)fileLength);
        return Parse(buffer);
    }

    /// <summary>
    /// バイト列から HEADER + SECTION TABLE をパースする（テスト直接呼出 / 内部分割実装用）。
    /// </summary>
    internal static OdrgSectionDirectory Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < OdrgFormat.HeaderSize)
        {
            throw new OdrgFormatException(
                $"File too small: {bytes.Length} bytes < {OdrgFormat.HeaderSize} byte header.");
        }

        var header = ParseHeader(bytes);
        var sections = ParseSectionTable(bytes, header);
        return new OdrgSectionDirectory(header, sections);
    }

    private static OdrgFileHeader ParseHeader(ReadOnlySpan<byte> span)
    {
        var magic = span.Slice(0, 8);
        var expected = OdrgFormat.MagicBytes;
        for (int i = 0; i < expected.Length; i++)
        {
            if (magic[i] != expected[i])
            {
                throw new OdrgFormatException(
                    $"Invalid magic bytes: expected 'ODRG\\0\\0\\0\\0', got [{BitConverter.ToString(magic.ToArray())}].");
            }
        }

        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(8, 2));
        ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(10, 2));

        if (major != OdrgFormat.VersionMajor)
        {
            throw new OdrgFormatException(
                $"Unsupported VersionMajor: {major}, expected {OdrgFormat.VersionMajor}.");
        }

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4));
        ulong vertexCount = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(16, 8));
        ulong edgeCount = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(24, 8));

        double minLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(32, 8));
        double minLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(40, 8));
        double maxLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(48, 8));
        double maxLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(56, 8));
        var bbox = new OdrgBbox(minLon, minLat, maxLon, maxLat);

        uint profileCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(64, 4));
        uint edgeFlagBytes = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(68, 4));
        ulong sectionTableOffset = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(72, 8));
        uint sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(80, 4));

        if (edgeFlagBytes != OdrgFormat.EdgeFlagBytes)
        {
            throw new OdrgFormatException(
                $"Unsupported EdgeFlagBytes: {edgeFlagBytes}, expected {OdrgFormat.EdgeFlagBytes}.");
        }
        if (sectionCount != 9)
        {
            throw new OdrgFormatException(
                $"Unexpected SectionCount: {sectionCount}, expected 9 (Phase 3 v1.0 spec).");
        }

        OdrgBbox requestedBbox;
        if (minor >= OdrgFormat.VersionMinorRequestedBbox)
        {
            double rMinLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(88, 8));
            double rMinLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(96, 8));
            double rMaxLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(104, 8));
            double rMaxLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(112, 8));
            requestedBbox = new OdrgBbox(rMinLon, rMinLat, rMaxLon, rMaxLat);
        }
        else
        {
            requestedBbox = default;
        }

        return new OdrgFileHeader(
            VersionMajor: major,
            VersionMinor: minor,
            Flags: flags,
            VertexCount: vertexCount,
            EdgeCount: edgeCount,
            Bbox: bbox,
            ProfileCount: profileCount,
            EdgeFlagBytes: edgeFlagBytes,
            SectionTableOffset: sectionTableOffset,
            SectionCount: sectionCount,
            RequestedBbox: requestedBbox);
    }

    private static ImmutableArray<OdrgSectionEntry> ParseSectionTable(ReadOnlySpan<byte> span, OdrgFileHeader header)
    {
        long tableLen = (long)header.SectionCount * OdrgFormat.SectionTableEntrySize;
        long tableEnd = (long)header.SectionTableOffset + tableLen;
        if ((long)header.SectionTableOffset < 0 || tableEnd > span.Length)
        {
            throw new OdrgFormatException(
                $"Section table extends past EOF: offset={header.SectionTableOffset} len={tableLen} fileLen={span.Length}.");
        }

        var builder = ImmutableArray.CreateBuilder<OdrgSectionEntry>((int)header.SectionCount);
        int baseOff = checked((int)header.SectionTableOffset);
        for (int i = 0; i < header.SectionCount; i++)
        {
            int o = baseOff + i * OdrgFormat.SectionTableEntrySize;
            ushort kind = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(o + 0, 2));
            uint entryFlags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 4, 4));
            ulong entryOffset = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(o + 8, 8));
            ulong entryLength = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(o + 16, 8));

            long entryEnd;
            try
            {
                entryEnd = checked((long)entryOffset + (long)entryLength);
            }
            catch (OverflowException)
            {
                throw new OdrgFormatException(
                    $"Section[{i}] (kind=0x{kind:X4}) offset+length overflow: offset={entryOffset} length={entryLength}.");
            }
            if (entryEnd > span.Length)
            {
                throw new OdrgFormatException(
                    $"Section[{i}] (kind=0x{kind:X4}) extends past EOF: offset={entryOffset} length={entryLength} fileLen={span.Length}.");
            }

            builder.Add(new OdrgSectionEntry(kind, entryFlags, entryOffset, entryLength));
        }
        return builder.MoveToImmutable();
    }
}
