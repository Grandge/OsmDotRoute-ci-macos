using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// 道路 way のノード参照履歴から、どの OSM Node を「頂点」に昇格させるかを決める。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 3.3。仕様書 §3.1 のエッジモデル「fromVertexId / toVertexId / shape」と
/// 計画書 §3.4-3 「頂点正規化（交差点・端点を頂点に、中間ノードはシェイプに）」の実装。
/// </para>
/// <para>判定規則:</para>
/// <list type="bullet">
///   <item>道路 way の始端ノード・終端ノードは無条件で頂点</item>
///   <item>2 つ以上の道路 way から参照されているノード（交差点）は頂点</item>
///   <item>同じ way 内に 2 回以上現れるノード（自己交差）は頂点</item>
///   <item>それ以外（way の中間に 1 回だけ現れる）はシェイプ点</item>
/// </list>
/// <para>使い方:</para>
/// <list type="number">
///   <item><see cref="AddWay"/> を <see cref="WayFilter.IsRoadWay"/> を通った全 way について呼ぶ</item>
///   <item><see cref="Build"/> で <see cref="VertexAssignment"/> を取得</item>
/// </list>
/// <para>
/// 頂点 ID は OSM Node ID の昇順で 0..N-1 に採番。<see cref="Dictionary{TKey,TValue}"/>
/// の列挙順は実装依存のため、決定論を担保するため Build 時に sort する。
/// </para>
/// </remarks>
internal sealed class VertexNormalizer
{
    private readonly Dictionary<long, NodeInfo> _info;

    public VertexNormalizer(int initialCapacity = 0)
    {
        _info = new Dictionary<long, NodeInfo>(initialCapacity);
    }

    /// <summary>これまでに <see cref="AddWay"/> で観測されたユニークノード数。</summary>
    public int ObservedNodeCount => _info.Count;

    /// <summary>
    /// 道路 way 1 本のノード参照列を投入する。長さ &lt; 2 の way（エッジを形成できない退化 way）は無視。
    /// </summary>
    /// <param name="nodeRefs">OSM Node ID 列（delta デコード済の絶対値）。</param>
    public void AddWay(ReadOnlySpan<long> nodeRefs)
    {
        if (nodeRefs.Length < 2)
            return;

        int lastIndex = nodeRefs.Length - 1;
        for (int i = 0; i < nodeRefs.Length; i++)
        {
            long id = nodeRefs[i];
            ref NodeInfo info = ref CollectionsMarshal.GetValueRefOrAddDefault(_info, id, out _);
            info.Count++;
            if (i == 0 || i == lastIndex)
                info.IsEndpoint = true;
        }
    }

    /// <summary>
    /// 観測済みノードから頂点判定を行い、<see cref="VertexAssignment"/> を返す。
    /// </summary>
    public VertexAssignment Build()
    {
        // 頂点候補のみを抽出し OSM ID でソート（決定論）
        var vertices = new List<long>();
        foreach (var (osmId, info) in _info)
        {
            if (info.IsEndpoint || info.Count >= 2)
                vertices.Add(osmId);
        }
        vertices.Sort();

        var mapping = new Dictionary<long, int>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
            mapping[vertices[i]] = i;

        return new VertexAssignment(mapping);
    }

    private struct NodeInfo
    {
        public int Count;
        public bool IsEndpoint;
    }
}
