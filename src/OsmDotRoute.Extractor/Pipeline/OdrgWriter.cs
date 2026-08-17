using System;
using System.IO;
using System.Text;
using OsmDotRoute;
using OsmDotRoute.Internal.Odrg;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// <see cref="OdrgWriteInput"/> を仕様書 §1〜§4 のレイアウトでバイナリ書出する。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 3.8。リトルエンディアン固定（仕様書 §0.2 P8 / §1.3）。
/// 書出順序: HEADER (256B) → SECTION TABLE → 各セクション本体（kind 昇順）。
/// </para>
/// <para>
/// <see cref="BinaryWriter"/> は .NET 9 で全プラットフォーム リトルエンディアン保証。
/// 各セクションのサイズを事前計算してオフセットを決定後、線形に書出。
/// </para>
/// </remarks>
internal static class OdrgWriter
{
    private const int SectionCount = 9;

    public static void Write(Stream output, OdrgWriteInput input)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        // ----- 各セクションサイズの事前計算 -----
        int vertexCount = input.Vertices.Length;
        int edgeCount = input.Edges.Length;

        long vertexLen = (long)vertexCount * OdrgFormat.VertexSize;
        long edgeLen = (long)edgeCount * OdrgFormat.EdgeSize;

        long shapeLen = 0;
        for (int i = 0; i < edgeCount; i++)
            shapeLen += (long)input.Edges[i].ShapeNodeRefs.Length * OdrgFormat.ShapePointSize;

        long aabbLen = (long)edgeCount * OdrgFormat.EdgeAabbSize;
        long flagLen = (long)edgeCount * OdrgFormat.EdgeFlagBytes;
        long rtreeLen = OdrgFormat.RTreeHeaderSize + (long)input.RTree.NodeCount * OdrgFormat.RTreeNodeSize;

        // Baked Profile Table のレイアウト計算
        // header(8) + name table(profileCount*8) + name UTF-8 buffer + entries(profileCount*edgeCount*8)
        int profileCount = input.ProfileTable.ProfileCount;
        long bakedHeader = OdrgFormat.BakedProfileTableHeaderSize;
        long bakedNameTable = (long)profileCount * OdrgFormat.BakedProfileNameTableEntrySize;

        var nameBytes = new byte[profileCount][];
        long bakedNameBuffer = 0;
        for (int p = 0; p < profileCount; p++)
        {
            nameBytes[p] = Encoding.UTF8.GetBytes(input.ProfileTable.ProfileNames[p]);
            bakedNameBuffer += nameBytes[p].Length;
        }
        long bakedEntries = (long)profileCount * edgeCount * OdrgFormat.BakedProfileEntrySize;
        long bakedLen = bakedHeader + bakedNameTable + bakedNameBuffer + bakedEntries;

        long turnRestrictLen = 0;  // v0.2 では予約のみ
        byte[] metadataBytes = Encoding.UTF8.GetBytes(input.MetadataJson ?? string.Empty);
        long metadataLen = metadataBytes.Length;

        // ----- オフセット計算 -----
        long sectionTableOffset = OdrgFormat.HeaderSize;
        long sectionsStart = sectionTableOffset + (long)SectionCount * OdrgFormat.SectionTableEntrySize;

        long offVertex = sectionsStart;
        long offEdge = offVertex + vertexLen;
        long offShape = offEdge + edgeLen;
        long offAabb = offShape + shapeLen;
        long offFlag = offAabb + aabbLen;
        long offRtree = offFlag + flagLen;
        long offBaked = offRtree + rtreeLen;
        long offTurn = offBaked + bakedLen;
        long offMeta = offTurn + turnRestrictLen;

        // ----- 書出 -----
        using var bw = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

        // HEADER (256B)
        WriteHeader(bw, vertexCount, edgeCount, input.Bbox, profileCount, sectionTableOffset, input.RequestedBbox);

        // SECTION TABLE (9 × 24B = 216B)
        WriteSectionEntry(bw, OdrgFormat.SectionVertexTable, offVertex, vertexLen);
        WriteSectionEntry(bw, OdrgFormat.SectionEdgeTable, offEdge, edgeLen);
        WriteSectionEntry(bw, OdrgFormat.SectionEdgeShapeBuffer, offShape, shapeLen);
        WriteSectionEntry(bw, OdrgFormat.SectionEdgeAabbTable, offAabb, aabbLen);
        WriteSectionEntry(bw, OdrgFormat.SectionEdgeFlagTable, offFlag, flagLen);
        WriteSectionEntry(bw, OdrgFormat.SectionEdgeSpatialIndex, offRtree, rtreeLen);
        WriteSectionEntry(bw, OdrgFormat.SectionBakedProfileTable, offBaked, bakedLen);
        WriteSectionEntry(bw, OdrgFormat.SectionTurnRestrictionTable, offTurn, turnRestrictLen);
        WriteSectionEntry(bw, OdrgFormat.SectionMetadata, offMeta, metadataLen);

        // Vertex Table
        for (int i = 0; i < vertexCount; i++)
        {
            bw.Write(input.Vertices[i].Longitude);
            bw.Write(input.Vertices[i].Latitude);
        }

        // Edge Table と Shape Buffer を並行構築（shape offset を確定するため）
        // Edge Table 書出
        long currentShapeOffset = 0;
        for (int i = 0; i < edgeCount; i++)
        {
            var e = input.Edges[i];
            bw.Write((uint)e.FromVertexId);
            bw.Write((uint)e.ToVertexId);
            bw.Write((ulong)currentShapeOffset);
            bw.Write((uint)e.ShapeNodeRefs.Length);
            bw.Write((uint)i);  // bakedProfileIndex == edgeId (仕様書 §4.7.5)
            currentShapeOffset += (long)e.ShapeNodeRefs.Length * OdrgFormat.ShapePointSize;
        }

        // Edge Shape Buffer
        for (int i = 0; i < edgeCount; i++)
        {
            long[] shape = input.Edges[i].ShapeNodeRefs;
            for (int j = 0; j < shape.Length; j++)
            {
                GeoCoordinate p = input.NodeCoordLookup(shape[j]);
                bw.Write(p.Longitude);
                bw.Write(p.Latitude);
            }
        }

        // Edge AABB Table
        for (int i = 0; i < edgeCount; i++)
        {
            var a = input.EdgeAabbs[i];
            bw.Write(a.MinLon);
            bw.Write(a.MinLat);
            bw.Write(a.MaxLon);
            bw.Write(a.MaxLat);
        }

        // Edge Flag Table
        for (int i = 0; i < edgeCount; i++)
            bw.Write((ushort)input.EdgeFlags[i]);

        // Edge Spatial Index (R-tree)
        bw.Write((uint)input.RTree.NodeCount);
        bw.Write(input.RTree.RootIndex);
        bw.Write(input.RTree.BranchingFactor);
        bw.Write(input.RTree.TreeHeight);
        ReadOnlySpan<byte> nodeReserved = stackalloc byte[12];
        for (int i = 0; i < input.RTree.NodeCount; i++)
        {
            var n = input.RTree.Nodes[i];
            bw.Write(n.Bounds.MinLon);
            bw.Write(n.Bounds.MinLat);
            bw.Write(n.Bounds.MaxLon);
            bw.Write(n.Bounds.MaxLat);
            bw.Write(n.FirstChildIndex);
            bw.Write(n.ChildCount);
            bw.Write(n.Flags);
            bw.Write(nodeReserved);  // 12 byte 予約
        }

        // Baked Profile Table
        bw.Write((uint)profileCount);
        bw.Write((uint)OdrgFormat.BakedProfileEntrySize);

        // Name table (string buffer offset は string buffer 内オフセット)
        uint nameBufCursor = 0;
        for (int p = 0; p < profileCount; p++)
        {
            bw.Write(nameBufCursor);
            bw.Write((uint)nameBytes[p].Length);
            nameBufCursor += (uint)nameBytes[p].Length;
        }
        // UTF-8 string buffer
        for (int p = 0; p < profileCount; p++)
            bw.Write(nameBytes[p]);
        // Entries (profile-major、各 8 byte)
        ReadOnlySpan<byte> entryReserved = stackalloc byte[3];
        for (int p = 0; p < profileCount; p++)
        {
            ReadOnlySpan<BakedProfileEntry> entries = input.ProfileTable.GetProfileEntries(p);
            for (int e = 0; e < entries.Length; e++)
            {
                bw.Write(entries[e].SpeedKmh);
                bw.Write(entries[e].Flags);
                bw.Write(entryReserved);
            }
        }

        // Turn Restriction Table: length=0 (予約のみ)

        // Metadata
        if (metadataLen > 0)
            bw.Write(metadataBytes);

        bw.Flush();
    }

    private static void WriteHeader(
        BinaryWriter bw,
        int vertexCount,
        int edgeCount,
        Aabb bbox,
        int profileCount,
        long sectionTableOffset,
        Aabb requestedBbox)
    {
        long startPos = bw.BaseStream.Position;

        bw.Write(OdrgFormat.MagicBytes);                   // 0..7
        bw.Write(OdrgFormat.VersionMajor);                  // 8..9
        bw.Write(OdrgFormat.VersionMinor);                  // 10..11
        bw.Write((uint)0);                                  // 12..15 flags
        bw.Write((ulong)vertexCount);                       // 16..23
        bw.Write((ulong)edgeCount);                         // 24..31
        bw.Write(bbox.MinLon);                              // 32..39
        bw.Write(bbox.MinLat);                              // 40..47
        bw.Write(bbox.MaxLon);                              // 48..55
        bw.Write(bbox.MaxLat);                              // 56..63
        bw.Write((uint)profileCount);                       // 64..67
        bw.Write(OdrgFormat.EdgeFlagBytes);                 // 68..71
        bw.Write((ulong)sectionTableOffset);                // 72..79
        bw.Write((uint)SectionCount);                       // 80..83
        bw.Write((uint)0);                                  // 84..87 reservedA
        bw.Write(requestedBbox.MinLon);                     // 88..95   (v0.3+)
        bw.Write(requestedBbox.MinLat);                     // 96..103
        bw.Write(requestedBbox.MaxLon);                     // 104..111
        bw.Write(requestedBbox.MaxLat);                     // 112..119

        // reservedB: 120..255 (136 byte) を 0 で埋める
        Span<byte> zero = stackalloc byte[136];
        bw.Write(zero);

        long written = bw.BaseStream.Position - startPos;
        if (written != OdrgFormat.HeaderSize)
            throw new InvalidOperationException(
                $"Header write size mismatch: expected {OdrgFormat.HeaderSize}, got {written}");
    }

    private static void WriteSectionEntry(BinaryWriter bw, ushort kind, long offset, long length)
    {
        bw.Write(kind);                       // 0..1
        bw.Write((ushort)0);                  // 2..3 reserved
        bw.Write((uint)0);                    // 4..7 flags
        bw.Write((ulong)offset);              // 8..15
        bw.Write((ulong)length);              // 16..23
    }

    private static void Validate(OdrgWriteInput input)
    {
        if (input.Vertices is null) throw new ArgumentException("Vertices is null", nameof(input));
        if (input.Edges is null) throw new ArgumentException("Edges is null", nameof(input));
        if (input.EdgeAabbs is null) throw new ArgumentException("EdgeAabbs is null", nameof(input));
        if (input.EdgeFlags is null) throw new ArgumentException("EdgeFlags is null", nameof(input));
        if (input.RTree is null) throw new ArgumentException("RTree is null", nameof(input));
        if (input.ProfileTable is null) throw new ArgumentException("ProfileTable is null", nameof(input));
        if (input.NodeCoordLookup is null) throw new ArgumentException("NodeCoordLookup is null", nameof(input));

        int n = input.Edges.Length;
        if (input.EdgeAabbs.Length != n)
            throw new ArgumentException($"EdgeAabbs length ({input.EdgeAabbs.Length}) != Edges length ({n})");
        if (input.EdgeFlags.Length != n)
            throw new ArgumentException($"EdgeFlags length ({input.EdgeFlags.Length}) != Edges length ({n})");
        if (input.ProfileTable.EdgeCount != n)
            throw new ArgumentException($"ProfileTable.EdgeCount ({input.ProfileTable.EdgeCount}) != Edges length ({n})");
    }
}
