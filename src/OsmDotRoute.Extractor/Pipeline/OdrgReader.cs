using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using OsmDotRoute;
using OsmDotRoute.Internal.Odrg;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// <see cref="OdrgWriter"/> の出力を読み戻す eager parse リーダー。Phase 2 ステップ 5.1。
/// </summary>
/// <remarks>
/// <para>
/// 検証専用。MapVerifier オーバーレイ・整合テスト・RouterDb 突合に使う。
/// 全データを managed 配列に展開するため、ホットパス用途は想定しない。
/// </para>
/// <para>
/// Phase 3 で実装予定の <c>NativeRoadGraph</c>（MMF + <c>ReadOnlySpan</c>）とは別設計。
/// </para>
/// </remarks>
internal static class OdrgReader
{
    /// <summary>ファイルパスから `.odrg` を読み込む。</summary>
    public static OdrgReadResult Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    /// <summary>ストリームから `.odrg` を読み込む（全バイト eager copy 後にパース）。</summary>
    public static OdrgReadResult Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] bytes = ReadAllBytes(stream);
        return Parse(bytes);
    }

    /// <summary>バイト配列から直接パース（テストの利便性のため）。</summary>
    public static OdrgReadResult Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < OdrgFormat.HeaderSize)
            throw new InvalidDataException(
                $"File too small: {bytes.Length} bytes < {OdrgFormat.HeaderSize} byte header");

        var span = bytes.AsSpan();
        var header = ReadHeader(span);
        var sectionTable = ReadSectionTable(span, header);

        var vertices = ReadVertices(span, FindSection(sectionTable, OdrgFormat.SectionVertexTable), header.VertexCount);
        var edges = ReadEdges(span, FindSection(sectionTable, OdrgFormat.SectionEdgeTable), header.EdgeCount);
        var shapes = ReadShapes(span, FindSection(sectionTable, OdrgFormat.SectionEdgeShapeBuffer), edges);
        var aabbs = ReadAabbs(span, FindSection(sectionTable, OdrgFormat.SectionEdgeAabbTable), header.EdgeCount);
        var flags = ReadFlags(span, FindSection(sectionTable, OdrgFormat.SectionEdgeFlagTable), header.EdgeCount);
        var rtree = ReadRTree(span, FindSection(sectionTable, OdrgFormat.SectionEdgeSpatialIndex));
        var profileTable = ReadBakedProfileTable(
            span,
            FindSection(sectionTable, OdrgFormat.SectionBakedProfileTable),
            (int)header.EdgeCount);
        var turnRestrictRaw = ReadRaw(span, FindSection(sectionTable, OdrgFormat.SectionTurnRestrictionTable));
        var metadataJson = ReadMetadata(span, FindSection(sectionTable, OdrgFormat.SectionMetadata));

        return new OdrgReadResult(
            Header: header,
            SectionTable: sectionTable,
            Vertices: vertices,
            Edges: edges,
            EdgeShapes: shapes,
            EdgeAabbs: aabbs,
            EdgeFlags: flags,
            RTree: rtree,
            ProfileTable: profileTable,
            TurnRestrictionRaw: turnRestrictRaw,
            MetadataJson: metadataJson);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream ms)
            return ms.ToArray();

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static OdrgHeader ReadHeader(ReadOnlySpan<byte> span)
    {
        // Magic 0..7
        var magic = span.Slice(0, 8);
        var expected = OdrgFormat.MagicBytes;
        for (int i = 0; i < expected.Length; i++)
            if (magic[i] != expected[i])
                throw new InvalidDataException(
                    $"Invalid magic bytes: expected 'ODRG\\0\\0\\0\\0', got bytes[0..8]=[{BitConverter.ToString(magic.ToArray())}]");

        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(8, 2));
        ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(10, 2));

        if (major != OdrgFormat.VersionMajor)
            throw new InvalidDataException(
                $"Unsupported version: major={major}, expected {OdrgFormat.VersionMajor}");

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4));
        ulong vertexCount = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(16, 8));
        ulong edgeCount = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(24, 8));

        double minLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(32, 8));
        double minLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(40, 8));
        double maxLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(48, 8));
        double maxLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(56, 8));
        var bbox = new Aabb(minLon, minLat, maxLon, maxLat);

        uint profileCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(64, 4));
        uint edgeFlagBytes = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(68, 4));
        ulong sectionTableOffset = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(72, 8));
        uint sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(80, 4));

        if (edgeFlagBytes != OdrgFormat.EdgeFlagBytes)
            throw new InvalidDataException(
                $"Unsupported edgeFlagBytes: {edgeFlagBytes}, expected {OdrgFormat.EdgeFlagBytes}");

        Aabb requestedBbox;
        if (minor >= OdrgFormat.VersionMinorRequestedBbox)
        {
            double rMinLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(88, 8));
            double rMinLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(96, 8));
            double rMaxLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(104, 8));
            double rMaxLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(112, 8));
            requestedBbox = new Aabb(rMinLon, rMinLat, rMaxLon, rMaxLat);
        }
        else
        {
            requestedBbox = default;
        }

        return new OdrgHeader(
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

    private static OdrgSectionTableEntry[] ReadSectionTable(ReadOnlySpan<byte> span, OdrgHeader header)
    {
        if (header.SectionCount == 0)
            throw new InvalidDataException("sectionCount == 0");

        int tableLen = checked((int)header.SectionCount * OdrgFormat.SectionTableEntrySize);
        if ((long)header.SectionTableOffset + tableLen > span.Length)
            throw new InvalidDataException(
                $"Section table extends past EOF: offset={header.SectionTableOffset} len={tableLen} fileLen={span.Length}");

        var table = new OdrgSectionTableEntry[header.SectionCount];
        for (int i = 0; i < header.SectionCount; i++)
        {
            int o = (int)header.SectionTableOffset + i * OdrgFormat.SectionTableEntrySize;
            ushort kind = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(o + 0, 2));
            // bytes 2..3: reserved
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 4, 4));
            ulong off = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(o + 8, 8));
            ulong len = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(o + 16, 8));
            table[i] = new OdrgSectionTableEntry(kind, flags, off, len);
        }
        return table;
    }

    private static OdrgSectionTableEntry FindSection(OdrgSectionTableEntry[] table, ushort kind)
    {
        for (int i = 0; i < table.Length; i++)
            if (table[i].Kind == kind) return table[i];
        throw new InvalidDataException($"Section kind 0x{kind:X4} missing from section table");
    }

    private static GeoCoordinate[] ReadVertices(ReadOnlySpan<byte> span, OdrgSectionTableEntry section, ulong vertexCount)
    {
        long expectedLen = (long)vertexCount * OdrgFormat.VertexSize;
        if ((long)section.Length != expectedLen)
            throw new InvalidDataException(
                $"Vertex section length mismatch: expected {expectedLen}, got {section.Length}");

        var vertices = new GeoCoordinate[vertexCount];
        int baseOff = checked((int)section.Offset);
        for (ulong i = 0; i < vertexCount; i++)
        {
            int o = baseOff + (int)i * OdrgFormat.VertexSize;
            double lon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 0, 8));
            double lat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 8, 8));
            vertices[i] = new GeoCoordinate(lat, lon);
        }
        return vertices;
    }

    private static OdrgEdge[] ReadEdges(ReadOnlySpan<byte> span, OdrgSectionTableEntry section, ulong edgeCount)
    {
        long expectedLen = (long)edgeCount * OdrgFormat.EdgeSize;
        if ((long)section.Length != expectedLen)
            throw new InvalidDataException(
                $"Edge section length mismatch: expected {expectedLen}, got {section.Length}");

        var edges = new OdrgEdge[edgeCount];
        int baseOff = checked((int)section.Offset);
        for (ulong i = 0; i < edgeCount; i++)
        {
            int o = baseOff + (int)i * OdrgFormat.EdgeSize;
            uint from = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 0, 4));
            uint to = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 4, 4));
            ulong shapeOff = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(o + 8, 8));
            uint shapeLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 16, 4));
            uint bakedIdx = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 20, 4));
            edges[i] = new OdrgEdge(from, to, shapeOff, shapeLen, bakedIdx);
        }
        return edges;
    }

    private static GeoCoordinate[][] ReadShapes(ReadOnlySpan<byte> span, OdrgSectionTableEntry section, OdrgEdge[] edges)
    {
        int baseOff = checked((int)section.Offset);
        var shapes = new GeoCoordinate[edges.Length][];
        for (int e = 0; e < edges.Length; e++)
        {
            int count = checked((int)edges[e].ShapePointCount);
            var arr = new GeoCoordinate[count];
            int bufStart = baseOff + checked((int)edges[e].ShapeOffset);
            for (int j = 0; j < count; j++)
            {
                int o = bufStart + j * OdrgFormat.ShapePointSize;
                double lon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 0, 8));
                double lat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 8, 8));
                arr[j] = new GeoCoordinate(lat, lon);
            }
            shapes[e] = arr;
        }
        return shapes;
    }

    private static Aabb[] ReadAabbs(ReadOnlySpan<byte> span, OdrgSectionTableEntry section, ulong edgeCount)
    {
        long expectedLen = (long)edgeCount * OdrgFormat.EdgeAabbSize;
        if ((long)section.Length != expectedLen)
            throw new InvalidDataException(
                $"AABB section length mismatch: expected {expectedLen}, got {section.Length}");

        var aabbs = new Aabb[edgeCount];
        int baseOff = checked((int)section.Offset);
        for (ulong i = 0; i < edgeCount; i++)
        {
            int o = baseOff + (int)i * OdrgFormat.EdgeAabbSize;
            double minLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 0, 8));
            double minLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 8, 8));
            double maxLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 16, 8));
            double maxLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 24, 8));
            aabbs[i] = new Aabb(minLon, minLat, maxLon, maxLat);
        }
        return aabbs;
    }

    private static EdgeFlags[] ReadFlags(ReadOnlySpan<byte> span, OdrgSectionTableEntry section, ulong edgeCount)
    {
        long expectedLen = (long)edgeCount * OdrgFormat.EdgeFlagBytes;
        if ((long)section.Length != expectedLen)
            throw new InvalidDataException(
                $"Flag section length mismatch: expected {expectedLen}, got {section.Length}");

        var flags = new EdgeFlags[edgeCount];
        int baseOff = checked((int)section.Offset);
        for (ulong i = 0; i < edgeCount; i++)
        {
            int o = baseOff + (int)i * (int)OdrgFormat.EdgeFlagBytes;
            ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(o, 2));
            flags[i] = (EdgeFlags)raw;
        }
        return flags;
    }

    private static OdrgRTreeRead ReadRTree(ReadOnlySpan<byte> span, OdrgSectionTableEntry section)
    {
        int baseOff = checked((int)section.Offset);
        uint nodeCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(baseOff + 0, 4));
        uint rootIndex = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(baseOff + 4, 4));
        uint branching = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(baseOff + 8, 4));
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(baseOff + 12, 4));

        long expectedLen = OdrgFormat.RTreeHeaderSize + (long)nodeCount * OdrgFormat.RTreeNodeSize;
        if ((long)section.Length != expectedLen)
            throw new InvalidDataException(
                $"R-tree section length mismatch: expected {expectedLen}, got {section.Length} (nodeCount={nodeCount})");

        var nodes = new RTreeNode[nodeCount];
        int nodesOff = baseOff + OdrgFormat.RTreeHeaderSize;
        for (uint i = 0; i < nodeCount; i++)
        {
            int o = nodesOff + (int)i * OdrgFormat.RTreeNodeSize;
            double minLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 0, 8));
            double minLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 8, 8));
            double maxLon = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 16, 8));
            double maxLat = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(o + 24, 8));
            uint firstChild = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 32, 4));
            uint childCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 36, 4));
            uint nflags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 40, 4));
            // bytes 44..55: reserved 12 byte
            nodes[i] = new RTreeNode(
                new Aabb(minLon, minLat, maxLon, maxLat),
                firstChild,
                childCount,
                nflags);
        }
        return new OdrgRTreeRead(nodeCount, rootIndex, branching, height, nodes);
    }

    private static OdrgBakedProfileRead ReadBakedProfileTable(
        ReadOnlySpan<byte> span,
        OdrgSectionTableEntry section,
        int edgeCount)
    {
        int baseOff = checked((int)section.Offset);
        uint profileCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(baseOff + 0, 4));
        uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(baseOff + 4, 4));

        if (entrySize != OdrgFormat.BakedProfileEntrySize)
            throw new InvalidDataException(
                $"Unsupported bakedProfileEntrySize: {entrySize}, expected {OdrgFormat.BakedProfileEntrySize}");

        // Name table: profileCount * 8 bytes (offset u32 + length u32)
        int nameTableOff = baseOff + OdrgFormat.BakedProfileTableHeaderSize;
        var nameOffsets = new uint[profileCount];
        var nameLengths = new uint[profileCount];
        uint totalNameBufLen = 0;
        for (uint p = 0; p < profileCount; p++)
        {
            int o = nameTableOff + (int)p * OdrgFormat.BakedProfileNameTableEntrySize;
            nameOffsets[p] = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 0, 4));
            nameLengths[p] = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(o + 4, 4));
            totalNameBufLen += nameLengths[p];
        }

        // UTF-8 name buffer
        int nameBufOff = nameTableOff + (int)profileCount * OdrgFormat.BakedProfileNameTableEntrySize;
        var names = new string[profileCount];
        for (uint p = 0; p < profileCount; p++)
        {
            int start = nameBufOff + (int)nameOffsets[p];
            int len = (int)nameLengths[p];
            names[p] = Encoding.UTF8.GetString(span.Slice(start, len));
        }

        // Entries: profileCount * edgeCount * 8 bytes (profile major)
        int entriesOff = nameBufOff + (int)totalNameBufLen;
        long expectedSectionLen =
            OdrgFormat.BakedProfileTableHeaderSize
            + (long)profileCount * OdrgFormat.BakedProfileNameTableEntrySize
            + totalNameBufLen
            + (long)profileCount * edgeCount * OdrgFormat.BakedProfileEntrySize;
        if ((long)section.Length != expectedSectionLen)
            throw new InvalidDataException(
                $"Baked profile section length mismatch: expected {expectedSectionLen}, got {section.Length}");

        var entries = new BakedProfileEntry[profileCount][];
        for (uint p = 0; p < profileCount; p++)
        {
            var arr = new BakedProfileEntry[edgeCount];
            for (int e = 0; e < edgeCount; e++)
            {
                int o = entriesOff + ((int)p * edgeCount + e) * OdrgFormat.BakedProfileEntrySize;
                float speed = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(o + 0, 4));
                byte flags = span[o + 4];
                // bytes 5..7: reserved 3 byte
                arr[e] = new BakedProfileEntry(speed, flags);
            }
            entries[p] = arr;
        }

        return new OdrgBakedProfileRead(names, entrySize, entries);
    }

    private static byte[] ReadRaw(ReadOnlySpan<byte> span, OdrgSectionTableEntry section)
    {
        int len = checked((int)section.Length);
        if (len == 0) return Array.Empty<byte>();
        int baseOff = checked((int)section.Offset);
        return span.Slice(baseOff, len).ToArray();
    }

    private static string ReadMetadata(ReadOnlySpan<byte> span, OdrgSectionTableEntry section)
    {
        int len = checked((int)section.Length);
        if (len == 0) return string.Empty;
        int baseOff = checked((int)section.Offset);
        return Encoding.UTF8.GetString(span.Slice(baseOff, len));
    }
}
