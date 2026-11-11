using System;
using System.Collections.Generic;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// 道路 way を <see cref="VertexAssignment"/> に従って頂点境界で分割し、エッジ列に変換する。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 3.4。仕様書 §4.2 Edge Table と §4.3 Edge Shape Buffer に対応する
/// 構造的エッジレコード <see cref="EdgeRecord"/> を生成する。
/// </para>
/// <para>
/// 双方向 / 一方通行 の判定は 3.5 (エッジフラグ bake) で実施するため、本段階では純構造的な
/// 分割のみ行う（仕様書 §4.5 で <c>IsOnewayForward</c> / <c>IsOnewayBackward</c> が両方
/// 立たない = 双方向、と規定）。エッジは way の向きで 1 本だけ生成する。
/// </para>
/// <para>
/// 前提: <see cref="VertexAssignment"/> はあらかじめ全道路 way を <see cref="VertexNormalizer"/>
/// に投入して構築されている必要がある。way の始端・終端は VertexNormalizer により頂点に
/// 昇格しているため、本メソッドは「始端から始まり終端で終わる」分割を保証できる。
/// </para>
/// </remarks>
internal static class EdgeGenerator
{
    /// <summary>
    /// way 1 本を頂点境界でエッジ列に分割する。
    /// </summary>
    /// <param name="way">由来 way。</param>
    /// <param name="vertexAssignment">頂点 ID 写像（<see cref="VertexNormalizer.Build"/> 出力）。</param>
    /// <param name="stringTable">tag インデックスの解決元（way と同じ PrimitiveBlock）。</param>
    /// <returns>
    /// エッジ列。空 way・退化 way・全ノードが非頂点だった way（VertexNormalizer 未投入の異常系）
    /// では空列。閉ループ way では <see cref="EdgeRecord.FromVertexId"/> == <see cref="EdgeRecord.ToVertexId"/>
    /// の自己ループエッジが 1 本生成される。
    /// </returns>
    public static List<EdgeRecord> SplitWay(
        OsmWay way,
        VertexAssignment vertexAssignment,
        OsmStringTable stringTable)
    {
        ArgumentNullException.ThrowIfNull(way);
        ArgumentNullException.ThrowIfNull(vertexAssignment);
        ArgumentNullException.ThrowIfNull(stringTable);

        var edges = new List<EdgeRecord>();
        long[] refs = way.NodeRefs;
        if (refs.Length < 2)
            return edges;

        int i = 0;
        // 最初の頂点までスキップ（通常 i=0 が頂点だが、防御的に）
        while (i < refs.Length && !vertexAssignment.TryGetVertexId(refs[i], out _))
            i++;

        while (i < refs.Length - 1)
        {
            int fromVertexId = vertexAssignment.TryGetVertexId(refs[i], out int fid) ? fid : -1;
            if (fromVertexId < 0)
                break;  // 異常系：頂点でない位置に着いた

            // 次の頂点まで走査
            int j = i + 1;
            while (j < refs.Length && !vertexAssignment.TryGetVertexId(refs[j], out _))
                j++;

            if (j >= refs.Length)
                break;  // 次の頂点が見つからない（way 末尾までシェイプ点だけ）

            int toVertexId = vertexAssignment.TryGetVertexId(refs[j], out int tid) ? tid : -1;
            int shapeLen = j - i - 1;
            long[] shape = shapeLen == 0 ? Array.Empty<long>() : new long[shapeLen];
            if (shapeLen > 0)
                Array.Copy(refs, i + 1, shape, 0, shapeLen);

            edges.Add(new EdgeRecord(
                OsmWayId: way.Id,
                FromVertexId: fromVertexId,
                ToVertexId: toVertexId,
                ShapeNodeRefs: shape,
                TagKeys: way.TagKeys,
                TagValues: way.TagValues,
                StringTable: stringTable));

            i = j;
        }

        return edges;
    }
}
