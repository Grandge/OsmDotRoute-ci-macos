using System.Linq;
using OsmDotRoute.Extractor.Pipeline;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.3 — <see cref="VertexNormalizer"/> + <see cref="VertexAssignment"/> の判定テスト。
/// </summary>
public sealed class VertexNormalizerTests
{
    private static VertexAssignment Normalize(params long[][] ways)
    {
        var n = new VertexNormalizer();
        foreach (var w in ways)
            n.AddWay(w);
        return n.Build();
    }

    [Fact]
    public void SingleWayTwoNodes_BothBecomeVertices()
    {
        var asg = Normalize(new long[] { 100, 200 });
        Assert.Equal(2, asg.VertexCount);
        Assert.True(asg.IsVertex(100));
        Assert.True(asg.IsVertex(200));
    }

    [Fact]
    public void SingleWayThreeNodes_OnlyEndpointsAreVertices()
    {
        var asg = Normalize(new long[] { 100, 200, 300 });
        Assert.Equal(2, asg.VertexCount);
        Assert.True(asg.IsVertex(100));
        Assert.False(asg.IsVertex(200));  // 中間ノードはシェイプ点
        Assert.True(asg.IsVertex(300));
    }

    [Fact]
    public void TwoWaysSharingMiddleNode_SharedNodeBecomesVertex()
    {
        // way1: A-B-C, way2: D-B-E
        var asg = Normalize(
            new long[] { 100, 200, 300 },
            new long[] { 400, 200, 500 });

        Assert.Equal(5, asg.VertexCount);  // A,C,D,E (4 端点) + B (交差点)
        foreach (long id in new long[] { 100, 200, 300, 400, 500 })
            Assert.True(asg.IsVertex(id), $"OSM {id} should be a vertex");
    }

    [Fact]
    public void TIntersection_BranchEndpointAtMidOfOtherWayIsVertex()
    {
        // way1: A-B-C, way2: D-B
        // B は way1 では中間、way2 では端点 → count=2 + isEndpoint → 頂点
        var asg = Normalize(
            new long[] { 100, 200, 300 },
            new long[] { 400, 200 });

        Assert.Equal(4, asg.VertexCount);
        Assert.True(asg.IsVertex(200));
    }

    [Fact]
    public void ClosedLoop_StartEqualsEnd_IsSingleVertex()
    {
        // ラウンドアバウト相当: A-B-C-D-A
        var asg = Normalize(new long[] { 100, 200, 300, 400, 100 });

        // A は count=2 (始端 + 終端) → 頂点。B/C/D は count=1 中間 → シェイプ点
        Assert.Equal(1, asg.VertexCount);
        Assert.True(asg.IsVertex(100));
        Assert.False(asg.IsVertex(200));
        Assert.False(asg.IsVertex(300));
        Assert.False(asg.IsVertex(400));
    }

    [Fact]
    public void SelfIntersectingWay_FigureEight_IntersectionIsVertex()
    {
        // 8 の字: A-B-C-B-D。B が同じ way 内に 2 回出現 → 頂点
        var asg = Normalize(new long[] { 100, 200, 300, 200, 400 });

        // A,D: 端点。B: count=2。C: count=1 中間 → シェイプ点
        Assert.Equal(3, asg.VertexCount);
        Assert.True(asg.IsVertex(100));
        Assert.True(asg.IsVertex(200));
        Assert.False(asg.IsVertex(300));
        Assert.True(asg.IsVertex(400));
    }

    [Fact]
    public void EmptyInput_NoVertices()
    {
        var asg = new VertexNormalizer().Build();
        Assert.Equal(0, asg.VertexCount);
    }

    [Theory]
    [InlineData(new long[0])]      // 0 ノード
    [InlineData(new long[] { 1 })]  // 1 ノード (退化)
    public void DegenerateWay_IsIgnored(long[] nodeRefs)
    {
        var n = new VertexNormalizer();
        n.AddWay(nodeRefs);
        Assert.Equal(0, n.ObservedNodeCount);
        Assert.Equal(0, n.Build().VertexCount);
    }

    [Fact]
    public void VertexIds_AssignedSequentiallyStartingFromZero()
    {
        var asg = Normalize(
            new long[] { 100, 200 },
            new long[] { 300, 400 },
            new long[] { 500, 100 });  // 100 は再利用、count=2

        // 5 unique OSM ID は全て端点。OSM ID 昇順で 0..4 採番
        Assert.Equal(5, asg.VertexCount);
        Assert.True(asg.TryGetVertexId(100, out int id100));
        Assert.True(asg.TryGetVertexId(200, out int id200));
        Assert.True(asg.TryGetVertexId(300, out int id300));
        Assert.True(asg.TryGetVertexId(400, out int id400));
        Assert.True(asg.TryGetVertexId(500, out int id500));

        Assert.Equal(0, id100);
        Assert.Equal(1, id200);
        Assert.Equal(2, id300);
        Assert.Equal(3, id400);
        Assert.Equal(4, id500);
    }

    [Fact]
    public void VertexAssignment_TryGetVertexId_UnknownIdReturnsFalse()
    {
        var asg = Normalize(new long[] { 100, 200 });
        Assert.False(asg.TryGetVertexId(999, out int id));
        Assert.Equal(0, id);
    }

    [Fact]
    public void VertexAssignment_VertexOsmIds_ContainsAllVertices()
    {
        var asg = Normalize(new long[] { 100, 200, 300 });
        var ids = asg.VertexOsmIds.OrderBy(x => x).ToArray();
        Assert.Equal(new long[] { 100, 300 }, ids);
    }

    [Fact]
    public void ObservedNodeCount_TracksAllUniqueReferences()
    {
        var n = new VertexNormalizer();
        n.AddWay(new long[] { 100, 200, 300 });
        n.AddWay(new long[] { 200, 400 });
        // 200 は 2 回出現するが unique は 4
        Assert.Equal(4, n.ObservedNodeCount);
    }

    [Fact]
    public void RealisticGridScenario_AllIntersectionsAreVertices()
    {
        // 田の字: 4 way が中央 2x2 グリッドを形成
        //   N1 - N2 - N3
        //    |    |    |
        //   N4 - N5 - N6
        //    |    |    |
        //   N7 - N8 - N9
        // 水平 way: N1-N2-N3、N4-N5-N6、N7-N8-N9
        // 垂直 way: N1-N4-N7、N2-N5-N8、N3-N6-N9
        var asg = Normalize(
            new long[] { 1, 2, 3 },
            new long[] { 4, 5, 6 },
            new long[] { 7, 8, 9 },
            new long[] { 1, 4, 7 },
            new long[] { 2, 5, 8 },
            new long[] { 3, 6, 9 });

        // 全 9 ノードが頂点になる: 角 4 つは端点、辺中央 4 つは端点 + 通過、中央は 2 way 通過
        Assert.Equal(9, asg.VertexCount);
        for (long id = 1; id <= 9; id++)
            Assert.True(asg.IsVertex(id), $"N{id} should be a vertex");
    }
}
