using System;
using System.Linq;
using OsmDotRoute.Extractor.Pipeline;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.7 — <see cref="StrRTreeBuilder"/> の構造正当性テスト。
/// </summary>
public sealed class StrRTreeBuilderTests
{
    private static Aabb Box(double minLon, double minLat, double maxLon, double maxLat) =>
        new(minLon, minLat, maxLon, maxLat);

    private static Aabb PointBox(double lon, double lat) => new(lon, lat, lon, lat);

    [Fact]
    public void Empty_ReturnsEmptyTree()
    {
        var tree = StrRTreeBuilder.Build(ReadOnlySpan<Aabb>.Empty);
        Assert.Equal(0, tree.NodeCount);
        Assert.Equal(0, tree.EdgeCount);
        Assert.Equal(16u, tree.BranchingFactor);
        Assert.Equal(0u, tree.TreeHeight);
    }

    [Fact]
    public void SingleEdge_SingleLeafRoot()
    {
        var aabbs = new[] { Box(0, 0, 1, 1) };
        var tree = StrRTreeBuilder.Build(aabbs);

        Assert.Equal(1, tree.NodeCount);
        Assert.Equal(0u, tree.RootIndex);
        Assert.True(tree.Nodes[0].IsLeaf);
        Assert.Equal(0u, tree.Nodes[0].FirstChildIndex);
        Assert.Equal(1u, tree.Nodes[0].ChildCount);
        Assert.Equal(new[] { 0 }, tree.EdgePermutation);
    }

    [Fact]
    public void TwoEdges_M16_SingleLeafContainsBoth()
    {
        var aabbs = new[] { Box(0, 0, 1, 1), Box(10, 10, 11, 11) };
        var tree = StrRTreeBuilder.Build(aabbs);

        Assert.Equal(1, tree.NodeCount);
        Assert.True(tree.Nodes[0].IsLeaf);
        Assert.Equal(2u, tree.Nodes[0].ChildCount);
        // 葉の Bounds は両エッジを内包
        Assert.Equal(0.0, tree.Nodes[0].Bounds.MinLon);
        Assert.Equal(11.0, tree.Nodes[0].Bounds.MaxLon);
    }

    [Fact]
    public void EdgesExceedM_BuildsLeavesPlusInternalRoot()
    {
        // 5 エッジ、M=2 → 葉 3 個（2+2+1）+ 内部 2 個（葉 2+1）+ ルート 1 個
        var aabbs = new Aabb[5];
        for (int i = 0; i < 5; i++)
            aabbs[i] = PointBox(i, i);
        var tree = StrRTreeBuilder.Build(aabbs, branchingFactor: 2);

        // ノード数は >= 葉数（>=3）
        Assert.True(tree.NodeCount >= 3);
        // ルートは内部
        Assert.False(tree.Nodes[tree.RootIndex].IsLeaf);
        // すべてのエッジが permutation に 1 度ずつ含まれる
        Assert.Equal(5, tree.EdgePermutation.Length);
        Assert.Equal(Enumerable.Range(0, 5).ToHashSet(), tree.EdgePermutation.ToHashSet());
    }

    [Fact]
    public void RootBoundsCoverAllEdges()
    {
        var aabbs = new Aabb[20];
        var rng = new Random(42);
        double minLon = double.PositiveInfinity, maxLon = double.NegativeInfinity;
        double minLat = double.PositiveInfinity, maxLat = double.NegativeInfinity;
        for (int i = 0; i < 20; i++)
        {
            double lon = rng.NextDouble() * 360 - 180;
            double lat = rng.NextDouble() * 180 - 90;
            double w = rng.NextDouble();
            double h = rng.NextDouble();
            aabbs[i] = Box(lon, lat, lon + w, lat + h);
            minLon = Math.Min(minLon, lon);
            minLat = Math.Min(minLat, lat);
            maxLon = Math.Max(maxLon, lon + w);
            maxLat = Math.Max(maxLat, lat + h);
        }
        var tree = StrRTreeBuilder.Build(aabbs, branchingFactor: 4);

        var root = tree.Nodes[tree.RootIndex];
        Assert.Equal(minLon, root.Bounds.MinLon);
        Assert.Equal(minLat, root.Bounds.MinLat);
        Assert.Equal(maxLon, root.Bounds.MaxLon);
        Assert.Equal(maxLat, root.Bounds.MaxLat);
    }

    [Fact]
    public void LeafBounds_AreUnionOfContainedEdges()
    {
        var aabbs = new Aabb[8];
        for (int i = 0; i < 8; i++)
            aabbs[i] = PointBox(i, i * 0.5);
        var tree = StrRTreeBuilder.Build(aabbs, branchingFactor: 4);

        // 全ての葉について、含むエッジの union と一致することを確認
        foreach (var node in tree.Nodes)
        {
            if (!node.IsLeaf) continue;
            double min_lon = double.PositiveInfinity, max_lon = double.NegativeInfinity;
            double min_lat = double.PositiveInfinity, max_lat = double.NegativeInfinity;
            for (uint i = 0; i < node.ChildCount; i++)
            {
                int newEdgeId = (int)(node.FirstChildIndex + i);
                int origEdgeId = tree.EdgePermutation[newEdgeId];
                var a = aabbs[origEdgeId];
                min_lon = Math.Min(min_lon, a.MinLon);
                min_lat = Math.Min(min_lat, a.MinLat);
                max_lon = Math.Max(max_lon, a.MaxLon);
                max_lat = Math.Max(max_lat, a.MaxLat);
            }
            Assert.Equal(min_lon, node.Bounds.MinLon);
            Assert.Equal(min_lat, node.Bounds.MinLat);
            Assert.Equal(max_lon, node.Bounds.MaxLon);
            Assert.Equal(max_lat, node.Bounds.MaxLat);
        }
    }

    [Fact]
    public void InternalBounds_AreUnionOfChildBounds()
    {
        var aabbs = new Aabb[20];
        for (int i = 0; i < 20; i++)
            aabbs[i] = PointBox(i, i);
        var tree = StrRTreeBuilder.Build(aabbs, branchingFactor: 3);

        foreach (var node in tree.Nodes)
        {
            if (node.IsLeaf) continue;
            double min_lon = double.PositiveInfinity, max_lon = double.NegativeInfinity;
            double min_lat = double.PositiveInfinity, max_lat = double.NegativeInfinity;
            for (uint i = 0; i < node.ChildCount; i++)
            {
                var child = tree.Nodes[node.FirstChildIndex + i];
                min_lon = Math.Min(min_lon, child.Bounds.MinLon);
                min_lat = Math.Min(min_lat, child.Bounds.MinLat);
                max_lon = Math.Max(max_lon, child.Bounds.MaxLon);
                max_lat = Math.Max(max_lat, child.Bounds.MaxLat);
            }
            Assert.Equal(min_lon, node.Bounds.MinLon);
            Assert.Equal(min_lat, node.Bounds.MinLat);
            Assert.Equal(max_lon, node.Bounds.MaxLon);
            Assert.Equal(max_lat, node.Bounds.MaxLat);
        }
    }

    [Fact]
    public void ChildIndices_AreContiguousForAllInternalNodes()
    {
        var aabbs = new Aabb[50];
        var rng = new Random(123);
        for (int i = 0; i < 50; i++)
        {
            double lon = rng.NextDouble() * 10;
            double lat = rng.NextDouble() * 10;
            aabbs[i] = PointBox(lon, lat);
        }
        var tree = StrRTreeBuilder.Build(aabbs, branchingFactor: 4);

        foreach (var node in tree.Nodes)
        {
            if (node.IsLeaf)
            {
                // エッジ ID 連続性は permutation 配列の存在で担保される
                Assert.True(node.FirstChildIndex + node.ChildCount <= (uint)tree.EdgeCount);
            }
            else
            {
                // ノード インデックス連続性
                Assert.True(node.FirstChildIndex + node.ChildCount <= (uint)tree.NodeCount);
            }
        }
    }

    [Fact]
    public void EdgePermutation_IsValidBijection()
    {
        var aabbs = new Aabb[37];
        for (int i = 0; i < 37; i++)
            aabbs[i] = PointBox(i, i);
        var tree = StrRTreeBuilder.Build(aabbs, branchingFactor: 5);

        Assert.Equal(37, tree.EdgePermutation.Length);
        var set = tree.EdgePermutation.ToHashSet();
        Assert.Equal(37, set.Count);
        Assert.Equal(0, tree.EdgePermutation.Min());
        Assert.Equal(36, tree.EdgePermutation.Max());
    }

    [Fact]
    public void TreeHeight_IsLogMOfN()
    {
        // 64 エッジ、M=4 → 葉 16、内部 4、ルート 1 → 高さ 3 (or close)
        var aabbs = new Aabb[64];
        for (int i = 0; i < 64; i++)
            aabbs[i] = PointBox(i % 8, i / 8);
        var tree = StrRTreeBuilder.Build(aabbs, branchingFactor: 4);

        Assert.True(tree.TreeHeight >= 3 && tree.TreeHeight <= 4);
    }

    [Fact]
    public void InvalidBranchingFactor_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StrRTreeBuilder.Build(ReadOnlySpan<Aabb>.Empty, branchingFactor: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StrRTreeBuilder.Build(ReadOnlySpan<Aabb>.Empty, branchingFactor: 0));
    }

    [Fact]
    public void StressTest_1000Edges_AllReachableViaTreeTraversal()
    {
        var aabbs = new Aabb[1000];
        var rng = new Random(7);
        for (int i = 0; i < 1000; i++)
        {
            double lon = rng.NextDouble() * 100;
            double lat = rng.NextDouble() * 100;
            aabbs[i] = Box(lon, lat, lon + 0.1, lat + 0.1);
        }
        var tree = StrRTreeBuilder.Build(aabbs, branchingFactor: 16);

        // ルートから DFS で全エッジ到達可能性を確認
        var visited = new HashSet<int>();
        var stack = new Stack<uint>();
        stack.Push(tree.RootIndex);
        while (stack.Count > 0)
        {
            var idx = stack.Pop();
            var node = tree.Nodes[idx];
            if (node.IsLeaf)
            {
                for (uint i = 0; i < node.ChildCount; i++)
                    visited.Add((int)(node.FirstChildIndex + i));
            }
            else
            {
                for (uint i = 0; i < node.ChildCount; i++)
                    stack.Push(node.FirstChildIndex + i);
            }
        }

        Assert.Equal(1000, visited.Count);
        Assert.Equal(0, visited.Min());
        Assert.Equal(999, visited.Max());
    }
}
