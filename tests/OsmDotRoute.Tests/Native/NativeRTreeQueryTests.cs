using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Native;
using Xunit;

namespace OsmDotRoute.Tests.Native;

/// <summary>
/// Phase 3 ステップ 3A.4 — <see cref="NativeRTreeQuery"/> の正確性検証（8 件）。
/// 計画書 §4.4-C のテスト構成（Q6 確定）。Brute-force 突合は <see cref="OdrgReader"/> 真値の
/// EDGE_AABB セクション全走査で行い、Itinero との突合は 3A.5/3A.6 担当。
/// </summary>
public sealed class NativeRTreeQueryTests : IClassFixture<NativeAndOdrgReaderFixture>
{
    private readonly NativeAndOdrgReaderFixture _fixture;

    public NativeRTreeQueryTests(NativeAndOdrgReaderFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Constructor_LoadsTsushimaOdrg_ExposesRTreeAccessors()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;

        Assert.True(graph.RTreeNodeCount > 0);
        Assert.Equal(truth.RTree.NodeCount, graph.RTreeNodeCount);
        Assert.Equal(truth.RTree.RootIndex, graph.RTreeRootIndex);
        Assert.Equal(truth.RTree.BranchingFactor, graph.RTreeBranchingFactor);
        Assert.Equal(truth.RTree.TreeHeight, graph.RTreeHeight);
        Assert.True(graph.RTreeRootIndex < graph.RTreeNodeCount);

        var nodes = graph.GetRTreeNodes();
        Assert.Equal((int)graph.RTreeNodeCount, nodes.Length);
    }

    [Fact]
    public void Query_FullBoundsBbox_ReturnsAllEdgeIds()
    {
        var graph = _fixture.Graph;
        var bbox = graph.GetRTreeNodes()[(int)graph.RTreeRootIndex].Bbox;

        var buffer = new uint[graph.EdgeCount];
        int hits = NativeRTreeQuery.Query(
            graph.GetRTreeNodes(), graph.RTreeRootIndex, graph.GetEdgeAabbs(), bbox, buffer);

        Assert.Equal((int)graph.EdgeCount, hits);

        var unique = new HashSet<uint>(buffer.AsSpan(0, hits).ToArray());
        Assert.Equal((int)graph.EdgeCount, unique.Count);
    }

    [Fact]
    public void Query_OutOfBoundsBbox_ReturnsZeroHits()
    {
        var graph = _fixture.Graph;
        // 津島市 (≒ N35) から十分離れた高緯度 bbox
        var farBbox = new OdrgBbox(MinLon: 0.0, MinLat: 89.0, MaxLon: 1.0, MaxLat: 90.0);

        var buffer = new uint[16];
        int hits = NativeRTreeQuery.Query(
            graph.GetRTreeNodes(), graph.RTreeRootIndex, graph.GetEdgeAabbs(), farBbox, buffer);

        Assert.Equal(0, hits);
    }

    [Fact]
    public void Query_FiftyRandomBboxes_MatchesBruteForceAabb()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;
        var rootBox = graph.GetRTreeNodes()[(int)graph.RTreeRootIndex].Bbox;

        var rng = new Random(42);
        var buffer = new uint[graph.EdgeCount];

        for (int trial = 0; trial < 50; trial++)
        {
            // ルート bbox の 1〜30% サイズのランダム矩形
            double widthFrac = 0.01 + rng.NextDouble() * 0.29;
            double heightFrac = 0.01 + rng.NextDouble() * 0.29;
            double rootLonSpan = rootBox.MaxLon - rootBox.MinLon;
            double rootLatSpan = rootBox.MaxLat - rootBox.MinLat;
            double w = rootLonSpan * widthFrac;
            double h = rootLatSpan * heightFrac;
            double minLon = rootBox.MinLon + rng.NextDouble() * (rootLonSpan - w);
            double minLat = rootBox.MinLat + rng.NextDouble() * (rootLatSpan - h);
            var qbox = new OdrgBbox(minLon, minLat, minLon + w, minLat + h);

            int hits = NativeRTreeQuery.Query(
                graph.GetRTreeNodes(), graph.RTreeRootIndex, graph.GetEdgeAabbs(), qbox, buffer);
            var nativeSet = new HashSet<uint>(buffer.AsSpan(0, hits).ToArray());

            // Brute-force: OdrgReader 真値の EDGE_AABB 全走査
            var bruteSet = new HashSet<uint>();
            for (int e = 0; e < truth.EdgeAabbs.Length; e++)
            {
                if (BboxIntersects(truth.EdgeAabbs[e], qbox))
                {
                    bruteSet.Add((uint)e);
                }
            }

            Assert.True(
                nativeSet.SetEquals(bruteSet),
                $"trial={trial} qbox={qbox} native={nativeSet.Count} brute={bruteSet.Count}");
        }
    }

    [Fact]
    public void Query_BufferOverrun_ReturnsTotalHitsAndWritesUpToBufferLength()
    {
        var graph = _fixture.Graph;
        var bbox = graph.GetRTreeNodes()[(int)graph.RTreeRootIndex].Bbox;

        Span<uint> smallBuffer = stackalloc uint[10];
        int hits = NativeRTreeQuery.Query(
            graph.GetRTreeNodes(), graph.RTreeRootIndex, graph.GetEdgeAabbs(), bbox, smallBuffer);

        Assert.Equal((int)graph.EdgeCount, hits);
        Assert.True(hits > smallBuffer.Length, "overrun ケースの前提");
        for (int i = 0; i < smallBuffer.Length; i++)
        {
            Assert.True(
                smallBuffer[i] < graph.EdgeCount,
                $"buffer[{i}] = {smallBuffer[i]} はエッジ ID 範囲外");
        }
    }

    [Fact]
    public void Nearest_K1_MatchesBruteForceMinimumDistance()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;
        var rootBox = graph.GetRTreeNodes()[(int)graph.RTreeRootIndex].Bbox;

        var rng = new Random(123);
        double lon = rootBox.MinLon + rng.NextDouble() * (rootBox.MaxLon - rootBox.MinLon);
        double lat = rootBox.MinLat + rng.NextDouble() * (rootBox.MaxLat - rootBox.MinLat);

        var buffer = new uint[1];
        int written = NativeRTreeQuery.Nearest(
            graph.GetRTreeNodes(), graph.RTreeRootIndex, graph.GetEdgeAabbs(),
            lon, lat, 1, buffer);

        Assert.Equal(1, written);

        // Brute-force: 全 EDGE_AABB に対し点-AABB 最小距離² を計算し最小エッジを抽出
        double bestDistSq = double.PositiveInfinity;
        uint bestId = uint.MaxValue;
        for (int e = 0; e < truth.EdgeAabbs.Length; e++)
        {
            double d = PointBboxDistanceSq(truth.EdgeAabbs[e], lon, lat);
            if (d < bestDistSq)
            {
                bestDistSq = d;
                bestId = (uint)e;
            }
        }

        Assert.Equal(bestId, buffer[0]);
    }

    [Fact]
    public void Nearest_K10_MatchesBruteForceTopTen()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;
        var rootBox = graph.GetRTreeNodes()[(int)graph.RTreeRootIndex].Bbox;

        var rng = new Random(456);
        double lon = rootBox.MinLon + rng.NextDouble() * (rootBox.MaxLon - rootBox.MinLon);
        double lat = rootBox.MinLat + rng.NextDouble() * (rootBox.MaxLat - rootBox.MinLat);

        const int K = 10;
        var buffer = new uint[K];
        int written = NativeRTreeQuery.Nearest(
            graph.GetRTreeNodes(), graph.RTreeRootIndex, graph.GetEdgeAabbs(),
            lon, lat, K, buffer);

        Assert.Equal(K, written);

        // Brute-force: 距離² 昇順で上位 K
        var distances = new (uint EdgeId, double DistSq)[truth.EdgeAabbs.Length];
        for (int e = 0; e < truth.EdgeAabbs.Length; e++)
        {
            distances[e] = ((uint)e, PointBboxDistanceSq(truth.EdgeAabbs[e], lon, lat));
        }
        Array.Sort(distances, (a, b) => a.DistSq.CompareTo(b.DistSq));
        double kthDistSq = distances[K - 1].DistSq;

        // Native の K 件すべてが Brute-force 第 K 距離以下である必要
        // （同距離タイの曖昧性を許容する妥協、計画書 §4.4-C テスト 7）
        for (int i = 0; i < K; i++)
        {
            double nativeDistSq = PointBboxDistanceSq(truth.EdgeAabbs[buffer[i]], lon, lat);
            Assert.True(
                nativeDistSq <= kthDistSq + 1e-12,
                $"native[{i}]={buffer[i]} distSq={nativeDistSq} > kthDistSq={kthDistSq}");
        }

        // 結果が距離² 昇順であること
        for (int i = 1; i < K; i++)
        {
            double a = PointBboxDistanceSq(truth.EdgeAabbs[buffer[i - 1]], lon, lat);
            double b = PointBboxDistanceSq(truth.EdgeAabbs[buffer[i]], lon, lat);
            Assert.True(a <= b + 1e-12, $"昇順違反: i={i} a={a} b={b}");
        }
    }

    [Fact]
    public void RTreeNodeStructure_LeafFlagAndChildReferenceContract_Holds()
    {
        var graph = _fixture.Graph;
        var nodes = graph.GetRTreeNodes();
        var coveredEdges = new HashSet<uint>();

        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            bool isLeaf = (n.Flags & OdrgRTreeFlags.LeafBit) != 0;
            if (isLeaf)
            {
                Assert.True(
                    n.FirstChildIndex + n.ChildCount <= graph.EdgeCount,
                    $"leaf node[{i}] first={n.FirstChildIndex} count={n.ChildCount} > EdgeCount={graph.EdgeCount}");
                for (uint k = 0; k < n.ChildCount; k++)
                {
                    coveredEdges.Add(n.FirstChildIndex + k);
                }
            }
            else
            {
                Assert.True(
                    n.FirstChildIndex + n.ChildCount <= graph.RTreeNodeCount,
                    $"internal node[{i}] first={n.FirstChildIndex} count={n.ChildCount} > NodeCount={graph.RTreeNodeCount}");
            }
        }

        // 全リーフのエッジ ID 和集合 == {0..EdgeCount-1}
        Assert.Equal((int)graph.EdgeCount, coveredEdges.Count);
        for (uint e = 0; e < graph.EdgeCount; e++)
        {
            Assert.Contains(e, coveredEdges);
        }
    }

    private static bool BboxIntersects(Aabb edgeAabb, in OdrgBbox queryBox)
    {
        if (edgeAabb.MaxLon < queryBox.MinLon) return false;
        if (edgeAabb.MinLon > queryBox.MaxLon) return false;
        if (edgeAabb.MaxLat < queryBox.MinLat) return false;
        if (edgeAabb.MinLat > queryBox.MaxLat) return false;
        return true;
    }

    private static double PointBboxDistanceSq(Aabb box, double lon, double lat)
    {
        double dx = 0.0;
        if (lon < box.MinLon) dx = box.MinLon - lon;
        else if (lon > box.MaxLon) dx = lon - box.MaxLon;

        double dy = 0.0;
        if (lat < box.MinLat) dy = box.MinLat - lat;
        else if (lat > box.MaxLat) dy = lat - box.MaxLat;

        return dx * dx + dy * dy;
    }
}
