using OsmDotRoute.Geometry;
using OsmDotRoute.Native;
using OsmDotRoute.Routing;
using Xunit;

namespace OsmDotRoute.Tests.Native;

/// <summary>
/// Phase 3 ステップ 3B.2 — <see cref="NativeRoadGraph.QueryEdgesByAabb"/> の検証 3 件（計画書 §4.2.3）。
/// </summary>
/// <remarks>
/// <para>
/// ユーザー判断 T4=(A) Aabb 公開 / T5=(A) IEnumerable<uint> yield return を検証する。
/// </para>
/// <para>
/// Brute-force 突合は <see cref="NativeRoadGraph.GetEdgeAabbs"/> で得た <c>OdrgBbox</c> (Lon-Lat) を
/// 直接線形走査する。<c>OdrgReadResult.EdgeAabbs</c> は <c>OsmDotRoute.Extractor.Pipeline.Aabb</c>
/// (Core の <c>OsmDotRoute.Geometry.Aabb</c> とは別型、3A.1 で並存している) のためテストから直接使えない。
/// </para>
/// </remarks>
public sealed class NativeRoadGraphQueryEdgesByAabbTests : IClassFixture<NativeAndOdrgReaderFixture>
{
    private readonly NativeAndOdrgReaderFixture _fixture;

    public NativeRoadGraphQueryEdgesByAabbTests(NativeAndOdrgReaderFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void QueryEdgesByAabb_FullBounds_ReturnsAllEdges()
    {
        IRoadGraph graph = _fixture.Graph;
        var bounds = graph.GetBounds();
        var aabb = new Aabb(bounds.SouthWest, bounds.NorthEast);

        var result = graph.QueryEdgesByAabb(aabb).ToList();

        // 全 bounds で全エッジ返却 (R-tree のルート AABB はすべてのエッジを包含)
        Assert.Equal(graph.EdgeCount, result.Count);
    }

    [Fact]
    public void QueryEdgesByAabb_OutOfBounds_ReturnsEmpty()
    {
        IRoadGraph graph = _fixture.Graph;
        // 津島市 (≒ N35) から十分離れた高緯度
        var far = new Aabb(
            new GeoCoordinate(89.0, 0.0),
            new GeoCoordinate(89.1, 0.1));

        var result = graph.QueryEdgesByAabb(far).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void QueryEdgesByAabb_RandomTrials_MatchesBruteForce()
    {
        var graph = _fixture.Graph;
        IRoadGraph igraph = graph;
        var bounds = graph.GetBounds();

        // Brute-force 用に EDGE_AABB セクションを ToArray でコピー (Span はラムダ/別ループで参照不可)
        var edgeBboxes = graph.GetEdgeAabbs().ToArray();
        int edgeCount = (int)graph.EdgeCount;

        var rng = new Random(987);
        const int TrialCount = 20;

        for (int trial = 0; trial < TrialCount; trial++)
        {
            // 範囲内のランダム矩形 (0.01〜0.06 度幅 ≈ 1〜6 km)
            double lat0 = bounds.SouthWest.Latitude
                + rng.NextDouble() * (bounds.NorthEast.Latitude - bounds.SouthWest.Latitude);
            double lon0 = bounds.SouthWest.Longitude
                + rng.NextDouble() * (bounds.NorthEast.Longitude - bounds.SouthWest.Longitude);
            double size = 0.01 + rng.NextDouble() * 0.05;

            var aabb = new Aabb(
                new GeoCoordinate(lat0, lon0),
                new GeoCoordinate(lat0 + size, lon0 + size));

            var rtreeResult = new HashSet<uint>(igraph.QueryEdgesByAabb(aabb));

            // Brute-force: NativeRoadGraph 内部の OdrgBbox (Lon-Lat) を直接線形走査
            var bruteResult = new HashSet<uint>();
            for (int e = 0; e < edgeCount; e++)
            {
                var bbox = edgeBboxes[e];
                bool intersects = !(bbox.MaxLon < aabb.MinLongitude
                                 || bbox.MinLon > aabb.MaxLongitude
                                 || bbox.MaxLat < aabb.MinLatitude
                                 || bbox.MinLat > aabb.MaxLatitude);
                if (intersects) bruteResult.Add((uint)e);
            }

            Assert.Equal(bruteResult.Count, rtreeResult.Count);
            Assert.True(bruteResult.SetEquals(rtreeResult),
                $"trial={trial}: R-tree {rtreeResult.Count} 件 vs Brute-force {bruteResult.Count} 件 不一致");
        }
    }
}
