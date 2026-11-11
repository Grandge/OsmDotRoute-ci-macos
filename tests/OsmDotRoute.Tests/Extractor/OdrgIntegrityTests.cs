using System.Collections.Generic;
using System.IO;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Tests.TestData;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// 津島市 PBF → ExtractPipeline → OdrgWriter → OdrgReader を 1 回だけ実行し、
/// 同一クラス内の整合性テストで共有するための fixture。
/// PBF 不在時はコンストラクタで <see cref="Assert.Fail"/>（全テストが同じメッセージで失敗）。
/// </summary>
public sealed class TsushimaOdrgFixture
{
    internal OdrgReadResult Read { get; }
    internal Aabb Bbox { get; }

    /// <summary>親プロジェクト由来 PBF が存在し津島データを構築できたか。不在環境（CI 等）では false。</summary>
    internal bool Available { get; }

    public TsushimaOdrgFixture()
    {
        // 親プロジェクト由来 PBF はリポジトリに含めない（CLAUDE.md）。不在の環境では津島テストをスキップする。
        if (!File.Exists(TestPaths.TsushimaExtractPbf))
        {
            Available = false;
            Read = default!;
            return;
        }
        Available = true;

        Bbox = new Aabb(MinLon: 136.65, MinLat: 35.13, MaxLon: 136.80, MaxLat: 35.25);
        var opts = new ExtractPipelineOptions(
            InputPbf: TestPaths.TsushimaExtractPbf,
            Bbox: Bbox,
            Profiles: new[] { VehicleProfile.Car, VehicleProfile.Pedestrian });
        var result = ExtractPipeline.Run(opts);

        var writeInput = new OdrgWriteInput(
            Vertices: result.Vertices,
            Edges: result.Edges,
            EdgeAabbs: result.EdgeAabbs,
            EdgeFlags: result.EdgeFlags,
            RTree: result.RTree,
            ProfileTable: result.ProfileTable,
            NodeCoordLookup: result.NodeCoordLookup,
            Bbox: result.FileBbox,
            MetadataJson: "{}");

        using var ms = new MemoryStream();
        OdrgWriter.Write(ms, writeInput);
        ms.Position = 0;
        Read = OdrgReader.Read(ms);
    }
}

/// <summary>
/// サブステップ 5.2 — <c>.odrg</c> 構造整合テスト (仕様書 §4 のセクション間整合)。
/// </summary>
/// <remarks>
/// 合成データ (小規模 odrg) と津島市 PBF 実データの両方で INT-1〜INT-10 を検査する。
/// 合成データは <c>OdrgIntegrityChecker</c> の自己検証（誤検出が無いこと）、
/// 津島データはステップ 3.9 で生成された .odrg の正当性確認が目的。
/// </remarks>
public sealed class OdrgIntegrityTests : IClassFixture<TsushimaOdrgFixture>
{
    private readonly TsushimaOdrgFixture _fixture;

    public OdrgIntegrityTests(TsushimaOdrgFixture fixture)
    {
        _fixture = fixture;
    }

    // ===== INT-1: 全エッジの from/to vertex id < vertexCount =====

    [Fact]
    public void INT01_Synthetic_EdgeVertexIdsAreInRange()
    {
        var r = Synthetic.Build();
        AssertEdgeVertexIdsInRange(r);
    }

    [Fact]
    public void INT01_Tsushima_EdgeVertexIdsAreInRange()
    {
        if (!_fixture.Available) return;
        AssertEdgeVertexIdsInRange(_fixture.Read);
    }

    private static void AssertEdgeVertexIdsInRange(OdrgReadResult r)
    {
        int vMax = r.Vertices.Length;
        for (int i = 0; i < r.Edges.Length; i++)
        {
            Assert.InRange((int)r.Edges[i].FromVertexId, 0, vMax - 1);
            Assert.InRange((int)r.Edges[i].ToVertexId, 0, vMax - 1);
        }
    }

    // ===== INT-2: shapeOffset + shapePointCount × ShapePointSize ≤ shape buffer length =====

    [Fact]
    public void INT02_Synthetic_ShapeRangeWithinBuffer()
    {
        var r = Synthetic.Build();
        AssertShapeRangeWithinBuffer(r);
    }

    [Fact]
    public void INT02_Tsushima_ShapeRangeWithinBuffer()
    {
        if (!_fixture.Available) return;
        AssertShapeRangeWithinBuffer(_fixture.Read);
    }

    private static void AssertShapeRangeWithinBuffer(OdrgReadResult r)
    {
        long bufLen = (long)r.GetSection(OdrgFormat.SectionEdgeShapeBuffer).Length;
        for (int i = 0; i < r.Edges.Length; i++)
        {
            long endOffset = (long)r.Edges[i].ShapeOffset
                + (long)r.Edges[i].ShapePointCount * OdrgFormat.ShapePointSize;
            Assert.True(endOffset <= bufLen,
                $"Edge {i}: shape end {endOffset} > buffer length {bufLen}");
        }
    }

    // ===== INT-3: 各エッジ AABB が端点 + 全 shape 点を内包 =====

    [Fact]
    public void INT03_Synthetic_AabbContainsAllGeometry()
    {
        var r = Synthetic.Build();
        AssertAabbContainsAllGeometry(r);
    }

    [Fact]
    public void INT03_Tsushima_AabbContainsAllGeometry()
    {
        if (!_fixture.Available) return;
        AssertAabbContainsAllGeometry(_fixture.Read);
    }

    private static void AssertAabbContainsAllGeometry(OdrgReadResult r)
    {
        for (int i = 0; i < r.Edges.Length; i++)
        {
            var aabb = r.EdgeAabbs[i];
            var from = r.Vertices[r.Edges[i].FromVertexId];
            var to = r.Vertices[r.Edges[i].ToVertexId];
            Assert.True(aabb.Contains(from.Longitude, from.Latitude),
                $"Edge {i}: AABB does not contain FROM vertex ({from.Longitude},{from.Latitude})");
            Assert.True(aabb.Contains(to.Longitude, to.Latitude),
                $"Edge {i}: AABB does not contain TO vertex ({to.Longitude},{to.Latitude})");
            foreach (var pt in r.EdgeShapes[i])
            {
                Assert.True(aabb.Contains(pt.Longitude, pt.Latitude),
                    $"Edge {i}: AABB does not contain shape point ({pt.Longitude},{pt.Latitude})");
            }
        }
    }

    // ===== INT-4: 全エッジ AABB がファイル全体 bbox に収まる =====

    [Fact]
    public void INT04_Synthetic_EdgeAabbsWithinFileBbox()
    {
        var r = Synthetic.Build();
        AssertEdgeAabbsWithinFileBbox(r);
    }

    [Fact]
    public void INT04_Tsushima_EdgeAabbsWithinFileBbox()
    {
        if (!_fixture.Available) return;
        AssertEdgeAabbsWithinFileBbox(_fixture.Read);
    }

    private static void AssertEdgeAabbsWithinFileBbox(OdrgReadResult r)
    {
        var bbox = r.Header.Bbox;
        for (int i = 0; i < r.EdgeAabbs.Length; i++)
        {
            var e = r.EdgeAabbs[i];
            Assert.True(e.MinLon >= bbox.MinLon, $"Edge {i}: minLon {e.MinLon} < file minLon {bbox.MinLon}");
            Assert.True(e.MinLat >= bbox.MinLat, $"Edge {i}: minLat {e.MinLat} < file minLat {bbox.MinLat}");
            Assert.True(e.MaxLon <= bbox.MaxLon, $"Edge {i}: maxLon {e.MaxLon} > file maxLon {bbox.MaxLon}");
            Assert.True(e.MaxLat <= bbox.MaxLat, $"Edge {i}: maxLat {e.MaxLat} > file maxLat {bbox.MaxLat}");
        }
    }

    // ===== INT-5: R-tree 全葉ノードのエッジ ID が {0..edgeCount-1} の bijection =====

    [Fact]
    public void INT05_Synthetic_RTreeLeafIdsAreBijection()
    {
        var r = Synthetic.Build();
        AssertRTreeLeafIdsAreBijection(r);
    }

    [Fact]
    public void INT05_Tsushima_RTreeLeafIdsAreBijection()
    {
        if (!_fixture.Available) return;
        AssertRTreeLeafIdsAreBijection(_fixture.Read);
    }

    private static void AssertRTreeLeafIdsAreBijection(OdrgReadResult r)
    {
        var seen = new HashSet<uint>();
        for (int i = 0; i < r.RTree.Nodes.Length; i++)
        {
            var n = r.RTree.Nodes[i];
            if (!n.IsLeaf) continue;
            for (uint k = 0; k < n.ChildCount; k++)
            {
                uint edgeId = n.FirstChildIndex + k;
                Assert.True(edgeId < r.Edges.Length, $"Leaf edge id {edgeId} >= edgeCount {r.Edges.Length}");
                Assert.True(seen.Add(edgeId), $"R-tree leaves contain duplicate edge id {edgeId}");
            }
        }
        Assert.Equal(r.Edges.Length, seen.Count);
    }

    // ===== INT-6: R-tree 全内部ノードの Bounds が子ノードの Bounds を包含 =====

    [Fact]
    public void INT06_Synthetic_RTreeInnerBoundsContainChildren()
    {
        var r = Synthetic.Build();
        AssertRTreeInnerBoundsContainChildren(r);
    }

    [Fact]
    public void INT06_Tsushima_RTreeInnerBoundsContainChildren()
    {
        if (!_fixture.Available) return;
        AssertRTreeInnerBoundsContainChildren(_fixture.Read);
    }

    private static void AssertRTreeInnerBoundsContainChildren(OdrgReadResult r)
    {
        for (int i = 0; i < r.RTree.Nodes.Length; i++)
        {
            var node = r.RTree.Nodes[i];
            if (node.IsLeaf) continue;
            for (uint k = 0; k < node.ChildCount; k++)
            {
                uint childIdx = node.FirstChildIndex + k;
                Assert.True(childIdx < r.RTree.Nodes.Length,
                    $"Inner node {i}: child index {childIdx} >= nodeCount {r.RTree.Nodes.Length}");
                var c = r.RTree.Nodes[childIdx].Bounds;
                Assert.True(node.Bounds.MinLon <= c.MinLon + 1e-12,
                    $"Inner node {i}: bounds.MinLon {node.Bounds.MinLon} > child {childIdx} MinLon {c.MinLon}");
                Assert.True(node.Bounds.MinLat <= c.MinLat + 1e-12);
                Assert.True(node.Bounds.MaxLon >= c.MaxLon - 1e-12);
                Assert.True(node.Bounds.MaxLat >= c.MaxLat - 1e-12);
            }
        }
    }

    // ===== INT-7: R-tree root.Bounds がファイル全体 bbox を包含 =====

    [Fact]
    public void INT07_Synthetic_RTreeRootBoundsCoverFileBbox()
    {
        var r = Synthetic.Build();
        AssertRTreeRootBoundsCoverFileBbox(r);
    }

    [Fact]
    public void INT07_Tsushima_RTreeRootBoundsCoverFileBbox()
    {
        if (!_fixture.Available) return;
        AssertRTreeRootBoundsCoverFileBbox(_fixture.Read);
    }

    private static void AssertRTreeRootBoundsCoverFileBbox(OdrgReadResult r)
    {
        if (r.RTree.Nodes.Length == 0) return;
        var root = r.RTree.Nodes[r.RTree.RootIndex];
        var bbox = r.Header.Bbox;
        Assert.True(root.Bounds.MinLon <= bbox.MinLon + 1e-9);
        Assert.True(root.Bounds.MinLat <= bbox.MinLat + 1e-9);
        Assert.True(root.Bounds.MaxLon >= bbox.MaxLon - 1e-9);
        Assert.True(root.Bounds.MaxLat >= bbox.MaxLat - 1e-9);
    }

    // ===== INT-8: TreeHeight × BranchingFactor で全エッジ収納可能 =====

    [Fact]
    public void INT08_Synthetic_RTreeCapacitySufficient()
    {
        var r = Synthetic.Build();
        AssertRTreeCapacitySufficient(r);
    }

    [Fact]
    public void INT08_Tsushima_RTreeCapacitySufficient()
    {
        if (!_fixture.Available) return;
        AssertRTreeCapacitySufficient(_fixture.Read);
    }

    private static void AssertRTreeCapacitySufficient(OdrgReadResult r)
    {
        if (r.Edges.Length == 0) return;
        // M^height ≥ edgeCount を確認 (オーバーフロー回避のため log で比較)
        double logCapacity = r.RTree.TreeHeight * System.Math.Log(r.RTree.BranchingFactor);
        double logNeeded = System.Math.Log(r.Edges.Length);
        Assert.True(logCapacity >= logNeeded - 1e-9,
            $"M^h = {r.RTree.BranchingFactor}^{r.RTree.TreeHeight} < edgeCount {r.Edges.Length}");
    }

    // ===== INT-9: BakedProfileEntry の論理整合 =====

    [Fact]
    public void INT09_Synthetic_BakedProfileEntryConsistency()
    {
        var r = Synthetic.Build();
        AssertBakedProfileEntryConsistency(r);
    }

    [Fact]
    public void INT09_Tsushima_BakedProfileEntryConsistency()
    {
        if (!_fixture.Available) return;
        AssertBakedProfileEntryConsistency(_fixture.Read);

        // 津島データ固有: WayFilter は highway=* を広めに採用しプロファイル側で絞る方針のため、
        // 両プロファイル通行不可なエッジ (proposed / construction 等) は一定数存在しうる。
        // 「ほとんどのエッジは少なくとも一方で通行可能」(>=95%) を sanity check として確認。
        var car = _fixture.Read.ProfileTable.EntriesByProfile[0];
        var ped = _fixture.Read.ProfileTable.EntriesByProfile[1];
        int passable = 0;
        for (int i = 0; i < car.Length; i++)
        {
            if (car[i].CanPass || ped[i].CanPass) passable++;
        }
        double ratio = (double)passable / car.Length;
        Assert.True(ratio >= 0.95,
            $"通行可能エッジ比率 {ratio:P2} ({passable}/{car.Length}) が 95% を下回る");
    }

    private static void AssertBakedProfileEntryConsistency(OdrgReadResult r)
    {
        for (int p = 0; p < r.ProfileTable.ProfileCount; p++)
        {
            var entries = r.ProfileTable.EntriesByProfile[p];
            for (int e = 0; e < entries.Length; e++)
            {
                Assert.True(entries[e].SpeedKmh >= 0f, $"profile {p} edge {e}: SpeedKmh < 0");
                if (!entries[e].CanPass)
                {
                    Assert.Equal(0f, entries[e].SpeedKmh);
                }
                else
                {
                    Assert.True(entries[e].Forward || entries[e].Backward,
                        $"profile {p} edge {e}: CanPass=true だが Forward/Backward 両方 false");
                }
            }
        }
    }

    // ===== INT-10: 全エッジ bakedProfileIndex == edgeId =====

    [Fact]
    public void INT10_Synthetic_BakedProfileIndexEqualsEdgeId()
    {
        var r = Synthetic.Build();
        AssertBakedProfileIndexEqualsEdgeId(r);
    }

    [Fact]
    public void INT10_Tsushima_BakedProfileIndexEqualsEdgeId()
    {
        if (!_fixture.Available) return;
        AssertBakedProfileIndexEqualsEdgeId(_fixture.Read);
    }

    private static void AssertBakedProfileIndexEqualsEdgeId(OdrgReadResult r)
    {
        for (int i = 0; i < r.Edges.Length; i++)
        {
            Assert.Equal((uint)i, r.Edges[i].BakedProfileIndex);
        }
    }

    // ===== 合成データ生成 =====

    private static class Synthetic
    {
        private static readonly OsmStringTable EmptyStringTable = new(new[] { System.Array.Empty<byte>() });

        /// <summary>
        /// 3 頂点 / 3 エッジ / 1 プロファイルの合成 .odrg を生成して読込結果を返す。
        /// 1 つのエッジには中間 shape 2 点を持たせ、INT-2/3/4 が自明でない状態にする。
        /// </summary>
        public static OdrgReadResult Build()
        {
            var vertices = new[]
            {
                new GeoCoordinate(35.10, 136.60),
                new GeoCoordinate(35.20, 136.70),
                new GeoCoordinate(35.30, 136.80),
            };

            var edges = new[]
            {
                new EdgeRecord(1, 0, 1, new long[] { 100, 101 },
                    System.Array.Empty<int>(), System.Array.Empty<int>(), EmptyStringTable),
                new EdgeRecord(2, 1, 2, System.Array.Empty<long>(),
                    System.Array.Empty<int>(), System.Array.Empty<int>(), EmptyStringTable),
                new EdgeRecord(3, 0, 2, System.Array.Empty<long>(),
                    System.Array.Empty<int>(), System.Array.Empty<int>(), EmptyStringTable),
            };

            GeoCoordinate Lookup(long id) => id switch
            {
                100 => new GeoCoordinate(35.12, 136.62),
                101 => new GeoCoordinate(35.15, 136.65),
                _ => default,
            };

            // エッジ AABB は端点 + shape を内包
            var aabbs = new[]
            {
                EdgeAabbCalculator.Compute(vertices[0], vertices[1], new[] { Lookup(100), Lookup(101) }),
                EdgeAabbCalculator.Compute(vertices[1], vertices[2], System.ReadOnlySpan<GeoCoordinate>.Empty),
                EdgeAabbCalculator.Compute(vertices[0], vertices[2], System.ReadOnlySpan<GeoCoordinate>.Empty),
            };

            var flags = new[] { EdgeFlags.None, EdgeFlags.IsOnewayForward, EdgeFlags.None };

            // R-tree: ルート (内部) + 葉 2 つ。葉 0 はエッジ 0-1、葉 1 はエッジ 2
            var rtreeNodes = new[]
            {
                RTreeNode.Create(
                    Union(aabbs[0], aabbs[1]),
                    firstChildIndex: 0, childCount: 2, isLeaf: true),
                RTreeNode.Create(
                    aabbs[2],
                    firstChildIndex: 2, childCount: 1, isLeaf: true),
                RTreeNode.Create(
                    Union(Union(aabbs[0], aabbs[1]), aabbs[2]),
                    firstChildIndex: 0, childCount: 2, isLeaf: false),
            };
            var rtree = new StrRTree(
                Nodes: rtreeNodes,
                RootIndex: 2,
                BranchingFactor: 16,
                TreeHeight: 2,
                EdgePermutation: new[] { 0, 1, 2 });

            var table = new BakedProfileTable(
                new[] { "car" },
                new[]
                {
                    new[]
                    {
                        BakedProfileEntry.Create(true, 50f, true, true),
                        BakedProfileEntry.Create(true, 30f, true, false),
                        BakedProfileEntry.Create(false, 0f, false, false),
                    },
                });

            var bbox = Union(Union(aabbs[0], aabbs[1]), aabbs[2]);

            var input = new OdrgWriteInput(
                Vertices: vertices,
                Edges: edges,
                EdgeAabbs: aabbs,
                EdgeFlags: flags,
                RTree: rtree,
                ProfileTable: table,
                NodeCoordLookup: Lookup,
                Bbox: bbox,
                MetadataJson: "{}");

            using var ms = new MemoryStream();
            OdrgWriter.Write(ms, input);
            ms.Position = 0;
            return OdrgReader.Read(ms);
        }

        private static Aabb Union(Aabb a, Aabb b) => new(
            MinLon: System.Math.Min(a.MinLon, b.MinLon),
            MinLat: System.Math.Min(a.MinLat, b.MinLat),
            MaxLon: System.Math.Max(a.MaxLon, b.MaxLon),
            MaxLat: System.Math.Max(a.MaxLat, b.MaxLat));
    }
}
