using System;
using System.IO;
using OsmDotRoute.Pbf.Blob;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Pbf;

/// <summary>
/// OSM PBF ストリームを順次読み、コールバックで Node / Way / Relation を供給する高レベル API。
/// </summary>
/// <remarks>
/// <para>
/// 内部で <see cref="PbfBlobReader"/> / <see cref="OsmHeaderParser"/> / <see cref="PrimitiveBlockParser"/> /
/// <see cref="OsmNodeParser"/> / <see cref="DenseNodesParser"/> / <see cref="OsmWayParser"/> /
/// <see cref="OsmRelationParser"/> を組み合わせて、1 ファイル走査で全要素を供給する。
/// </para>
/// <para>
/// 抽出ツール (<c>OsmDotRoute.Extractor</c>) はマルチパス処理が必要なため、必要なコールバックだけを
/// 渡して複数回呼び出すパターンを想定している。
/// </para>
/// </remarks>
internal static class PbfReader
{
    /// <summary>PBF ストリームを最後まで読み、コールバックで要素を供給する。</summary>
    /// <param name="stream">入力 PBF ストリーム (FileStream / MemoryStream 等)。</param>
    /// <param name="onNode">Node / DenseNodes 要素のコールバック。null なら Node 系セクションをスキップ。</param>
    /// <param name="onWay">Way 要素のコールバック。null なら Way セクションをスキップ。</param>
    /// <param name="onRelation">Relation 要素のコールバック。null なら Relation セクションをスキップ。</param>
    /// <param name="leaveOpen">true なら <paramref name="stream"/> を Dispose しない。</param>
    /// <returns>ファイル先頭の OSMHeader を解析した結果。<see cref="OsmHeaderParser.EnsureSupported"/> 済。</returns>
    public static OsmHeader Read(
        Stream stream,
        Action<OsmNode, OsmStringTable>? onNode = null,
        Action<OsmWay, OsmStringTable>? onWay = null,
        Action<OsmRelation, OsmStringTable>? onRelation = null,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var blobReader = new PbfBlobReader(stream, leaveOpen);

        // 1. 先頭 blob は OSMHeader でなければならない
        if (!blobReader.MoveNext())
            throw new InvalidDataException("PBF stream is empty (no OSMHeader blob).");
        if (blobReader.CurrentType != PbfBlobType.Header)
            throw new InvalidDataException(
                $"First PBF blob must be OSMHeader, got {blobReader.CurrentType}.");

        OsmHeader header = OsmHeaderParser.Parse(blobReader.CurrentPayload);
        OsmHeaderParser.EnsureSupported(header);

        // 2. 以降は OSMData blob を順次処理
        while (blobReader.MoveNext())
        {
            switch (blobReader.CurrentType)
            {
                case PbfBlobType.Data:
                    ProcessPrimitiveBlock(blobReader.CurrentPayload, onNode, onWay, onRelation);
                    break;
                case PbfBlobType.Header:
                    throw new InvalidDataException(
                        "Unexpected duplicate OSMHeader blob after the first one.");
                case PbfBlobType.Unknown:
                    // 未知 blob 種別はスキップ (前方互換)
                    break;
            }
        }

        return header;
    }

    private static void ProcessPrimitiveBlock(
        ReadOnlySpan<byte> blockBytes,
        Action<OsmNode, OsmStringTable>? onNode,
        Action<OsmWay, OsmStringTable>? onWay,
        Action<OsmRelation, OsmStringTable>? onRelation)
    {
        // Pass 1: エンベロープ (StringTable + granularity + offsets) を解析。
        // PrimitiveBlockParser はステップ 2.5 設計で PrimitiveGroup (field 2) をスキップする。
        PrimitiveBlock block = PrimitiveBlockParser.Parse(blockBytes);

        // Pass 2: 同じバイト列を再走査して PrimitiveGroup (field 2) を抽出しディスパッチ。
        // blockBytes はメモリ上にあるため再走査は安価 (varint デコードのみ、IO なし)。
        var reader = new ProtoReader(blockBytes);
        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            if (tag.FieldNumber == 2 && tag.WireType == WireType.LengthDelimited)
            {
                ReadOnlySpan<byte> groupBytes = reader.ReadLengthDelimited();
                ProcessPrimitiveGroup(groupBytes, block, onNode, onWay, onRelation);
            }
            else
            {
                reader.SkipField(tag.WireType);
            }
        }
    }

    private static void ProcessPrimitiveGroup(
        ReadOnlySpan<byte> groupBytes,
        PrimitiveBlock block,
        Action<OsmNode, OsmStringTable>? onNode,
        Action<OsmWay, OsmStringTable>? onWay,
        Action<OsmRelation, OsmStringTable>? onRelation)
    {
        var reader = new ProtoReader(groupBytes);
        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // nodes (repeated Node)
                    EnsureWireType(tag, WireType.LengthDelimited, "PrimitiveGroup.nodes");
                    if (onNode is not null)
                    {
                        ReadOnlySpan<byte> nodeBytes = reader.ReadLengthDelimited();
                        OsmNode node = OsmNodeParser.Parse(nodeBytes, block);
                        onNode(node, block.StringTable);
                    }
                    else
                    {
                        reader.SkipField(tag.WireType);
                    }
                    break;

                case 2: // dense (DenseNodes)
                    EnsureWireType(tag, WireType.LengthDelimited, "PrimitiveGroup.dense");
                    if (onNode is not null)
                    {
                        ReadOnlySpan<byte> denseBytes = reader.ReadLengthDelimited();
                        OsmNode[] denseNodes = DenseNodesParser.Parse(denseBytes, block);
                        for (int i = 0; i < denseNodes.Length; i++)
                        {
                            onNode(denseNodes[i], block.StringTable);
                        }
                    }
                    else
                    {
                        reader.SkipField(tag.WireType);
                    }
                    break;

                case 3: // ways (repeated Way)
                    EnsureWireType(tag, WireType.LengthDelimited, "PrimitiveGroup.ways");
                    if (onWay is not null)
                    {
                        ReadOnlySpan<byte> wayBytes = reader.ReadLengthDelimited();
                        OsmWay way = OsmWayParser.Parse(wayBytes);
                        onWay(way, block.StringTable);
                    }
                    else
                    {
                        reader.SkipField(tag.WireType);
                    }
                    break;

                case 4: // relations (repeated Relation)
                    EnsureWireType(tag, WireType.LengthDelimited, "PrimitiveGroup.relations");
                    if (onRelation is not null)
                    {
                        ReadOnlySpan<byte> relationBytes = reader.ReadLengthDelimited();
                        OsmRelation relation = OsmRelationParser.Parse(relationBytes);
                        onRelation(relation, block.StringTable);
                    }
                    else
                    {
                        reader.SkipField(tag.WireType);
                    }
                    break;

                // field 5 = changesets (OsmDotRoute では未対応、スキップ)
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }
    }

    private static void EnsureWireType(ProtoTag tag, WireType expected, string fieldName)
    {
        if (tag.WireType != expected)
            throw new InvalidDataException(
                $"{fieldName} expected wire-type {expected} but got {tag.WireType}.");
    }
}
