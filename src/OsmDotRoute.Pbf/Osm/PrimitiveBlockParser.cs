using System;
using System.Collections.Generic;
using System.IO;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の PrimitiveBlock を解析する static パーサー（ステップ 2.5 ではエンベロープのみ）。
/// </summary>
/// <remarks>
/// <para>解析対象（proto2 osmformat.proto）：</para>
/// <list type="bullet">
///   <item>field 1: stringtable (required, StringTable)</item>
///   <item>field 2: primitivegroup (repeated, PrimitiveGroup) — 本ステップではスキップ、2.6 以降で実装</item>
///   <item>field 17: granularity (optional int32, default 100)</item>
///   <item>field 18: date_granularity (optional int32, default 1000)</item>
///   <item>field 19: lat_offset (optional int64, default 0)</item>
///   <item>field 20: lon_offset (optional int64, default 0)</item>
/// </list>
/// </remarks>
internal static class PrimitiveBlockParser
{
    /// <summary>PBF 仕様の granularity デフォルト値（100 nanodegree）。</summary>
    public const int DefaultGranularity = 100;

    /// <summary>PBF 仕様の date_granularity デフォルト値（1000 ms）。</summary>
    public const int DefaultDateGranularity = 1000;

    /// <summary>PrimitiveBlock のバイト列を解析する（PrimitiveGroup はスキップ）。</summary>
    public static PrimitiveBlock Parse(ReadOnlySpan<byte> blockBytes)
    {
        var reader = new ProtoReader(blockBytes);
        OsmStringTable? stringTable = null;
        int granularity = DefaultGranularity;
        long latOffset = 0;
        long lonOffset = 0;
        int dateGranularity = DefaultDateGranularity;

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // stringtable (required, StringTable)
                    EnsureWireType(tag, WireType.LengthDelimited, "PrimitiveBlock.stringtable");
                    stringTable = ParseStringTable(reader.ReadLengthDelimited());
                    break;
                case 2: // primitivegroup (repeated, PrimitiveGroup) — Phase 2.5 ではスキップ
                    EnsureWireType(tag, WireType.LengthDelimited, "PrimitiveBlock.primitivegroup");
                    reader.SkipField(tag.WireType);
                    break;
                case 17: // granularity (optional int32)
                    EnsureWireType(tag, WireType.Varint, "PrimitiveBlock.granularity");
                    granularity = checked((int)reader.ReadVarint32());
                    break;
                case 18: // date_granularity (optional int32)
                    EnsureWireType(tag, WireType.Varint, "PrimitiveBlock.date_granularity");
                    dateGranularity = checked((int)reader.ReadVarint32());
                    break;
                case 19: // lat_offset (optional int64, 負値あり)
                    EnsureWireType(tag, WireType.Varint, "PrimitiveBlock.lat_offset");
                    latOffset = unchecked((long)reader.ReadVarint64());
                    break;
                case 20: // lon_offset (optional int64, 負値あり)
                    EnsureWireType(tag, WireType.Varint, "PrimitiveBlock.lon_offset");
                    lonOffset = unchecked((long)reader.ReadVarint64());
                    break;
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        if (stringTable is null)
            throw new InvalidDataException(
                "PrimitiveBlock is missing required field 'stringtable'.");

        if (granularity <= 0)
            throw new InvalidDataException(
                $"PrimitiveBlock.granularity must be positive, got {granularity}.");

        return new PrimitiveBlock(stringTable, granularity, latOffset, lonOffset, dateGranularity);
    }

    /// <summary>StringTable サブメッセージを解析する。</summary>
    public static OsmStringTable ParseStringTable(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtoReader(bytes);
        var items = new List<byte[]>();

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // s: repeated bytes
                    EnsureWireType(tag, WireType.LengthDelimited, "StringTable.s");
                    items.Add(reader.ReadLengthDelimited().ToArray());
                    break;
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        return new OsmStringTable(items.ToArray());
    }

    private static void EnsureWireType(ProtoTag tag, WireType expected, string fieldName)
    {
        if (tag.WireType != expected)
            throw new InvalidDataException(
                $"{fieldName} expected wire-type {expected} but got {tag.WireType}.");
    }
}
