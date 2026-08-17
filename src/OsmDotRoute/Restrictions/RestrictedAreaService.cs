using System.Text.Json;
using OsmDotRoute.Geometry;
using OsmDotRoute.Gml;
using OsmDotRoute.Mesh;
using OsmDotRoute.Profiles;
using OsmDotRoute.Restrictions;
using OsmDotRoute.Routing;

namespace OsmDotRoute;

/// <summary>
/// 動的制約（進入不可エリア・難所エリア）の登録・削除・一覧取得を提供するサービス（REQ-API-004）。
/// 制約変更は次回の <see cref="Router.Calculate"/> 呼び出しから即時反映される（REQ-RST-012）。
/// </summary>
/// <remarks>
/// 難所エリア（<see cref="AddDifficultyArea(GeoPolygon, string, string?)"/> 系）の <c>difficultyType</c> 引数は
/// <see cref="DifficultyTypes"/> 定数（正準小文字キー）の利用を推奨する。プロファイル評価側の照合は
/// v1.1.1 以降 case-insensitive のため <c>"Flooding"</c> 等の表記揺れでも一致するが、
/// **未定義の難所タイプはサイレントに <c>difficultyDefault</c>（既定 <c>speedFactor=1.0</c>）にフォールバック**し、
/// 速度低下が一切適用されない（REQ-PRF-014）。タイプミス検知には <see cref="VehicleProfile.HasDifficulty"/> を併用すること。
/// </remarks>
public sealed class RestrictedAreaService
{
    private readonly Dictionary<RestrictedAreaId, AreaEntry> _entries = new();
    private readonly SpatialIndex<ShapeRef> _index = new();

    // Phase 3 ステップ 3B.3 で追加: IRoadGraph 注入 + eager bake キャッシュ
    private IRoadGraph? _graph;
    private RestrictedAreaEdgeCache? _cache;

    /// <summary>新規空のサービスを作成する。</summary>
    public RestrictedAreaService()
    {
    }

    /// <summary>
    /// <see cref="IRoadGraph"/> が本サービスに注入済みかを返す（Phase 3 ステップ 3B.3、テスト・診断用）。
    /// </summary>
    internal bool IsGraphAttached => _graph != null;

    /// <summary>
    /// 動的制約のエッジ ID キャッシュ（Phase 3 ステップ 3B.3、3B.4 で <see cref="EdgeWeightCalculator"/> から参照）。
    /// graph 未注入時は <c>null</c>。
    /// </summary>
    internal RestrictedAreaEdgeCache? Cache => _cache;

    /// <summary>
    /// <see cref="IRoadGraph"/> を本サービスにアタッチし、内部キャッシュを初期化する
    /// （Phase 3 ステップ 3B.3、計画書 §4.3.2、ユーザー判断 T7=A / T9=A）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通常は <see cref="Router(RouterDb, RestrictedAreaService)"/> コンストラクタが自動的に
    /// 1 度だけ呼び出す（公開 API は不変、ユーザーは意識不要）。
    /// </para>
    /// <para>
    /// 同一 <paramref name="graph"/> による再 attach は no-op（同一サービスを複数 <see cref="Router"/>
    /// で共有可）。別 <paramref name="graph"/> による attach は <see cref="InvalidOperationException"/>
    /// （誤動作防止、T7=A）。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> が <c>null</c></exception>
    /// <exception cref="InvalidOperationException">既に別の <see cref="IRoadGraph"/> に attach 済</exception>
    internal void AttachGraph(IRoadGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (_graph != null)
        {
            if (ReferenceEquals(_graph, graph)) return;  // T7=A: 同一 graph → no-op
            throw new InvalidOperationException(
                "RestrictedAreaService は既に別の IRoadGraph に attach されています。");
        }
        _graph = graph;
        _cache = new RestrictedAreaEdgeCache();
        // 既存 _entries を全て再評価して bake
        foreach (var kv in _entries)
        {
            BakeIntoCache(kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// 指定制約をキャッシュに bake する（<see cref="AttachGraph"/> および <see cref="Register"/> から呼ぶ）。
    /// shape ごとに <see cref="IRoadGraph.QueryEdgesByAabb"/> で候補エッジ取得 → 厳密判定 →
    /// <see cref="RestrictedAreaEdgeCache"/> に格納。
    /// </summary>
    private void BakeIntoCache(RestrictedAreaId id, AreaEntry entry)
    {
        // _graph / _cache 確実に non-null (内部呼出規約: AttachGraph 済または Register 経由)
        foreach (var shape in entry.Shapes)
        {
            foreach (var edgeId in _graph!.QueryEdgesByAabb(shape.Bounds))
            {
                if (!EdgeIntersectsShape(_graph, edgeId, shape)) continue;

                if (entry.Area is BlockArea)
                {
                    _cache!.AddBlocked(id, edgeId);
                }
                else if (entry.Area is DifficultyArea diff)
                {
                    _cache!.AddDifficulty(id, diff, edgeId);
                }
            }
        }
    }

    /// <summary>
    /// エッジ全体（端点 + 中間シェイプ）が指定 <see cref="Shape"/> と交差するかを判定する
    /// （<see cref="EvaluateConstraints"/> の <see cref="EdgeIntersectsAreaShapes"/> と同セマンティクス、bake 用）。
    /// </summary>
    private static bool EdgeIntersectsShape(IRoadGraph graph, uint edgeId, Shape shape)
    {
        var edge = graph.GetEdge(edgeId);
        var from = graph.GetVertex(edge.From);
        var to = graph.GetVertex(edge.To);

        var prev = from;
        for (int i = 0; i < edge.Shape.Count; i++)
        {
            var next = edge.Shape[i];
            if (ShapeIntersectsSegment(shape, prev, next)) return true;
            prev = next;
        }
        return ShapeIntersectsSegment(shape, prev, to);
    }

    private static bool ShapeIntersectsSegment(Shape shape, GeoCoordinate p1, GeoCoordinate p2)
    {
        if (!shape.Bounds.IntersectsSegment(p1, p2)) return false;
        if (shape.Polygon is null) return true;  // メッシュ AABB は AABB 交差で確定
        return PolygonIntersection.IntersectsSegment(shape.Polygon, p1, p2);
    }

    // --- ポリゴン指定 ---

    /// <summary>ポリゴンによる進入不可エリアを登録する（REQ-RST-001）。</summary>
    /// <param name="polygon">進入不可領域を表すポリゴン</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> が <c>null</c></exception>
    public RestrictedAreaId AddBlockArea(GeoPolygon polygon, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var id = RestrictedAreaId.New();
        var area = new BlockArea(id, polygon, tag);
        var shapes = new[] { Shape.FromPolygon(polygon) };
        Register(id, area, shapes);
        return id;
    }

    /// <summary>ポリゴンによる難所エリアを登録する（REQ-RST-004）。</summary>
    /// <param name="polygon">難所領域を表すポリゴン</param>
    /// <param name="difficultyType">難所タイプ文字列（<see cref="DifficultyTypes"/> 参照）</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID</returns>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="difficultyType"/> が空文字/null（REQ-RST-007）</exception>
    public RestrictedAreaId AddDifficultyArea(GeoPolygon polygon, string difficultyType, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var id = RestrictedAreaId.New();
        var area = new DifficultyArea(id, polygon, difficultyType, tag);
        var shapes = new[] { Shape.FromPolygon(polygon) };
        Register(id, area, shapes);
        return id;
    }

    // --- メッシュコード指定 ---

    /// <summary>メッシュコードによる進入不可エリアを登録する（REQ-RST-002）。</summary>
    /// <param name="meshCode">進入不可とする単一メッシュコード</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID</returns>
    public RestrictedAreaId AddBlockArea(MeshCode meshCode, string? tag = null)
    {
        var id = RestrictedAreaId.New();
        var area = new BlockArea(id, meshCode, tag);
        var shapes = new[] { Shape.FromMesh(meshCode) };
        Register(id, area, shapes);
        return id;
    }

    /// <summary>複数メッシュコードを一括で進入不可エリアとして登録する（REQ-RST-003）。</summary>
    /// <param name="meshCodes">進入不可とするメッシュコード集合（異なる階層の混在可）</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID</returns>
    /// <exception cref="ArgumentNullException"><paramref name="meshCodes"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="meshCodes"/> が空</exception>
    public RestrictedAreaId AddBlockArea(IEnumerable<MeshCode> meshCodes, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(meshCodes);
        var id = RestrictedAreaId.New();
        var area = new BlockArea(id, meshCodes, tag);
        var shapes = area.MeshCodes!.Select(Shape.FromMesh).ToArray();
        Register(id, area, shapes);
        return id;
    }

    /// <summary>メッシュコードによる難所エリアを登録する（REQ-RST-005）。</summary>
    /// <param name="meshCode">難所領域とする単一メッシュコード</param>
    /// <param name="difficultyType">難所タイプ文字列（<see cref="DifficultyTypes"/> 参照）</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID</returns>
    /// <exception cref="ArgumentException"><paramref name="difficultyType"/> が空文字/null（REQ-RST-007）</exception>
    public RestrictedAreaId AddDifficultyArea(MeshCode meshCode, string difficultyType, string? tag = null)
    {
        var id = RestrictedAreaId.New();
        var area = new DifficultyArea(id, meshCode, difficultyType, tag);
        var shapes = new[] { Shape.FromMesh(meshCode) };
        Register(id, area, shapes);
        return id;
    }

    /// <summary>複数メッシュコードを一括で難所エリアとして登録する（REQ-RST-006）。</summary>
    /// <param name="meshCodes">難所領域とするメッシュコード集合（異なる階層の混在可）</param>
    /// <param name="difficultyType">難所タイプ文字列（<see cref="DifficultyTypes"/> 参照）</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID</returns>
    /// <exception cref="ArgumentNullException"><paramref name="meshCodes"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="meshCodes"/> が空、または <paramref name="difficultyType"/> が空文字/null（REQ-RST-007）</exception>
    public RestrictedAreaId AddDifficultyArea(IEnumerable<MeshCode> meshCodes, string difficultyType, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(meshCodes);
        var id = RestrictedAreaId.New();
        var area = new DifficultyArea(id, meshCodes, difficultyType, tag);
        var shapes = area.MeshCodes!.Select(Shape.FromMesh).ToArray();
        Register(id, area, shapes);
        return id;
    }

    // --- GML 入力（国土数値情報 KSJ アプリケーションスキーマ準拠 GML 3.2、REQ-RST-020〜028, REQ-RST-040） ---

    /// <summary>
    /// GML 文字列から進入不可エリアを一括登録する（REQ-RST-020/025）。
    /// <paramref name="mapBounds"/> 指定時は外周頂点が 1 つでも範囲内にあるフィーチャのみ採用（REQ-RST-040）。
    /// 全採用フィーチャに同一の <paramref name="tag"/> を付与する（REQ-RST-027）。
    /// </summary>
    /// <param name="gml">KSJ アプリケーションスキーマ準拠 GML 3.2 文字列</param>
    /// <param name="mapBounds">採用範囲フィルタ（REQ-RST-040）。<c>null</c> なら全フィーチャ採用</param>
    /// <param name="tag">採用フィーチャ全体に共通付与する一括削除用タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID 配列（採用フィーチャと同順）</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gml"/> が <c>null</c></exception>
    /// <exception cref="InvalidGmlException">GML が不正、xlink 参照解決失敗</exception>
    /// <exception cref="NotSupportedException"><c>&lt;gml:MultiSurface&gt;</c> 検出（REQ-RST-023）</exception>
    public RestrictedAreaId[] AddBlockAreaFromGml(string gml, MapBounds? mapBounds = null, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(gml);
        var polygons = GmlParser.ParseString(gml);
        return RegisterBlockPolygons(polygons, mapBounds, tag);
    }

    /// <summary>GML ファイルから進入不可エリアを一括登録する（REQ-RST-024/040）。</summary>
    /// <param name="filePath">GML ファイルパス</param>
    /// <param name="mapBounds">採用範囲フィルタ（REQ-RST-040）。<c>null</c> なら全フィーチャ採用</param>
    /// <param name="tag">採用フィーチャ全体に共通付与する一括削除用タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID 配列（採用フィーチャと同順）</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> が <c>null</c>/空/空白</exception>
    /// <exception cref="FileNotFoundException">ファイルが存在しない</exception>
    /// <exception cref="InvalidGmlException">GML が不正、xlink 参照解決失敗</exception>
    /// <exception cref="NotSupportedException"><c>&lt;gml:MultiSurface&gt;</c> 検出（REQ-RST-023）</exception>
    public RestrictedAreaId[] AddBlockAreaFromGmlFile(string filePath, MapBounds? mapBounds = null, string? tag = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return AddBlockAreaFromGmlStream(stream, mapBounds, tag);
    }

    /// <summary>GML Stream から進入不可エリアを一括登録する（REQ-RST-025/040）。</summary>
    /// <param name="stream">GML を含む読み取り可能 Stream</param>
    /// <param name="mapBounds">採用範囲フィルタ（REQ-RST-040）。<c>null</c> なら全フィーチャ採用</param>
    /// <param name="tag">採用フィーチャ全体に共通付与する一括削除用タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID 配列（採用フィーチャと同順）</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> が <c>null</c></exception>
    /// <exception cref="InvalidGmlException">GML が不正、xlink 参照解決失敗</exception>
    /// <exception cref="NotSupportedException"><c>&lt;gml:MultiSurface&gt;</c> 検出（REQ-RST-023）</exception>
    public RestrictedAreaId[] AddBlockAreaFromGmlStream(Stream stream, MapBounds? mapBounds = null, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var polygons = GmlParser.ParseStream(stream);
        return RegisterBlockPolygons(polygons, mapBounds, tag);
    }

    /// <summary>
    /// GML 文字列から難所エリアを一括登録する（REQ-RST-020/025/026）。
    /// <paramref name="mapBounds"/> 指定時は外周頂点が 1 つでも範囲内にあるフィーチャのみ採用（REQ-RST-040）。
    /// 全採用フィーチャに同一の <paramref name="difficultyType"/> と <paramref name="tag"/> を適用する。
    /// </summary>
    /// <param name="gml">KSJ アプリケーションスキーマ準拠 GML 3.2 文字列</param>
    /// <param name="difficultyType">採用フィーチャ全体に共通適用する難所タイプ（<see cref="DifficultyTypes"/> 参照）</param>
    /// <param name="mapBounds">採用範囲フィルタ（REQ-RST-040）。<c>null</c> なら全フィーチャ採用</param>
    /// <param name="tag">採用フィーチャ全体に共通付与する一括削除用タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID 配列（採用フィーチャと同順）</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gml"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="difficultyType"/> が空文字/null（REQ-RST-007）</exception>
    /// <exception cref="InvalidGmlException">GML が不正、xlink 参照解決失敗</exception>
    /// <exception cref="NotSupportedException"><c>&lt;gml:MultiSurface&gt;</c> 検出（REQ-RST-023）</exception>
    public RestrictedAreaId[] AddDifficultyAreaFromGml(string gml, string difficultyType, MapBounds? mapBounds = null, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(gml);
        var polygons = GmlParser.ParseString(gml);
        return RegisterDifficultyPolygons(polygons, difficultyType, mapBounds, tag);
    }

    /// <summary>GML ファイルから難所エリアを一括登録する（REQ-RST-024/026/040）。</summary>
    /// <param name="filePath">GML ファイルパス</param>
    /// <param name="difficultyType">採用フィーチャ全体に共通適用する難所タイプ（<see cref="DifficultyTypes"/> 参照）</param>
    /// <param name="mapBounds">採用範囲フィルタ（REQ-RST-040）。<c>null</c> なら全フィーチャ採用</param>
    /// <param name="tag">採用フィーチャ全体に共通付与する一括削除用タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID 配列（採用フィーチャと同順）</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> が <c>null</c>/空/空白、または <paramref name="difficultyType"/> が空文字/null（REQ-RST-007）</exception>
    /// <exception cref="FileNotFoundException">ファイルが存在しない</exception>
    /// <exception cref="InvalidGmlException">GML が不正、xlink 参照解決失敗</exception>
    /// <exception cref="NotSupportedException"><c>&lt;gml:MultiSurface&gt;</c> 検出（REQ-RST-023）</exception>
    public RestrictedAreaId[] AddDifficultyAreaFromGmlFile(string filePath, string difficultyType, MapBounds? mapBounds = null, string? tag = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return AddDifficultyAreaFromGmlStream(stream, difficultyType, mapBounds, tag);
    }

    /// <summary>GML Stream から難所エリアを一括登録する（REQ-RST-025/026/040）。</summary>
    /// <param name="stream">GML を含む読み取り可能 Stream</param>
    /// <param name="difficultyType">採用フィーチャ全体に共通適用する難所タイプ（<see cref="DifficultyTypes"/> 参照）</param>
    /// <param name="mapBounds">採用範囲フィルタ（REQ-RST-040）。<c>null</c> なら全フィーチャ採用</param>
    /// <param name="tag">採用フィーチャ全体に共通付与する一括削除用タグ（REQ-RST-010）</param>
    /// <returns>登録された制約の ID 配列（採用フィーチャと同順）</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="difficultyType"/> が空文字/null（REQ-RST-007）</exception>
    /// <exception cref="InvalidGmlException">GML が不正、xlink 参照解決失敗</exception>
    /// <exception cref="NotSupportedException"><c>&lt;gml:MultiSurface&gt;</c> 検出（REQ-RST-023）</exception>
    public RestrictedAreaId[] AddDifficultyAreaFromGmlStream(Stream stream, string difficultyType, MapBounds? mapBounds = null, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var polygons = GmlParser.ParseStream(stream);
        return RegisterDifficultyPolygons(polygons, difficultyType, mapBounds, tag);
    }

    private RestrictedAreaId[] RegisterBlockPolygons(IReadOnlyList<GeoPolygon> polygons, MapBounds? mapBounds, string? tag)
    {
        var ids = new List<RestrictedAreaId>(polygons.Count);
        foreach (var polygon in polygons)
        {
            if (!PassesMapBoundsFilter(polygon, mapBounds)) continue;
            ids.Add(AddBlockArea(polygon, tag));
        }
        return ids.ToArray();
    }

    private RestrictedAreaId[] RegisterDifficultyPolygons(IReadOnlyList<GeoPolygon> polygons, string difficultyType, MapBounds? mapBounds, string? tag)
    {
        var ids = new List<RestrictedAreaId>(polygons.Count);
        foreach (var polygon in polygons)
        {
            if (!PassesMapBoundsFilter(polygon, mapBounds)) continue;
            // 難所タイプ検証は DifficultyArea コンストラクタに委譲（REQ-RST-007）
            ids.Add(AddDifficultyArea(polygon, difficultyType, tag));
        }
        return ids.ToArray();
    }

    /// <summary>
    /// マップ範囲フィルタ（REQ-RST-040）。<paramref name="mapBounds"/> 未指定なら常に true。
    /// 指定時は外周頂点が 1 つでも範囲内（境界線上を含む）にあれば true。Hole は判定に使わない。
    /// </summary>
    private static bool PassesMapBoundsFilter(GeoPolygon polygon, MapBounds? mapBounds)
    {
        if (mapBounds is not { } bounds) return true;
        foreach (var coord in polygon.OuterBoundary)
        {
            if (bounds.Contains(coord)) return true;
        }
        return false;
    }

    // --- 削除 ---

    /// <summary>指定 ID の制約を削除する（REQ-RST-008）。存在しない ID は何もしない。</summary>
    /// <param name="id">削除対象の制約 ID</param>
    public void Remove(RestrictedAreaId id)
    {
        if (_entries.Remove(id))
        {
            _index.RemoveAll(s => s.Id.Equals(id));
            _cache?.RemoveArea(id);  // Phase 3 ステップ 3B.3 追加
        }
    }

    /// <summary>指定タグの制約を一括削除する（REQ-RST-010）。<c>null</c> 渡しは例外。</summary>
    /// <param name="tag">削除対象のタグ（完全一致）</param>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> が <c>null</c></exception>
    public void RemoveByTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var targets = _entries
            .Where(kv => string.Equals(kv.Value.Area.Tag, tag, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToArray();
        foreach (var id in targets)
        {
            _entries.Remove(id);
        }
        if (targets.Length > 0)
        {
            var idSet = new HashSet<RestrictedAreaId>(targets);
            _index.RemoveAll(s => idSet.Contains(s.Id));
            if (_cache != null)  // Phase 3 ステップ 3B.3 追加
            {
                foreach (var id in targets) _cache.RemoveArea(id);
            }
        }
    }

    /// <summary>全制約を一括クリアする（REQ-RST-009）。</summary>
    public void ClearAll()
    {
        _entries.Clear();
        _index.Clear();
        _cache?.Clear();  // Phase 3 ステップ 3B.3 追加
    }

    // --- 一覧 ---

    /// <summary>登録済み制約の一覧を読み取り専用ビューで取得する（REQ-RST-011）。</summary>
    /// <returns>登録済み制約のスナップショット（登録順保証なし）</returns>
    public IReadOnlyList<RestrictedArea> ListAll()
    {
        return _entries.Values.Select(e => e.Area).ToArray();
    }

    // --- JSON 永続化（保存 / 読込） ---

    private const string JsonFormatId = "osmdotroute-restrictions";
    private const int JsonFormatVersion = 1;

    /// <summary>
    /// 登録済み制約を JSON 文字列にシリアライズする。形式は Sandbox デモの保存形式と互換
    /// （<c>{ "format": "osmdotroute-restrictions", "version": 1, "items": [...] }</c>）。
    /// </summary>
    /// <remarks>ポリゴンの穴（<see cref="GeoPolygon.Holes"/>）は保存形式に含まれない（外周のみ保存）。</remarks>
    /// <returns>制約一覧を表す JSON 文字列</returns>
    public string ToJsonString()
        => JsonSerializer.Serialize(BuildDocument(), RestrictionJsonContext.Default.RestrictionDocument);

    /// <summary>登録済み制約を JSON として Stream へ書き出す（<see cref="ToJsonString"/> 参照）。</summary>
    /// <param name="stream">書き込み可能 Stream</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> が <c>null</c></exception>
    public void SaveToJsonStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        JsonSerializer.Serialize(stream, BuildDocument(), RestrictionJsonContext.Default.RestrictionDocument);
    }

    /// <summary>登録済み制約を JSON ファイルへ保存する（<see cref="ToJsonString"/> 参照）。</summary>
    /// <param name="filePath">保存先ファイルパス</param>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> が null または空</exception>
    public void SaveToJsonFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.Create(filePath);
        SaveToJsonStream(stream);
    }

    /// <summary>
    /// JSON 文字列から制約を一括追加する（<see cref="ToJsonString"/> の逆操作）。
    /// 既存の制約は保持され、追加分の ID は新規採番される（置換したい場合は事前に <see cref="ClearAll"/>）。
    /// </summary>
    /// <param name="json">制約一覧を表す JSON 文字列</param>
    /// <returns>追加された制約の ID 配列（items と同順）</returns>
    /// <exception cref="ArgumentException"><paramref name="json"/> が null または空</exception>
    /// <exception cref="FormatException">format / version が非対応、または item の内容が不正</exception>
    /// <exception cref="JsonException">JSON のパースに失敗</exception>
    public RestrictedAreaId[] AddFromJsonString(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return RegisterDocument(JsonSerializer.Deserialize(json, RestrictionJsonContext.Default.RestrictionDocument));
    }

    /// <summary>JSON Stream から制約を一括追加する（<see cref="AddFromJsonString"/> 参照）。</summary>
    /// <param name="stream">JSON を含む読み取り可能 Stream</param>
    /// <returns>追加された制約の ID 配列</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> が <c>null</c></exception>
    /// <exception cref="FormatException">format / version が非対応、または item の内容が不正</exception>
    /// <exception cref="JsonException">JSON のパースに失敗</exception>
    public RestrictedAreaId[] AddFromJsonStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return RegisterDocument(JsonSerializer.Deserialize(stream, RestrictionJsonContext.Default.RestrictionDocument));
    }

    /// <summary>JSON ファイルから制約を一括追加する（<see cref="AddFromJsonString"/> 参照）。</summary>
    /// <param name="filePath">読み込む JSON ファイルパス</param>
    /// <returns>追加された制約の ID 配列</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> が null または空</exception>
    /// <exception cref="FileNotFoundException">ファイルが存在しない</exception>
    /// <exception cref="FormatException">format / version が非対応、または item の内容が不正</exception>
    /// <exception cref="JsonException">JSON のパースに失敗</exception>
    public RestrictedAreaId[] AddFromJsonFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("制約 JSON ファイルが見つかりません。", filePath);
        }
        using var stream = File.OpenRead(filePath);
        return AddFromJsonStream(stream);
    }

    private RestrictionDocument BuildDocument()
        => new(JsonFormatId, JsonFormatVersion, ListAll().Select(ToRecord).ToArray());

    private static RestrictionRecord ToRecord(RestrictedArea area)
    {
        var (kind, difficultyType) = area switch
        {
            BlockArea => ("block", (string?)null),
            DifficultyArea d => ("difficulty", d.DifficultyType),
            _ => throw new NotSupportedException($"未対応の制約種別: {area.GetType().Name}"),
        };
        var polygon = (area as BlockArea)?.Polygon ?? (area as DifficultyArea)?.Polygon;
        var meshCodes = (area as BlockArea)?.MeshCodes ?? (area as DifficultyArea)?.MeshCodes;

        return polygon is not null
            ? new RestrictionRecord(kind, difficultyType, "polygon", null,
                polygon.OuterBoundary.Select(c => new CoordinateRecord(c.Latitude, c.Longitude)).ToArray(), area.Tag)
            : new RestrictionRecord(kind, difficultyType, "mesh",
                meshCodes!.Select(m => m.Value).ToArray(), null, area.Tag);
    }

    private RestrictedAreaId[] RegisterDocument(RestrictionDocument? doc)
    {
        if (doc is null)
        {
            throw new FormatException("制約 JSON が null です。");
        }
        if (!string.Equals(doc.Format, JsonFormatId, StringComparison.Ordinal))
        {
            throw new FormatException($"未対応の format です（期待: {JsonFormatId}, 受信: {doc.Format}）。");
        }
        if (doc.Version != JsonFormatVersion)
        {
            throw new FormatException($"未対応の version です（期待: {JsonFormatVersion}, 受信: {doc.Version}）。");
        }
        if (doc.Items is null || doc.Items.Length == 0)
        {
            return [];
        }

        var ids = new RestrictedAreaId[doc.Items.Length];
        for (var i = 0; i < doc.Items.Length; i++)
        {
            ids[i] = RegisterRecord(doc.Items[i], i);
        }
        return ids;
    }

    private RestrictedAreaId RegisterRecord(RestrictionRecord rec, int index)
    {
        var kind = (rec.Kind ?? "").Trim().ToLowerInvariant();
        var shape = (rec.ShapeType ?? "").Trim().ToLowerInvariant();

        switch (shape)
        {
            case "mesh":
                if (rec.MeshCodes is null || rec.MeshCodes.Length == 0)
                {
                    throw new FormatException($"items[{index}]: shapeType=mesh には meshCodes が必要です。");
                }
                var codes = rec.MeshCodes.Select(v => new MeshCode(v)).ToArray();
                return kind switch
                {
                    "block" => AddBlockArea(codes, rec.Tag),
                    "difficulty" => AddDifficultyArea(codes, RequireDifficultyType(rec.DifficultyType, index), rec.Tag),
                    _ => throw new FormatException($"items[{index}]: kind は block / difficulty のいずれか (受信: {rec.Kind})"),
                };

            case "polygon":
                if (rec.OuterBoundary is null || rec.OuterBoundary.Length < 3)
                {
                    throw new FormatException($"items[{index}]: shapeType=polygon には 3 頂点以上の outerBoundary が必要です。");
                }
                var polygon = new GeoPolygon(rec.OuterBoundary.Select(c => new GeoCoordinate(c.Latitude, c.Longitude)).ToArray());
                return kind switch
                {
                    "block" => AddBlockArea(polygon, rec.Tag),
                    "difficulty" => AddDifficultyArea(polygon, RequireDifficultyType(rec.DifficultyType, index), rec.Tag),
                    _ => throw new FormatException($"items[{index}]: kind は block / difficulty のいずれか (受信: {rec.Kind})"),
                };

            default:
                throw new FormatException($"items[{index}]: shapeType は mesh / polygon のいずれか (受信: {rec.ShapeType})");
        }
    }

    private static string RequireDifficultyType(string? difficultyType, int index)
        => string.IsNullOrWhiteSpace(difficultyType)
            ? throw new FormatException($"items[{index}]: kind=difficulty には difficultyType が必要です。")
            : difficultyType;

    // --- internal クエリ API（Step 9 の EdgeWeightCalculator から使用） ---

    /// <summary>線分と AABB が交差する候補制約（重複除去済み）を返す。</summary>
    internal IEnumerable<RestrictedArea> QueryCandidates(GeoCoordinate p1, GeoCoordinate p2)
    {
        var seen = new HashSet<RestrictedAreaId>();
        foreach (var s in _index.Query(p1, p2))
        {
            if (seen.Add(s.Id))
            {
                yield return s.Area;
            }
        }
    }

    /// <summary>クエリ AABB と交差する候補制約（重複除去済み）を返す。</summary>
    internal IEnumerable<RestrictedArea> QueryCandidates(Aabb queryBounds)
    {
        var seen = new HashSet<RestrictedAreaId>();
        foreach (var s in _index.Query(queryBounds))
        {
            if (seen.Add(s.Id))
            {
                yield return s.Area;
            }
        }
    }

    /// <summary>指定 ID の制約が登録済みかを返す（テスト・診断用）。</summary>
    internal bool Contains(RestrictedAreaId id) => _entries.ContainsKey(id);

    /// <summary>
    /// 与えられたエッジシェイプに沿って、登録済み制約を AABB プリフィルタ → 厳密判定で評価し、
    /// 結合速度低下係数を返す（REQ-RST-013〜015, REQ-RST-030〜032）。
    /// </summary>
    /// <param name="edgeShape">エッジ全体の連続座標列（端点 + 中間シェイプ）。要素数 &lt; 2 のときは制約効果なし</param>
    /// <param name="evaluator">プロファイル評価器。難所タイプから speedFactor/canPass を導出する</param>
    /// <returns>
    /// 結合 speedFactor（全該当 <see cref="DifficultyArea"/> の <c>speedFactor</c> の積）。
    /// <see cref="BlockArea"/> 交差、または難所評価で <c>canPass=false</c> が含まれる場合は
    /// <see cref="double.PositiveInfinity"/>（短絡評価）。制約効果なしのときは 1.0。
    /// </returns>
    internal double EvaluateConstraints(IReadOnlyList<GeoCoordinate> edgeShape, ProfileEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(edgeShape);
        ArgumentNullException.ThrowIfNull(evaluator);

        if (edgeShape.Count < 2) return 1.0;
        if (_entries.Count == 0) return 1.0;

        var edgeAabb = Aabb.FromCoordinates(edgeShape);
        var seenIds = new HashSet<RestrictedAreaId>();
        var combined = 1.0;

        foreach (var sr in _index.Query(edgeAabb))
        {
            if (!seenIds.Add(sr.Id)) continue;

            // ID 単位の厳密判定: 当該 ID の全 Shape を見て、いずれかと交差すれば「ヒット」
            if (!EdgeIntersectsAreaShapes(edgeShape, _entries[sr.Id].Shapes)) continue;

            if (sr.Area is BlockArea) return double.PositiveInfinity;          // REQ-RST-032
            if (sr.Area is DifficultyArea diff)
            {
                var ev = evaluator.EvaluateDifficulty(diff.DifficultyType);
                if (!ev.CanPass) return double.PositiveInfinity;                // REQ-RST-031
                combined *= ev.SpeedFactor;
                if (combined <= 0.0) return double.PositiveInfinity;
            }
        }

        return combined;
    }

    /// <summary>
    /// エッジシェイプの全セグメントに対し、当該制約 ID の全 Shape のいずれかと交差するかを判定する。
    /// Shape ごとに AABB プリフィルタ → ポリゴン版は <see cref="PolygonIntersection.IntersectsSegment"/>、
    /// メッシュ版は AABB 交差のみ（REQ-RST-015）で確定。
    /// </summary>
    private static bool EdgeIntersectsAreaShapes(IReadOnlyList<GeoCoordinate> edgeShape, Shape[] shapes)
    {
        for (var i = 0; i < edgeShape.Count - 1; i++)
        {
            var p1 = edgeShape[i];
            var p2 = edgeShape[i + 1];
            foreach (var s in shapes)
            {
                if (!s.Bounds.IntersectsSegment(p1, p2)) continue;
                if (s.Polygon is null) return true;     // メッシュ AABB は AABB 交差で確定
                if (PolygonIntersection.IntersectsSegment(s.Polygon, p1, p2)) return true;
            }
        }
        return false;
    }

    private void Register(RestrictedAreaId id, RestrictedArea area, Shape[] shapes)
    {
        var entry = new AreaEntry(area, shapes);
        _entries[id] = entry;
        foreach (var s in shapes)
        {
            _index.Add(s.Bounds, new ShapeRef(id, area, s));
        }
        // Phase 3 ステップ 3B.3 追加: graph 注入済なら即時 bake
        if (_cache != null)
        {
            BakeIntoCache(id, entry);
        }
    }

    private sealed record AreaEntry(RestrictedArea Area, Shape[] Shapes);

    /// <summary>
    /// 1 つの制約 ID 内に複数存在しうる空間形状の単位（ポリゴン本体 1 つ、またはメッシュ AABB 1 つ）。
    /// </summary>
    private readonly record struct Shape(Aabb Bounds, GeoPolygon? Polygon)
    {
        public bool IsMesh => Polygon is null;

        public static Shape FromPolygon(GeoPolygon polygon)
            => new(PolygonIntersection.ComputeBoundingBox(polygon), polygon);

        public static Shape FromMesh(MeshCode meshCode)
            => new(MeshCodeConverter.ToBoundingBox(meshCode), null);
    }

    private readonly record struct ShapeRef(RestrictedAreaId Id, RestrictedArea Area, Shape Shape);
}
