using System;
using System.Collections.Generic;
using System.IO;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の DenseNodes メッセージを解析する static パーサー。
/// </summary>
/// <remarks>
/// <para>DenseNodes フィールド (proto2 osmformat.proto)：</para>
/// <list type="bullet">
///   <item>field 1: id (repeated sint64, packed) — <b>delta-coded</b> zigzag</item>
///   <item>field 5: denseinfo (DenseInfo, optional) — Phase 2 ではスキップ</item>
///   <item>field 8: lat (repeated sint64, packed) — <b>delta-coded</b> zigzag</item>
///   <item>field 9: lon (repeated sint64, packed) — <b>delta-coded</b> zigzag</item>
///   <item>field 10: keys_vals (repeated int32, packed) — 0 区切りでノードごとの (key, val) ペア列</item>
/// </list>
/// <para>
/// 現代 OSM PBF（Geofabrik / Osmosis）はノードを DenseNodes で格納するのが標準。本パーサーが
/// PBF 抽出のメインパスとなる。
/// </para>
/// <para>
/// keys_vals の特別フォーマット：例 3 ノードで Node 0=2 tags / Node 1=1 tag / Node 2=tagless の場合：
/// <c>[k1, v1, k2, v2, 0,  k3, v3, 0,  0]</c>。
/// 全ノード tagless なら空配列でも可（仕様の省略形）。
/// </para>
/// </remarks>
internal static class DenseNodesParser
{
    /// <summary>DenseNodes メッセージを解析し、含まれる全 Node を配列で返す。</summary>
    public static OsmNode[] Parse(ReadOnlySpan<byte> denseNodesBytes, PrimitiveBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var reader = new ProtoReader(denseNodesBytes);
        long[] ids = Array.Empty<long>();
        long[] lats = Array.Empty<long>();
        long[] lons = Array.Empty<long>();
        int[] keysVals = Array.Empty<int>();

        while (reader.HasMore)
        {
            ProtoTag tag = reader.ReadTag();
            if (tag.IsEnd) break;

            switch (tag.FieldNumber)
            {
                case 1: // id (packed sint64, delta-coded)
                    EnsureWireType(tag, WireType.LengthDelimited, "DenseNodes.id");
                    ids = PackedReader.ReadPackedZigzag64(reader.ReadLengthDelimited());
                    break;
                case 5: // denseinfo (DenseInfo) — Phase 2 では未使用
                    EnsureWireType(tag, WireType.LengthDelimited, "DenseNodes.denseinfo");
                    reader.SkipField(tag.WireType);
                    break;
                case 8: // lat (packed sint64, delta-coded)
                    EnsureWireType(tag, WireType.LengthDelimited, "DenseNodes.lat");
                    lats = PackedReader.ReadPackedZigzag64(reader.ReadLengthDelimited());
                    break;
                case 9: // lon (packed sint64, delta-coded)
                    EnsureWireType(tag, WireType.LengthDelimited, "DenseNodes.lon");
                    lons = PackedReader.ReadPackedZigzag64(reader.ReadLengthDelimited());
                    break;
                case 10: // keys_vals (packed int32, 0-separated)
                    EnsureWireType(tag, WireType.LengthDelimited, "DenseNodes.keys_vals");
                    keysVals = PackedReader.ReadPackedUint32(reader.ReadLengthDelimited());
                    break;
                default:
                    reader.SkipField(tag.WireType);
                    break;
            }
        }

        if (ids.Length != lats.Length || ids.Length != lons.Length)
            throw new InvalidDataException(
                $"DenseNodes id/lat/lon length mismatch: id={ids.Length}, lat={lats.Length}, lon={lons.Length}.");

        int nodeCount = ids.Length;
        if (nodeCount == 0) return Array.Empty<OsmNode>();

        // delta デコード (in-place)
        long currentId = 0;
        long currentLat = 0;
        long currentLon = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            currentId += ids[i];
            ids[i] = currentId;
            currentLat += lats[i];
            lats[i] = currentLat;
            currentLon += lons[i];
            lons[i] = currentLon;
        }

        // keys_vals を各ノードに分配
        int[][] tagKeysPerNode = new int[nodeCount][];
        int[][] tagValsPerNode = new int[nodeCount][];
        DistributeTags(keysVals, nodeCount, tagKeysPerNode, tagValsPerNode);

        // 結果生成
        var result = new OsmNode[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            result[i] = new OsmNode(
                Id: ids[i],
                Lon: block.ToLon(lons[i]),
                Lat: block.ToLat(lats[i]),
                TagKeys: tagKeysPerNode[i],
                TagValues: tagValsPerNode[i]);
        }
        return result;
    }

    private static void DistributeTags(int[] keysVals, int nodeCount, int[][] tagKeysOut, int[][] tagValsOut)
    {
        if (keysVals.Length == 0)
        {
            // 全ノード tagless（省略形）
            for (int n = 0; n < nodeCount; n++)
            {
                tagKeysOut[n] = Array.Empty<int>();
                tagValsOut[n] = Array.Empty<int>();
            }
            return;
        }

        int kvIdx = 0;
        var keysList = new List<int>();
        var valsList = new List<int>();

        for (int n = 0; n < nodeCount; n++)
        {
            keysList.Clear();
            valsList.Clear();
            bool sawZero = false;

            while (kvIdx < keysVals.Length)
            {
                int k = keysVals[kvIdx];
                if (k == 0)
                {
                    kvIdx++;
                    sawZero = true;
                    break;
                }
                if (kvIdx + 1 >= keysVals.Length)
                    throw new InvalidDataException(
                        $"DenseNodes.keys_vals truncated mid-tag for node index {n}.");
                keysList.Add(k);
                valsList.Add(keysVals[kvIdx + 1]);
                kvIdx += 2;
            }

            if (!sawZero)
                throw new InvalidDataException(
                    $"DenseNodes.keys_vals missing 0-separator for node index {n}.");

            tagKeysOut[n] = keysList.Count == 0 ? Array.Empty<int>() : keysList.ToArray();
            tagValsOut[n] = valsList.Count == 0 ? Array.Empty<int>() : valsList.ToArray();
        }

        if (kvIdx != keysVals.Length)
            throw new InvalidDataException(
                $"DenseNodes.keys_vals has {keysVals.Length - kvIdx} trailing element(s) after node {nodeCount - 1}.");
    }

    private static void EnsureWireType(ProtoTag tag, WireType expected, string fieldName)
    {
        if (tag.WireType != expected)
            throw new InvalidDataException(
                $"{fieldName} expected wire-type {expected} but got {tag.WireType}.");
    }
}
