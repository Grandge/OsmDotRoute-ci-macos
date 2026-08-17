using System;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// STR (Sort-Tile-Recursive) パック静的 R-tree のビルド結果。
/// </summary>
/// <param name="Nodes">ノード配列（仕様書 §4.6.2）。葉から内部、最後にルート。</param>
/// <param name="RootIndex"><see cref="Nodes"/> 内のルートノードインデックス。</param>
/// <param name="BranchingFactor">分岐数 M（v0.2 初期値 = 16）。</param>
/// <param name="TreeHeight">ツリー高（参考情報、ルート 1 ノードを含む全段数）。</param>
/// <param name="EdgePermutation">
/// エッジ再採番表。<c>EdgePermutation[newEdgeId] = oldEdgeId</c>。
/// 呼出側はこの順序で <c>EdgeRecord</c> / <c>Aabb</c> / <c>EdgeFlags</c> / <c>BakedProfileTable</c> を
/// 並べ替えた上で <c>.odrg</c> を書き出す（仕様書 §4.6.3 で leaf の firstChildIndex は連番エッジ ID 前提）。
/// </param>
internal sealed record StrRTree(
    RTreeNode[] Nodes,
    uint RootIndex,
    uint BranchingFactor,
    uint TreeHeight,
    int[] EdgePermutation)
{
    /// <summary>空ツリー（エッジ 0 件）。</summary>
    public static StrRTree Empty(int branchingFactor) =>
        new(Array.Empty<RTreeNode>(), 0, (uint)branchingFactor, 0, Array.Empty<int>());

    /// <summary>エッジ数。</summary>
    public int EdgeCount => EdgePermutation.Length;

    /// <summary>ノード総数。</summary>
    public int NodeCount => Nodes.Length;
}
