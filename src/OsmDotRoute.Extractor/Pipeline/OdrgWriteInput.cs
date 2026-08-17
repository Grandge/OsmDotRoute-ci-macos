using System;
using OsmDotRoute;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// <see cref="OdrgWriter.Write"/> への入力。サブステップ 3.4〜3.7 で構築した抽出パイプラインの結果をまとめる。
/// </summary>
/// <remarks>
/// 配列は <see cref="StrRTree.EdgePermutation"/> を適用済み（new edge ID 順）の状態を渡すこと。
/// </remarks>
/// <param name="Vertices">頂点配列。インデックスは <c>VertexAssignment</c> の頂点 ID。</param>
/// <param name="Edges">エッジ列。new edge ID 順。</param>
/// <param name="EdgeAabbs">エッジ AABB。<paramref name="Edges"/> と同順。</param>
/// <param name="EdgeFlags">エッジフラグ。<paramref name="Edges"/> と同順。</param>
/// <param name="RTree">STR R-tree。葉の <c>FirstChildIndex</c> は new edge ID。</param>
/// <param name="ProfileTable">bake 済プロファイル表（new edge ID 順）。</param>
/// <param name="NodeCoordLookup">OSM Node ID → 座標の解決関数（エッジ shape の座標展開に使用）。</param>
/// <param name="Bbox">全体バウンディングボックス（ヘッダーに記録、抽出後の頂点 AABB）。</param>
/// <param name="MetadataJson">UTF-8 JSON 文字列。Metadata セクション (kind=0x0009) にそのまま書く。</param>
/// <param name="RequestedBbox">抽出要求時の bbox（CLI <c>--bbox</c> のユーザー入力、way 拡張前）。v0.3 以降ヘッダーに記録。未指定 (default) は <see cref="Bbox"/> と同じ値が書かれる。</param>
internal sealed record OdrgWriteInput(
    GeoCoordinate[] Vertices,
    EdgeRecord[] Edges,
    Aabb[] EdgeAabbs,
    EdgeFlags[] EdgeFlags,
    StrRTree RTree,
    BakedProfileTable ProfileTable,
    Func<long, GeoCoordinate> NodeCoordLookup,
    Aabb Bbox,
    string MetadataJson,
    Aabb RequestedBbox = default);
