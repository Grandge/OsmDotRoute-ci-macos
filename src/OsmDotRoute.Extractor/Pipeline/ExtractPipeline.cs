using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsmDotRoute;
using OsmDotRoute.Pbf;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// <see cref="ExtractPipeline.Run"/> への入力。
/// </summary>
/// <param name="InputPbf">入力 OSM PBF ファイルパス。</param>
/// <param name="Bbox">抽出 bbox（WGS84 経緯度）。仕様書 §5.3.1 v0.1.3 で必須化済。</param>
/// <param name="Profiles">bake 対象プロファイル列（順序が表のプロファイル ID に対応）。</param>
internal sealed record ExtractPipelineOptions(
    string InputPbf,
    Aabb Bbox,
    IReadOnlyList<VehicleProfile> Profiles);

/// <summary>
/// <see cref="ExtractPipeline.Run"/> の出力。<see cref="OdrgWriter.Write"/> 呼出に必要な全データを含む。
/// </summary>
internal sealed record ExtractPipelineResult(
    GeoCoordinate[] Vertices,
    EdgeRecord[] Edges,
    Aabb[] EdgeAabbs,
    EdgeFlags[] EdgeFlags,
    StrRTree RTree,
    BakedProfileTable ProfileTable,
    Func<long, GeoCoordinate> NodeCoordLookup,
    Aabb FileBbox,
    Aabb RequestedBbox);

/// <summary>
/// OSM PBF → 抽出済みグラフ in-memory 構造体への変換パイプライン全体を組み立てる。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 3.9。仕様書 §5.3.3 の 3 パス走査戦略を実装し、3.3〜3.7 のサブステップ部品を統合する。
/// </para>
/// <para>処理フロー:</para>
/// <list type="number">
///   <item>Pass 1: bbox 内のノード ID を <see cref="HashSet{T}"/> に記録</item>
///   <item>Pass 2: 道路 way フィルタを通った way について、bbox 内ノードを 1 つでも含めば採用。
///       採用 way の全ノード (bbox 外含む) を「必要ノード集合」に追加</item>
///   <item>Pass 3: 必要ノードの座標を <see cref="Dictionary{TKey, TValue}"/> に展開</item>
///   <item>頂点正規化 (<see cref="VertexNormalizer"/>) → <see cref="VertexAssignment"/></item>
///   <item>エッジ生成 (<see cref="EdgeGenerator.SplitWay"/>) → エッジ列</item>
///   <item>各エッジに AABB / EdgeFlags を bake</item>
///   <item>STR R-tree 構築 (<see cref="StrRTreeBuilder.Build"/>) → エッジ再採番</item>
///   <item>全エッジテーブル (Edges / AABBs / Flags) に permutation 適用</item>
///   <item>プロファイル bake (<see cref="ProfileBaker.Build"/>) ※ permutation 適用後で実施</item>
/// </list>
/// </remarks>
internal static class ExtractPipeline
{
    public static ExtractPipelineResult Run(ExtractPipelineOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        if (string.IsNullOrWhiteSpace(opts.InputPbf))
            throw new ArgumentException("InputPbf が空", nameof(opts));
        if (opts.Profiles is null || opts.Profiles.Count == 0)
            throw new ArgumentException("Profiles が空", nameof(opts));
        if (!File.Exists(opts.InputPbf))
            throw new FileNotFoundException("PBF ファイルが見つかりません", opts.InputPbf);

        // ----- Pass 1: bbox 内ノード ID -----
        var bboxInternal = new HashSet<long>();
        Aabb bbox = opts.Bbox;
        using (var fs = File.OpenRead(opts.InputPbf))
        {
            PbfReader.Read(fs, onNode: (node, _) =>
            {
                if (bbox.Contains(node.Lon, node.Lat))
                    bboxInternal.Add(node.Id);
            });
        }

        // ----- Pass 2: way フィルタ + 必要ノード集合 -----
        var acceptedWays = new List<(OsmWay Way, OsmStringTable StringTable)>();
        var neededNodeIds = new HashSet<long>();
        using (var fs = File.OpenRead(opts.InputPbf))
        {
            PbfReader.Read(fs, onWay: (way, st) =>
            {
                if (!WayFilter.IsRoadWay(way, st)) return;

                bool intersectsBbox = false;
                long[] refs = way.NodeRefs;
                for (int i = 0; i < refs.Length; i++)
                {
                    if (bboxInternal.Contains(refs[i])) { intersectsBbox = true; break; }
                }
                if (!intersectsBbox) return;

                acceptedWays.Add((way, st));
                for (int i = 0; i < refs.Length; i++)
                    neededNodeIds.Add(refs[i]);
            });
        }

        // bboxInternal はもう不要、GC 開放
        bboxInternal.Clear();

        // ----- Pass 3: 必要ノード座標を展開 -----
        var nodeCoords = new Dictionary<long, GeoCoordinate>(neededNodeIds.Count);
        using (var fs = File.OpenRead(opts.InputPbf))
        {
            PbfReader.Read(fs, onNode: (node, _) =>
            {
                if (neededNodeIds.Contains(node.Id))
                    nodeCoords[node.Id] = new GeoCoordinate(node.Lat, node.Lon);
            });
        }

        // neededNodeIds も用済み
        neededNodeIds.Clear();

        // ----- 頂点正規化 -----
        var normalizer = new VertexNormalizer(initialCapacity: nodeCoords.Count);
        foreach (var (way, _) in acceptedWays)
            normalizer.AddWay(way.NodeRefs);
        VertexAssignment assignment = normalizer.Build();

        // 頂点座標配列を頂点 ID 順に構築
        var vertices = new GeoCoordinate[assignment.VertexCount];
        foreach (long osmId in assignment.VertexOsmIds)
        {
            if (!assignment.TryGetVertexId(osmId, out int vid))
                throw new InvalidOperationException($"頂点 ID 未割当: OSM {osmId}");
            if (!nodeCoords.TryGetValue(osmId, out var c))
                throw new InvalidOperationException($"必要ノード座標未ロード: OSM {osmId}");
            vertices[vid] = c;
        }

        // ----- エッジ生成 -----
        var edgesList = new List<EdgeRecord>(acceptedWays.Count * 2);
        foreach (var (way, st) in acceptedWays)
            edgesList.AddRange(EdgeGenerator.SplitWay(way, assignment, st));
        EdgeRecord[] edges = edgesList.ToArray();

        // ----- AABB / フラグ bake -----
        int edgeCount = edges.Length;
        var aabbs = new Aabb[edgeCount];
        var flags = new EdgeFlags[edgeCount];
        for (int i = 0; i < edgeCount; i++)
        {
            EdgeRecord e = edges[i];
            GeoCoordinate from = vertices[e.FromVertexId];
            GeoCoordinate to = vertices[e.ToVertexId];

            // shape 座標を一時的に展開（AABB 計算のためだけ、ホットパスではない）
            int shapeLen = e.ShapeNodeRefs.Length;
            var shapeCoords = shapeLen == 0
                ? Array.Empty<GeoCoordinate>()
                : new GeoCoordinate[shapeLen];
            for (int j = 0; j < shapeLen; j++)
                shapeCoords[j] = nodeCoords[e.ShapeNodeRefs[j]];

            aabbs[i] = EdgeAabbCalculator.Compute(from, to, shapeCoords);
            flags[i] = EdgeFlagsBaker.Bake(e);
        }

        // ----- R-tree 構築 + エッジ再採番 -----
        StrRTree rtree = StrRTreeBuilder.Build(aabbs);

        // ----- 全エッジテーブルに permutation 適用 -----
        var permEdges = new EdgeRecord[edgeCount];
        var permAabbs = new Aabb[edgeCount];
        var permFlags = new EdgeFlags[edgeCount];
        for (int newId = 0; newId < edgeCount; newId++)
        {
            int oldId = rtree.EdgePermutation[newId];
            permEdges[newId] = edges[oldId];
            permAabbs[newId] = aabbs[oldId];
            permFlags[newId] = flags[oldId];
        }

        // ----- プロファイル bake (permutation 適用後で実施) -----
        BakedProfileTable profileTable = ProfileBaker.Build(opts.Profiles, permEdges);

        // ----- 全頂点から実 bbox を計算 -----
        Aabb fileBbox = ComputeBbox(vertices, opts.Bbox);

        return new ExtractPipelineResult(
            Vertices: vertices,
            Edges: permEdges,
            EdgeAabbs: permAabbs,
            EdgeFlags: permFlags,
            RTree: rtree,
            ProfileTable: profileTable,
            NodeCoordLookup: id => nodeCoords[id],
            FileBbox: fileBbox,
            RequestedBbox: opts.Bbox);
    }

    private static Aabb ComputeBbox(GeoCoordinate[] vertices, Aabb fallback)
    {
        if (vertices.Length == 0) return fallback;

        double minLon = vertices[0].Longitude, maxLon = minLon;
        double minLat = vertices[0].Latitude, maxLat = minLat;
        for (int i = 1; i < vertices.Length; i++)
        {
            var v = vertices[i];
            if (v.Longitude < minLon) minLon = v.Longitude;
            if (v.Longitude > maxLon) maxLon = v.Longitude;
            if (v.Latitude < minLat) minLat = v.Latitude;
            if (v.Latitude > maxLat) maxLat = v.Latitude;
        }
        return new Aabb(minLon, minLat, maxLon, maxLat);
    }
}
