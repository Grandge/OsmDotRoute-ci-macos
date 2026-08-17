using OsmDotRoute.Geometry;
using OsmDotRoute.Profiles;

namespace OsmDotRoute.Routing;

/// <summary>
/// 経路計算エンジンが依存する道路グラフ抽象。
/// Phase 1: <c>ItineroRoadGraph</c>（Itinero RouterDb ラップ）、Phase 2 以降: 独自バイナリグラフ実装に差し替え可能。
/// </summary>
/// <remarks>
/// <see cref="IDisposable"/> 継承（Phase 3 ステップ 3A.3e）: <c>NativeRoadGraph</c> が MMF / SafeBuffer を保持するため
/// 明示的解放が必要。<c>ItineroRoadGraph</c> 側は no-op <c>Dispose</c> 実装。
/// </remarks>
internal interface IRoadGraph : IDisposable
{
    /// <summary>頂点数</summary>
    uint VertexCount { get; }

    /// <summary>辺数（無向重複なし）。Itinero に合わせて <c>long</c>。</summary>
    long EdgeCount { get; }

    /// <summary>グラフ全体の経緯度範囲（AABB）。頂点列挙して計算するため初回呼び出しは O(V)。</summary>
    GeoBounds GetBounds();

    /// <summary>指定頂点 ID の緯度経度座標を取得する。</summary>
    GeoCoordinate GetVertex(uint vertexId);

    /// <summary>
    /// 指定頂点から出るエッジを列挙するエニュメレータを取得する。
    /// 列挙中は <see cref="IRoadGraphEdgeEnumerator.MoveNext"/> で次へ進める。
    /// </summary>
    IRoadGraphEdgeEnumerator GetEdgeEnumerator(uint vertexId);

    /// <summary>
    /// エニュメレータが指す現在エッジを、指定 <see cref="ProfileEvaluator"/> で評価する
    /// （ホットパス用、Dijkstra 近傍展開で使用）。
    /// Phase 1 → Phase 3 セマンティック移行 (Phase 3 ステップ 3A.3b)。
    /// </summary>
    /// <remarks>
    /// Itinero 系: 内部で OSM タグを取得し <c>evaluator.Evaluate(tags)</c> を呼ぶ。
    /// Native 系: <c>evaluator.Name</c> で <c>.odrg</c> の BAKED_PROFILE スロットを解決し、bake 済値を直接返却。
    /// </remarks>
    EdgeEvaluation EvaluateEdge(IRoadGraphEdgeEnumerator en, ProfileEvaluator evaluator);

    /// <summary>
    /// エッジ ID で直接取得した <see cref="RoadEdge"/> を、指定 <see cref="ProfileEvaluator"/> で評価する
    /// （スナップエッジ評価用、Dijkstra 開始時の sourceEdge/targetEdge 評価で使用）。
    /// </summary>
    EdgeEvaluation EvaluateEdge(RoadEdge edge, ProfileEvaluator evaluator);

    /// <summary>
    /// 指定エッジ ID のエッジ情報（端点・距離・プロファイル index・シェイプ）を取得する。
    /// Phase 1 ではスナップ結果（<see cref="SnapResult.EdgeId"/>）から経路探索の起点・終点情報を取得するためと、
    /// 経路復元（<see cref="RouteBuilder"/>）でシェイプを取り出すために使用する。
    /// </summary>
    RoadEdge GetEdge(uint edgeId);

    /// <summary>
    /// 指定エッジ ID の中間シェイプ点（端点を含まない）を <see cref="ReadOnlySpan{T}"/> で取得する
    /// （Phase 3 ステップ 3A.3e、Phase 1 §18.4 経路 1 本あたり 77 MB アロケート削減の土台）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 返却 Span のライフタイムは <see cref="IRoadGraph"/> インスタンスの <see cref="IDisposable.Dispose"/> 呼出までと
    /// する（XML doc 契約）。<c>NativeRoadGraph</c> はゼロコピーで内部キャッシュ Span を返す。
    /// </para>
    /// <para>
    /// <c>ItineroRoadGraph</c> 側は per-call で <see cref="GeoCoordinate"/> 配列を確保し <see cref="System.MemoryExtensions.AsSpan{T}(T[])"/>
    /// で返却する（3C で <c>ItineroRoadGraph</c> 撤去時に消滅）。コピーが発生するためホットパス用途には適さない
    /// （現状の経路復元は <see cref="GetEdge"/>.<see cref="RoadEdge.Shape"/> 経由で <see cref="System.Collections.Generic.IReadOnlyList{T}"/> を使う）。
    /// </para>
    /// </remarks>
    ReadOnlySpan<GeoCoordinate> GetEdgeShape(uint edgeId);

    /// <summary>
    /// 指定 AABB と交差するエッジ ID を列挙する（Phase 3 ステップ 3B.2、動的制約 eager bake 用）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用途: <see cref="OsmDotRoute.RestrictedAreaService"/> の eager bake (制約 add 時)
    /// が制約形状の AABB をクエリしてエッジ ID 集合を取得する。bake はホットパスではないため
    /// alloc 許容、列挙順序は実装依存（呼出側は集合として扱う）。
    /// </para>
    /// <para>
    /// <c>NativeRoadGraph</c>: <see cref="OsmDotRoute.Native.NativeRTreeQuery"/> (3A.4) で R-tree クエリ、O(log E)。
    /// <c>ItineroRoadGraph</c>: 全エッジ走査 fallback（<see cref="GetEdge"/> でシェイプ + 端点から AABB 都度計算、O(E)）。
    /// 3C で <c>ItineroRoadGraph</c> 撤去予定。
    /// </para>
    /// </remarks>
    IEnumerable<uint> QueryEdgesByAabb(Aabb queryBounds);
}
