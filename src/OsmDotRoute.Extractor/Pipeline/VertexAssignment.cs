using System;
using System.Collections.Generic;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// 頂点正規化の結果。OSM Node ID → 0 始まりの連番 頂点 ID への写像。
/// </summary>
/// <remarks>
/// 3.3 のステップで <see cref="VertexNormalizer.Build"/> から生成される immutable 構造体。
/// 3.4 のエッジ生成で「way の各参照ノードが頂点なら次のエッジを切る、シェイプ点なら shape に追加」
/// の判定に用いる。
/// </remarks>
internal sealed class VertexAssignment
{
    private readonly Dictionary<long, int> _osmToVertex;

    internal VertexAssignment(Dictionary<long, int> osmToVertex)
    {
        ArgumentNullException.ThrowIfNull(osmToVertex);
        _osmToVertex = osmToVertex;
    }

    /// <summary>頂点総数。</summary>
    public int VertexCount => _osmToVertex.Count;

    /// <summary>OSM Node ID が頂点に採用されたかを返す。</summary>
    public bool IsVertex(long osmNodeId) => _osmToVertex.ContainsKey(osmNodeId);

    /// <summary>OSM Node ID に対応する頂点 ID を返す（存在しない場合は false）。</summary>
    public bool TryGetVertexId(long osmNodeId, out int vertexId) =>
        _osmToVertex.TryGetValue(osmNodeId, out vertexId);

    /// <summary>頂点に採用された OSM Node ID 列を返す（頂点 ID 昇順）。</summary>
    public IEnumerable<long> VertexOsmIds => _osmToVertex.Keys;
}
