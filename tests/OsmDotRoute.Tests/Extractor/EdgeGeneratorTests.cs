using System;
using System.Linq;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.4 — <see cref="EdgeGenerator.SplitWay"/> の判定テスト。
/// </summary>
public sealed class EdgeGeneratorTests
{
    private static readonly OsmStringTable EmptyTable = new(new[] { Array.Empty<byte>() });

    private static OsmWay MakeWay(long id, params long[] nodeRefs) =>
        new(id, nodeRefs, Array.Empty<int>(), Array.Empty<int>());

    /// <summary>指定した OSM ID 配列を全て頂点として持つ assignment を作る。</summary>
    private static VertexAssignment MakeAssignment(params long[] vertexOsmIds)
    {
        var n = new VertexNormalizer();
        // VertexNormalizer の判定経路を素直に使うため、2 ノード way として 1 個ずつ投入
        // ペアを作る: 各 vertexId を端点として 2 ノード way を構築
        for (int i = 0; i < vertexOsmIds.Length - 1; i++)
            n.AddWay(new[] { vertexOsmIds[i], vertexOsmIds[i + 1] });
        if (vertexOsmIds.Length == 1)
            n.AddWay(new[] { vertexOsmIds[0], vertexOsmIds[0] + 999_999 });  // self pair で endpoint 化
        return n.Build();
    }

    [Fact]
    public void SplitWay_SingleEdgeWayTwoNodes_ProducesOneEdge()
    {
        var asg = MakeAssignment(100, 200);
        var way = MakeWay(id: 1, 100, 200);

        var edges = EdgeGenerator.SplitWay(way, asg, EmptyTable);

        var e = Assert.Single(edges);
        Assert.Equal(1, e.OsmWayId);
        Assert.Equal(asg.TryGetVertexId(100, out int v100) ? v100 : -1, e.FromVertexId);
        Assert.Equal(asg.TryGetVertexId(200, out int v200) ? v200 : -1, e.ToVertexId);
        Assert.Empty(e.ShapeNodeRefs);  // 直線エッジは shape 0
    }

    [Fact]
    public void SplitWay_WayWithIntermediateShapePoints_ProducesOneEdgeWithShape()
    {
        // way: A-B-C-D。A と D だけが頂点（B, C は他 way に出現せず）
        var n = new VertexNormalizer();
        n.AddWay(new long[] { 100, 200, 300, 400 });
        var asg = n.Build();

        var way = MakeWay(id: 42, 100, 200, 300, 400);
        var edges = EdgeGenerator.SplitWay(way, asg, EmptyTable);

        var e = Assert.Single(edges);
        Assert.Equal(42, e.OsmWayId);
        Assert.Equal(new long[] { 200, 300 }, e.ShapeNodeRefs);
    }

    [Fact]
    public void SplitWay_WayWithMiddleIntersection_SplitsIntoTwoEdges()
    {
        // way1: A-B-C-D、way2: E-C-F が C を共有 → C は頂点に昇格
        var n = new VertexNormalizer();
        n.AddWay(new long[] { 100, 200, 300, 400 });
        n.AddWay(new long[] { 500, 300, 600 });
        var asg = n.Build();

        // way1 を分割: A-B-C と C-D の 2 エッジになる
        var way = MakeWay(id: 1, 100, 200, 300, 400);
        var edges = EdgeGenerator.SplitWay(way, asg, EmptyTable);

        Assert.Equal(2, edges.Count);

        Assert.True(asg.TryGetVertexId(100, out int v100));
        Assert.True(asg.TryGetVertexId(300, out int v300));
        Assert.True(asg.TryGetVertexId(400, out int v400));

        // エッジ 1: 100 → 300、shape=[200]
        Assert.Equal(v100, edges[0].FromVertexId);
        Assert.Equal(v300, edges[0].ToVertexId);
        Assert.Equal(new long[] { 200 }, edges[0].ShapeNodeRefs);

        // エッジ 2: 300 → 400、shape=[]
        Assert.Equal(v300, edges[1].FromVertexId);
        Assert.Equal(v400, edges[1].ToVertexId);
        Assert.Empty(edges[1].ShapeNodeRefs);
    }

    [Fact]
    public void SplitWay_ClosedLoop_ProducesSingleSelfLoopEdge()
    {
        // ラウンドアバウト相当: A-B-C-D-A。A だけ頂点
        var n = new VertexNormalizer();
        n.AddWay(new long[] { 100, 200, 300, 400, 100 });
        var asg = n.Build();

        var way = MakeWay(id: 7, 100, 200, 300, 400, 100);
        var edges = EdgeGenerator.SplitWay(way, asg, EmptyTable);

        var e = Assert.Single(edges);
        Assert.True(asg.TryGetVertexId(100, out int v100));
        Assert.Equal(v100, e.FromVertexId);
        Assert.Equal(v100, e.ToVertexId);  // self-loop
        Assert.Equal(new long[] { 200, 300, 400 }, e.ShapeNodeRefs);
    }

    [Fact]
    public void SplitWay_AllNodesAreVertices_ProducesEdgesWithEmptyShapes()
    {
        // way: A-B-C 全てが他 way との交差で頂点
        var n = new VertexNormalizer();
        n.AddWay(new long[] { 100, 200 });
        n.AddWay(new long[] { 200, 300 });
        n.AddWay(new long[] { 100, 200, 300 });  // この way を分割対象に
        var asg = n.Build();

        var way = MakeWay(id: 1, 100, 200, 300);
        var edges = EdgeGenerator.SplitWay(way, asg, EmptyTable);

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Empty(e.ShapeNodeRefs));
    }

    [Fact]
    public void SplitWay_EmptyOrDegenerateWay_ReturnsNoEdges()
    {
        var asg = new VertexNormalizer().Build();
        Assert.Empty(EdgeGenerator.SplitWay(MakeWay(1), asg, EmptyTable));
        Assert.Empty(EdgeGenerator.SplitWay(MakeWay(1, 100), asg, EmptyTable));
    }

    [Fact]
    public void SplitWay_EdgeCarriesWayTags()
    {
        var n = new VertexNormalizer();
        n.AddWay(new long[] { 100, 200 });
        var asg = n.Build();

        int[] keys = { 1, 3 };
        int[] values = { 2, 4 };
        var way = new OsmWay(Id: 1, NodeRefs: new long[] { 100, 200 }, TagKeys: keys, TagValues: values);
        var table = new OsmStringTable(new[]
        {
            Array.Empty<byte>(),
            System.Text.Encoding.UTF8.GetBytes("highway"),
            System.Text.Encoding.UTF8.GetBytes("residential"),
            System.Text.Encoding.UTF8.GetBytes("oneway"),
            System.Text.Encoding.UTF8.GetBytes("yes"),
        });

        var edge = Assert.Single(EdgeGenerator.SplitWay(way, asg, table));

        // tags はゼロコピーで共有
        Assert.Same(keys, edge.TagKeys);
        Assert.Same(values, edge.TagValues);
        Assert.Same(table, edge.StringTable);
    }

    [Fact]
    public void SplitWay_GridScenario_ProducesExpectedEdges()
    {
        // 田の字 6 way (3 水平 + 3 垂直)
        var n = new VertexNormalizer();
        long[][] ways =
        {
            new long[] { 1, 2, 3 },
            new long[] { 4, 5, 6 },
            new long[] { 7, 8, 9 },
            new long[] { 1, 4, 7 },
            new long[] { 2, 5, 8 },
            new long[] { 3, 6, 9 },
        };
        foreach (var w in ways)
            n.AddWay(w);
        var asg = n.Build();

        // 水平 way 1 (1-2-3) を分割: 1-2、2-3 の 2 エッジ
        var edges1 = EdgeGenerator.SplitWay(MakeWay(11, 1, 2, 3), asg, EmptyTable);
        Assert.Equal(2, edges1.Count);
        Assert.All(edges1, e => Assert.Empty(e.ShapeNodeRefs));

        // 6 way 全部を分割 → 各 2 エッジ = 計 12 エッジ
        int totalEdges = ways.Sum(w => EdgeGenerator.SplitWay(MakeWay(0, w), asg, EmptyTable).Count);
        Assert.Equal(12, totalEdges);
    }

    [Fact]
    public void SplitWay_NullArgs_Throws()
    {
        var asg = new VertexNormalizer().Build();
        var way = MakeWay(1, 100, 200);
        Assert.Throws<ArgumentNullException>(() => EdgeGenerator.SplitWay(null!, asg, EmptyTable));
        Assert.Throws<ArgumentNullException>(() => EdgeGenerator.SplitWay(way, null!, EmptyTable));
        Assert.Throws<ArgumentNullException>(() => EdgeGenerator.SplitWay(way, asg, null!));
    }
}
