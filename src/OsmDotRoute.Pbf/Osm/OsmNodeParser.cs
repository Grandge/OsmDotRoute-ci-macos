using System;
using System.IO;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の単体 Node メッセージを解析する static パーサー。
/// </summary>
/// <remarks>
/// <para>Node フィールド (proto2 osmformat.proto)：</para>
/// <list type="bullet">
///   <item>field 1: id (sint64, required) — zigzag</item>
///   <item>field 2: keys (repeated uint32, packed) — StringTable インデックス</item>
///   <item>field 3: vals (repeated uint32, packed) — StringTable インデックス</item>
///   <item>field 4: info (Info, optional) — Phase 2 ではスキップ (ルーティング用途で metadata 不要)</item>
///   <item>field 8: lat (sint64, required) — granularity × encoded</item>
///   <item>field 9: lon (sint64, required) — granularity × encoded</item>
/// </list>
/// <para>
/// 現代 OSM PBF では DenseNodes (ステップ 2.7) が主流。本パーサーは仕様完全性のために用意するが、
/// 実際のファイルでは呼ばれる頻度は低い。
/// </para>
/// <para>
/// packed encoding 専用。非 packed (旧形式) は <see cref="InvalidDataException"/> で拒否する。
/// </para>
/// </remarks>
internal static class OsmNodeParser
{
    /// <summary>単体 Node メッセージのバイト列を解析する。</summary>
    /// <param name="nodeBytes">Node protobuf バイト列。</param>
    /// <param name="block">座標変換に使う <see cref="PrimitiveBlock"/>。</param>
    public static OsmNode Parse(ReadOnlySpan<byte> nodeBytes, PrimitiveBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var reader = new ProtoReader(nodeBytes);
        long? id = null;
        long? encodedLat = null;
        long? encodedLon = null;
        int[] keys = Array.Empty<int>();
        int[] vals = Array.Empty<int>();

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // id (sint64)
                    EnsureWireType(tag, WireType.Varint, "Node.id");
                    id = reader.ReadZigzag64();
                    break;
                case 2: // keys (packed uint32)
                    EnsureWireType(tag, WireType.LengthDelimited, "Node.keys");
                    keys = PackedReader.ReadPackedUint32(reader.ReadLengthDelimited());
                    break;
                case 3: // vals (packed uint32)
                    EnsureWireType(tag, WireType.LengthDelimited, "Node.vals");
                    vals = PackedReader.ReadPackedUint32(reader.ReadLengthDelimited());
                    break;
                case 4: // info (Info) — Phase 2 では未使用
                    EnsureWireType(tag, WireType.LengthDelimited, "Node.info");
                    reader.SkipField(tag.WireType);
                    break;
                case 8: // lat (sint64)
                    EnsureWireType(tag, WireType.Varint, "Node.lat");
                    encodedLat = reader.ReadZigzag64();
                    break;
                case 9: // lon (sint64)
                    EnsureWireType(tag, WireType.Varint, "Node.lon");
                    encodedLon = reader.ReadZigzag64();
                    break;
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        if (id is null)
            throw new InvalidDataException("Node is missing required field 'id'.");
        if (encodedLat is null)
            throw new InvalidDataException("Node is missing required field 'lat'.");
        if (encodedLon is null)
            throw new InvalidDataException("Node is missing required field 'lon'.");

        if (keys.Length != vals.Length)
            throw new InvalidDataException(
                $"Node tag arrays length mismatch: keys={keys.Length}, vals={vals.Length}.");

        return new OsmNode(
            Id: id.Value,
            Lon: block.ToLon(encodedLon.Value),
            Lat: block.ToLat(encodedLat.Value),
            TagKeys: keys,
            TagValues: vals);
    }

    private static void EnsureWireType(ProtoTag tag, WireType expected, string fieldName)
    {
        if (tag.WireType != expected)
            throw new InvalidDataException(
                $"{fieldName} expected wire-type {expected} but got {tag.WireType}.");
    }
}
