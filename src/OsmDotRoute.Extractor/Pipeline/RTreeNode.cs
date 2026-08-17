namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// .odrg 仕様書 §4.6.2 R-tree ノードの論理表現（1 ノード 56 byte）。
/// </summary>
/// <remarks>
/// <para>
/// レイアウト: <c>Aabb (32B) + FirstChildIndex (4B) + ChildCount (4B) + Flags (4B) + reserved (12B) = 56B</c>。
/// 本構造体は論理表現で、wire format への展開は <c>OdrgWriter</c> 側で行う。
/// </para>
/// <para>
/// 子参照規約:
/// </para>
/// <list type="bullet">
///   <item><c>IsLeaf=true</c>: 子はエッジ。<see cref="FirstChildIndex"/> = 最初のエッジ ID、<see cref="ChildCount"/> 個が連続</item>
///   <item><c>IsLeaf=false</c>: 子は R-tree ノード。<see cref="FirstChildIndex"/> = 最初の子ノードの配列インデックス、<see cref="ChildCount"/> 個が連続</item>
/// </list>
/// </remarks>
internal readonly record struct RTreeNode(
    Aabb Bounds,
    uint FirstChildIndex,
    uint ChildCount,
    uint Flags)
{
    public const uint LeafFlagBit = 1u << 0;

    /// <summary>葉ノード判定。</summary>
    public bool IsLeaf => (Flags & LeafFlagBit) != 0;

    /// <summary>論理値から構築する便利コンストラクタ。</summary>
    public static RTreeNode Create(Aabb bounds, uint firstChildIndex, uint childCount, bool isLeaf) =>
        new(bounds, firstChildIndex, childCount, isLeaf ? LeafFlagBit : 0u);
}
