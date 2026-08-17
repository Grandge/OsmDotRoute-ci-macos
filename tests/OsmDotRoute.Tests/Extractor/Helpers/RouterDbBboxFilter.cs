using System.Collections.Generic;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;

namespace OsmDotRoute.Tests.Extractor.Helpers;

/// <summary>
/// Phase 1 <see cref="RouterDb"/> の頂点・辺を bbox で絞り込む統計ヘルパ。
/// </summary>
/// <remarks>
/// <para>
/// サブステップ 5.3 で Phase 1 RouterDb と Phase 2 <c>.odrg</c> を比較するために導入。
/// 公開 API は <see cref="RouterDb.GetStatistics"/> しか集計値を返さないため、
/// 本ヘルパでは <c>internal</c> な <c>RouterDb.Graph</c> を経由して逐次列挙する。
/// </para>
/// <para>
/// セマンティクス: 厳密 bbox。「両端点が bbox 内」のエッジのみカウント。
/// <see cref="ExtractPipeline"/> の bbox 拡張 (way が bbox に触れれば全ノード採用) とは
/// 異なるため、<c>.odrg</c> 側も同じ strict 絞込を適用して比較する。
/// </para>
/// </remarks>
internal static class RouterDbBboxFilter
{
    internal readonly record struct FilterStats(
        int VertexCount,
        int EdgeCount,
        Aabb FilteredBounds);

    /// <summary>
    /// RouterDb の頂点・辺を bbox で絞り込み、頂点数・辺数・実 bounds を返す。
    /// </summary>
    public static FilterStats Filter(RouterDb db, Aabb bbox)
    {
        var graph = db.Graph;

        // 頂点の bbox 内判定をビット配列で先に作る（エッジ走査で再利用）
        uint vCount = graph.VertexCount;
        var inBbox = new bool[vCount];
        int verticesIn = 0;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        double minLat = double.MaxValue, maxLat = double.MinValue;
        for (uint i = 0; i < vCount; i++)
        {
            var v = graph.GetVertex(i);
            if (bbox.Contains(v.Longitude, v.Latitude))
            {
                inBbox[i] = true;
                verticesIn++;
                if (v.Longitude < minLon) minLon = v.Longitude;
                if (v.Longitude > maxLon) maxLon = v.Longitude;
                if (v.Latitude < minLat) minLat = v.Latitude;
                if (v.Latitude > maxLat) maxLat = v.Latitude;
            }
        }

        // エッジ走査: 両端点が bbox 内のエッジのみカウント、EdgeId で dedup
        var seenEdges = new HashSet<uint>();
        for (uint vid = 0; vid < vCount; vid++)
        {
            if (!inBbox[vid]) continue;
            var en = graph.GetEdgeEnumerator(vid);
            while (en.MoveNext())
            {
                uint other = en.From == vid ? en.To : en.From;
                if (other >= vCount) continue;
                if (!inBbox[other]) continue;
                seenEdges.Add(en.EdgeId);
            }
        }

        var filteredBounds = verticesIn == 0
            ? default
            : new Aabb(minLon, minLat, maxLon, maxLat);

        return new FilterStats(verticesIn, seenEdges.Count, filteredBounds);
    }

    /// <summary>
    /// <c>.odrg</c> 側にも同じ strict bbox 絞込を適用するためのヘルパ。
    /// </summary>
    public static FilterStats FilterOdrg(OdrgReadResult odrg, Aabb bbox)
    {
        int vCount = odrg.Vertices.Length;
        var inBbox = new bool[vCount];
        int verticesIn = 0;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        double minLat = double.MaxValue, maxLat = double.MinValue;
        for (int i = 0; i < vCount; i++)
        {
            var v = odrg.Vertices[i];
            if (bbox.Contains(v.Longitude, v.Latitude))
            {
                inBbox[i] = true;
                verticesIn++;
                if (v.Longitude < minLon) minLon = v.Longitude;
                if (v.Longitude > maxLon) maxLon = v.Longitude;
                if (v.Latitude < minLat) minLat = v.Latitude;
                if (v.Latitude > maxLat) maxLat = v.Latitude;
            }
        }

        int edgesIn = 0;
        for (int e = 0; e < odrg.Edges.Length; e++)
        {
            uint from = odrg.Edges[e].FromVertexId;
            uint to = odrg.Edges[e].ToVertexId;
            if (from < vCount && to < vCount && inBbox[from] && inBbox[to])
                edgesIn++;
        }

        var filteredBounds = verticesIn == 0
            ? default
            : new Aabb(minLon, minLat, maxLon, maxLat);

        return new FilterStats(verticesIn, edgesIn, filteredBounds);
    }
}
