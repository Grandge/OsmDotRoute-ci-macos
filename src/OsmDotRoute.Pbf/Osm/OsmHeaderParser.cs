using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の HeaderBlock を解析する static パーサー。
/// </summary>
/// <remarks>
/// HeaderBlock のフィールド (proto2 osmformat.proto):
/// <list type="bullet">
///   <item>field 1: bbox (HeaderBBox, optional)</item>
///   <item>field 4: required_features (repeated string)</item>
///   <item>field 5: optional_features (repeated string)</item>
///   <item>field 16: writingprogram (string, optional)</item>
///   <item>field 17: source (string, optional)</item>
///   <item>field 32-34: replication 情報 (本実装ではスキップ)</item>
/// </list>
/// </remarks>
internal static class OsmHeaderParser
{
    /// <summary>OsmDotRoute.Pbf がサポートする required_features の集合 (大文字小文字を区別)。</summary>
    public static readonly IReadOnlySet<string> SupportedRequiredFeatures
        = new HashSet<string>(StringComparer.Ordinal)
        {
            "OsmSchema-V0.6",
            "DenseNodes",
        };

    /// <summary>ナノ度 → 度 への変換係数 (1e-9)。</summary>
    private const double NanodegreeToDegree = 1e-9;

    /// <summary>HeaderBlock のバイト列を解析する。</summary>
    /// <param name="headerBlockBytes">Blob 解凍後のペイロード (HeaderBlock protobuf)。</param>
    /// <returns>解析結果。必須フィールド (required_features 等) が不正なら <see cref="EnsureSupported"/> で別途検証。</returns>
    public static OsmHeader Parse(ReadOnlySpan<byte> headerBlockBytes)
    {
        var reader = new ProtoReader(headerBlockBytes);
        OsmBoundingBox? bbox = null;
        var required = new List<string>();
        var optional = new List<string>();
        string? writingProgram = null;
        string? source = null;

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // bbox (HeaderBBox, optional)
                    EnsureWireType(tag, WireType.LengthDelimited, "HeaderBlock.bbox");
                    bbox = ParseBoundingBox(reader.ReadLengthDelimited());
                    break;
                case 4: // required_features (repeated string)
                    EnsureWireType(tag, WireType.LengthDelimited, "HeaderBlock.required_features");
                    required.Add(Encoding.UTF8.GetString(reader.ReadLengthDelimited()));
                    break;
                case 5: // optional_features (repeated string)
                    EnsureWireType(tag, WireType.LengthDelimited, "HeaderBlock.optional_features");
                    optional.Add(Encoding.UTF8.GetString(reader.ReadLengthDelimited()));
                    break;
                case 16: // writingprogram (string, optional)
                    EnsureWireType(tag, WireType.LengthDelimited, "HeaderBlock.writingprogram");
                    writingProgram = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                case 17: // source (string, optional)
                    EnsureWireType(tag, WireType.LengthDelimited, "HeaderBlock.source");
                    source = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                default:
                    // replication 情報 (field 32-34) と未知フィールドはスキップ
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        return new OsmHeader(
            BoundingBox: bbox,
            RequiredFeatures: required,
            OptionalFeatures: optional,
            WritingProgram: writingProgram,
            Source: source);
    }

    /// <summary>
    /// <paramref name="header"/> の required_features がすべて <see cref="SupportedRequiredFeatures"/> に含まれることを保証する。
    /// 未サポートの機能が見つかれば <see cref="NotSupportedException"/>。
    /// </summary>
    public static void EnsureSupported(OsmHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        foreach (string feature in header.RequiredFeatures)
        {
            if (!SupportedRequiredFeatures.Contains(feature))
                throw new NotSupportedException(
                    $"PBF file declares unsupported required_feature '{feature}'. " +
                    $"OsmDotRoute.Pbf supports: {string.Join(", ", SupportedRequiredFeatures)}.");
        }
    }

    private static OsmBoundingBox ParseBoundingBox(ReadOnlySpan<byte> bboxBytes)
    {
        var reader = new ProtoReader(bboxBytes);
        long? left = null;
        long? right = null;
        long? top = null;
        long? bottom = null;

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // left (sint64, required)
                    EnsureWireType(tag, WireType.Varint, "HeaderBBox.left");
                    left = reader.ReadZigzag64();
                    break;
                case 2: // right (sint64, required)
                    EnsureWireType(tag, WireType.Varint, "HeaderBBox.right");
                    right = reader.ReadZigzag64();
                    break;
                case 3: // top (sint64, required)
                    EnsureWireType(tag, WireType.Varint, "HeaderBBox.top");
                    top = reader.ReadZigzag64();
                    break;
                case 4: // bottom (sint64, required)
                    EnsureWireType(tag, WireType.Varint, "HeaderBBox.bottom");
                    bottom = reader.ReadZigzag64();
                    break;
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        if (left is null || right is null || top is null || bottom is null)
            throw new InvalidDataException(
                "HeaderBBox is missing one of required fields (left/right/top/bottom).");

        return new OsmBoundingBox(
            MinLon: left.Value * NanodegreeToDegree,
            MinLat: bottom.Value * NanodegreeToDegree,
            MaxLon: right.Value * NanodegreeToDegree,
            MaxLat: top.Value * NanodegreeToDegree);
    }

    private static void EnsureWireType(ProtoTag tag, WireType expected, string fieldName)
    {
        if (tag.WireType != expected)
            throw new InvalidDataException(
                $"{fieldName} expected wire-type {expected} but got {tag.WireType}.");
    }
}
