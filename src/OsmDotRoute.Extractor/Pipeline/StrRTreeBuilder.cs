using System;
using System.Collections.Generic;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// STR (Sort-Tile-Recursive) パック静的 R-tree を構築する。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 3.7。仕様書 §4.6.1 のアルゴリズム:
/// </para>
/// <list type="number">
///   <item>全エッジ AABB の中心点 (cx, cy) を計算</item>
///   <item>L = ⌈N/M⌉ 葉数、S = ⌈√L⌉ ストリップ数</item>
///   <item>cx でソートし S ストリップに分割</item>
///   <item>各ストリップ内で cy ソートし、M ずつ葉ノードにまとめる</item>
///   <item>葉の AABB = 内包エッジ AABB の Union</item>
///   <item>葉を再帰的に同じ STR でグルーピングし、内部ノードを構築</item>
///   <item>1 ノードに集約されたらルート確定</item>
/// </list>
/// <para>
/// 葉の <c>firstChildIndex</c> がエッジ ID の連番を指すよう、エッジは R-tree 葉グループ順に
/// 再採番される。<see cref="StrRTree.EdgePermutation"/> を呼出側で適用すること。
/// </para>
/// <para>
/// ノード配列のレイアウトは BFS 順（ルート→子→孫…）。各内部ノードの子は配列上連続するため、
/// <c>firstChildIndex</c> + 0..<c>childCount</c>-1 で正しく参照できる。
/// </para>
/// </remarks>
internal static class StrRTreeBuilder
{
    public const int DefaultBranchingFactor = 16;

    public static StrRTree Build(ReadOnlySpan<Aabb> edgeAabbs, int branchingFactor = DefaultBranchingFactor)
    {
        if (branchingFactor < 2)
            throw new ArgumentOutOfRangeException(nameof(branchingFactor), "分岐数 M は 2 以上が必要");

        if (edgeAabbs.Length == 0)
            return StrRTree.Empty(branchingFactor);

        int M = branchingFactor;

        // Step 1: 葉構築。エッジを STR で並べ、M ずつ葉にまとめる。
        var leafItems = new LeafItem[edgeAabbs.Length];
        for (int i = 0; i < edgeAabbs.Length; i++)
        {
            var a = edgeAabbs[i];
            leafItems[i] = new LeafItem(a, (a.MinLon + a.MaxLon) * 0.5, (a.MinLat + a.MaxLat) * 0.5, i);
        }
        var leaves = BuildLeaves(leafItems, M);

        // Step 2: 内部レベル構築（bottom-up）
        var levels = new List<BuildNode[]> { leaves };
        while (levels[^1].Length > 1)
        {
            levels.Add(BuildInternals(levels[^1], M));
        }

        // Step 3: 出力配列を BFS 順で確定。
        // 重要: 各レベルのノードを「親の Children 配列の順」に並べ替えてから emit する。
        // そうしないと「親の Children[0..N-1] が出力配列上で連続」という規約を破る。
        levels.Reverse();
        int treeHeight = levels.Count;

        // 親の Children 並び順で各レベルを再構成（root は単一なのでそのまま）
        var orderedLevels = new BuildNode[treeHeight][];
        orderedLevels[0] = levels[0];
        for (int k = 1; k < treeHeight; k++)
        {
            var prev = orderedLevels[k - 1];
            var bag = new List<BuildNode>();
            foreach (var parent in prev)
            {
                if (parent.Children is not null)
                {
                    foreach (var c in parent.Children)
                        bag.Add(c);
                }
            }
            orderedLevels[k] = bag.ToArray();
        }

        // 各 BuildNode に出力配列上のインデックスを割り当て
        int totalNodes = 0;
        foreach (var lvl in orderedLevels)
            totalNodes += lvl.Length;

        var output = new RTreeNode[totalNodes];
        var nodeIndex = new Dictionary<BuildNode, int>(totalNodes, ReferenceEqualityComparer.Instance);
        int offset = 0;
        foreach (var lvl in orderedLevels)
        {
            foreach (var n in lvl)
                nodeIndex[n] = offset++;
        }

        // BFS 順で emit、葉のエッジを順次 permutation に積む
        var edgePermutation = new int[edgeAabbs.Length];
        int edgeCursor = 0;
        offset = 0;
        foreach (var lvl in orderedLevels)
        {
            foreach (var n in lvl)
            {
                if (n.IsLeaf)
                {
                    int firstEdgeId = edgeCursor;
                    int[] origIndices = n.LeafEdgeIndices!;
                    for (int i = 0; i < origIndices.Length; i++)
                        edgePermutation[edgeCursor++] = origIndices[i];
                    output[offset++] = RTreeNode.Create(
                        n.Bounds,
                        firstChildIndex: (uint)firstEdgeId,
                        childCount: (uint)origIndices.Length,
                        isLeaf: true);
                }
                else
                {
                    int firstChild = nodeIndex[n.Children![0]];
                    output[offset++] = RTreeNode.Create(
                        n.Bounds,
                        firstChildIndex: (uint)firstChild,
                        childCount: (uint)n.Children.Length,
                        isLeaf: false);
                }
            }
        }

        uint rootIdx = (uint)nodeIndex[orderedLevels[0][0]];
        return new StrRTree(output, rootIdx, (uint)M, (uint)treeHeight, edgePermutation);
    }

    private static BuildNode[] BuildLeaves(LeafItem[] items, int M)
    {
        int N = items.Length;
        int L = (N + M - 1) / M;
        int S = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(L)));
        int stripSize = Math.Max(M, (N + S - 1) / S);

        Array.Sort(items, (a, b) => a.CenterX.CompareTo(b.CenterX));

        var leaves = new List<BuildNode>(L);
        for (int sStart = 0; sStart < N; sStart += stripSize)
        {
            int sEnd = Math.Min(sStart + stripSize, N);
            int stripCount = sEnd - sStart;
            Array.Sort(items, sStart, stripCount, LeafItemYComparer.Instance);

            for (int cStart = sStart; cStart < sEnd; cStart += M)
            {
                int cEnd = Math.Min(cStart + M, sEnd);
                int count = cEnd - cStart;
                var edgeIndices = new int[count];
                Aabb b = items[cStart].Bounds;
                edgeIndices[0] = items[cStart].OriginalIndex;
                for (int i = 1; i < count; i++)
                {
                    edgeIndices[i] = items[cStart + i].OriginalIndex;
                    b = Union(b, items[cStart + i].Bounds);
                }
                leaves.Add(new BuildNode { Bounds = b, LeafEdgeIndices = edgeIndices });
            }
        }
        return leaves.ToArray();
    }

    private static BuildNode[] BuildInternals(BuildNode[] children, int M)
    {
        int N = children.Length;
        int L = (N + M - 1) / M;
        int S = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(L)));
        int stripSize = Math.Max(M, (N + S - 1) / S);

        // node center を一度だけ計算してから sort（パフォーマンス上は十分）
        var items = new NodeItem[N];
        for (int i = 0; i < N; i++)
        {
            var b = children[i].Bounds;
            items[i] = new NodeItem(children[i], (b.MinLon + b.MaxLon) * 0.5, (b.MinLat + b.MaxLat) * 0.5);
        }

        Array.Sort(items, (a, b) => a.CenterX.CompareTo(b.CenterX));

        var parents = new List<BuildNode>(L);
        for (int sStart = 0; sStart < N; sStart += stripSize)
        {
            int sEnd = Math.Min(sStart + stripSize, N);
            int stripCount = sEnd - sStart;
            Array.Sort(items, sStart, stripCount, NodeItemYComparer.Instance);

            for (int cStart = sStart; cStart < sEnd; cStart += M)
            {
                int cEnd = Math.Min(cStart + M, sEnd);
                int count = cEnd - cStart;
                var childArr = new BuildNode[count];
                Aabb b = items[cStart].Node.Bounds;
                childArr[0] = items[cStart].Node;
                for (int i = 1; i < count; i++)
                {
                    childArr[i] = items[cStart + i].Node;
                    b = Union(b, items[cStart + i].Node.Bounds);
                }
                parents.Add(new BuildNode { Bounds = b, Children = childArr });
            }
        }
        return parents.ToArray();
    }

    private static Aabb Union(Aabb a, Aabb b) => new(
        MinLon: Math.Min(a.MinLon, b.MinLon),
        MinLat: Math.Min(a.MinLat, b.MinLat),
        MaxLon: Math.Max(a.MaxLon, b.MaxLon),
        MaxLat: Math.Max(a.MaxLat, b.MaxLat));

    private readonly record struct LeafItem(Aabb Bounds, double CenterX, double CenterY, int OriginalIndex);

    private readonly record struct NodeItem(BuildNode Node, double CenterX, double CenterY);

    private sealed class LeafItemYComparer : IComparer<LeafItem>
    {
        public static readonly LeafItemYComparer Instance = new();
        public int Compare(LeafItem x, LeafItem y) => x.CenterY.CompareTo(y.CenterY);
    }

    private sealed class NodeItemYComparer : IComparer<NodeItem>
    {
        public static readonly NodeItemYComparer Instance = new();
        public int Compare(NodeItem x, NodeItem y) => x.CenterY.CompareTo(y.CenterY);
    }

    private sealed class BuildNode
    {
        public Aabb Bounds;
        public BuildNode[]? Children;       // 内部ノード時
        public int[]? LeafEdgeIndices;       // 葉ノード時（元エッジ ID 列）
        public bool IsLeaf => LeafEdgeIndices is not null;
    }
}
