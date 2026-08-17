using System;
using System.IO;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の Way メッセージを解析する static パーサー。
/// </summary>
/// <remarks>
/// <para>Way フィールド (proto2 osmformat.proto)：</para>
/// <list type="bullet">
///   <item>field 1: id (int64, required) — plain varint (sint64 ではない、zigzag 不要)</item>
///   <item>field 2: keys (repeated uint32, packed) — StringTable インデックス</item>
///   <item>field 3: vals (repeated uint32, packed) — StringTable インデックス</item>
///   <item>field 4: info (Info, optional) — Phase 2 ではスキップ</item>
///   <item>field 8: refs (repeated sint64, packed) — <b>delta-coded</b> zigzag、ノード ID 列</item>
///   <item>field 9-10: lat / lon (LocationsOnWays 拡張、optional) — Phase 2 ではスキップ</item>
/// </list>
/// <para>
/// Way は道路ネットワーク抽出の中核データ。Phase 2 抽出ツールは `highway=*` タグを持つ way を取り込み、
/// <see cref="OsmWay.NodeRefs"/> を頂点列として使用する。
/// </para>
/// </remarks>
internal static class OsmWayParser
{
    /// <summary>Way メッセージのバイト列を解析する。</summary>
    /// <remarks>
    /// 座標変換は不要なため <see cref="PrimitiveBlock"/> を受け取らない（tag 解決は呼出側で
    /// <see cref="OsmStringTable"/> 経由で行う）。
    /// </remarks>
    public static OsmWay Parse(ReadOnlySpan<byte> wayBytes)
    {
        var reader = new ProtoReader(wayBytes);
        long? id = null;
        int[] keys = Array.Empty<int>();
        int[] vals = Array.Empty<int>();
        long[] refs = Array.Empty<long>();

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // id (int64 plain varint)
                    EnsureWireType(tag, WireType.Varint, "Way.id");
                    id = unchecked((long)reader.ReadVarint64());
                    break;
                case 2: // keys (packed uint32)
                    EnsureWireType(tag, WireType.LengthDelimited, "Way.keys");
                    keys = PackedReader.ReadPackedUint32(reader.ReadLengthDelimited());
                    break;
                case 3: // vals (packed uint32)
                    EnsureWireType(tag, WireType.LengthDelimited, "Way.vals");
                    vals = PackedReader.ReadPackedUint32(reader.ReadLengthDelimited());
                    break;
                case 4: // info (Info, optional) — Phase 2 では未使用
                    EnsureWireType(tag, WireType.LengthDelimited, "Way.info");
                    reader.SkipField(tag.WireType);
                    break;
                case 8: // refs (packed sint64, delta-coded)
                    EnsureWireType(tag, WireType.LengthDelimited, "Way.refs");
                    refs = PackedReader.ReadPackedZigzag64(reader.ReadLengthDelimited());
                    break;
                case 9: // lat (LocationsOnWays 拡張、Phase 2 では未使用)
                case 10: // lon (LocationsOnWays 拡張、Phase 2 では未使用)
                    reader.SkipField(tag.WireType);
                    break;
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        if (id is null)
            throw new InvalidDataException("Way is missing required field 'id'.");

        if (keys.Length != vals.Length)
            throw new InvalidDataException(
                $"Way tag arrays length mismatch: keys={keys.Length}, vals={vals.Length}.");

        // refs を in-place で delta デコード（絶対 ID 列に変換）
        if (refs.Length > 0)
        {
            long currentRef = 0;
            for (int i = 0; i < refs.Length; i++)
            {
                currentRef += refs[i];
                refs[i] = currentRef;
            }
        }

        return new OsmWay(
            Id: id.Value,
            NodeRefs: refs,
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
