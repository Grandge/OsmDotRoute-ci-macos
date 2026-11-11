using System;
using System.IO;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の Relation メッセージを解析する static パーサー。
/// </summary>
/// <remarks>
/// <para>Relation フィールド (proto2 osmformat.proto)：</para>
/// <list type="bullet">
///   <item>field 1: id (int64, required) — plain varint</item>
///   <item>field 2: keys (packed uint32) — StringTable インデックス</item>
///   <item>field 3: vals (packed uint32) — StringTable インデックス</item>
///   <item>field 4: info (Info, optional) — Phase 2 ではスキップ</item>
///   <item>field 8: roles_sid (packed int32) — role 文字列の StringTable インデックス</item>
///   <item>field 9: memids (packed sint64) — <b>delta-coded</b> zigzag、メンバー ID</item>
///   <item>field 10: types (packed MemberType enum) — 0=NODE / 1=WAY / 2=RELATION</item>
/// </list>
/// <para>
/// Phase 2 抽出フローでは Relation を使わない。`type=restriction` などのターン制限対応は
/// Phase 4+ に延期（計画書 §5.6-18）。本パーサーは仕様完全性のために提供。
/// </para>
/// </remarks>
internal static class OsmRelationParser
{
    /// <summary>Relation メッセージのバイト列を解析する。</summary>
    public static OsmRelation Parse(ReadOnlySpan<byte> relationBytes)
    {
        var reader = new ProtoReader(relationBytes);
        long? id = null;
        int[] keys = Array.Empty<int>();
        int[] vals = Array.Empty<int>();
        int[] rolesSid = Array.Empty<int>();
        long[] memids = Array.Empty<long>();
        int[] types = Array.Empty<int>();

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // id (int64 plain varint)
                    EnsureWireType(tag, WireType.Varint, "Relation.id");
                    id = unchecked((long)reader.ReadVarint64());
                    break;
                case 2: // keys (packed uint32)
                    EnsureWireType(tag, WireType.LengthDelimited, "Relation.keys");
                    keys = PackedReader.ReadPackedUint32(reader.ReadLengthDelimited());
                    break;
                case 3: // vals (packed uint32)
                    EnsureWireType(tag, WireType.LengthDelimited, "Relation.vals");
                    vals = PackedReader.ReadPackedUint32(reader.ReadLengthDelimited());
                    break;
                case 4: // info (Info, optional) — Phase 2 では未使用
                    EnsureWireType(tag, WireType.LengthDelimited, "Relation.info");
                    reader.SkipField(tag.WireType);
                    break;
                case 8: // roles_sid (packed int32)
                    EnsureWireType(tag, WireType.LengthDelimited, "Relation.roles_sid");
                    rolesSid = PackedReader.ReadPackedUint32(reader.ReadLengthDelimited());
                    break;
                case 9: // memids (packed sint64, delta-coded)
                    EnsureWireType(tag, WireType.LengthDelimited, "Relation.memids");
                    memids = PackedReader.ReadPackedZigzag64(reader.ReadLengthDelimited());
                    break;
                case 10: // types (packed MemberType enum, int32 varint)
                    EnsureWireType(tag, WireType.LengthDelimited, "Relation.types");
                    types = PackedReader.ReadPackedUint32(reader.ReadLengthDelimited());
                    break;
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        if (id is null)
            throw new InvalidDataException("Relation is missing required field 'id'.");

        if (keys.Length != vals.Length)
            throw new InvalidDataException(
                $"Relation tag arrays length mismatch: keys={keys.Length}, vals={vals.Length}.");

        if (rolesSid.Length != memids.Length || rolesSid.Length != types.Length)
            throw new InvalidDataException(
                $"Relation member arrays length mismatch: " +
                $"roles_sid={rolesSid.Length}, memids={memids.Length}, types={types.Length}.");

        int memberCount = memids.Length;

        // memids を in-place で delta デコード
        if (memberCount > 0)
        {
            long currentMemId = 0;
            for (int i = 0; i < memberCount; i++)
            {
                currentMemId += memids[i];
                memids[i] = currentMemId;
            }
        }

        // メンバー配列構築
        OsmRelationMember[] members;
        if (memberCount == 0)
        {
            members = Array.Empty<OsmRelationMember>();
        }
        else
        {
            members = new OsmRelationMember[memberCount];
            for (int i = 0; i < memberCount; i++)
            {
                int typeRaw = types[i];
                OsmMemberType type = typeRaw switch
                {
                    0 => OsmMemberType.Node,
                    1 => OsmMemberType.Way,
                    2 => OsmMemberType.Relation,
                    _ => throw new InvalidDataException(
                        $"Relation member type {typeRaw} is unknown (expected 0=NODE / 1=WAY / 2=RELATION)."),
                };
                members[i] = new OsmRelationMember(
                    MemberId: memids[i],
                    Type: type,
                    RoleStringIndex: rolesSid[i]);
            }
        }

        return new OsmRelation(
            Id: id.Value,
            Members: members,
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
