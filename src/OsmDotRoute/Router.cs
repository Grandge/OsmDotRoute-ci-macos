using OsmDotRoute.GeoJson;
using OsmDotRoute.Geometry;
using OsmDotRoute.Routing;

namespace OsmDotRoute;

/// <summary>
/// OsmDotRoute の公開ファサード（REQ-API-001）。
/// 経路計算・道路スナップ・道路ネットワーク GeoJSON 出力を提供する。
/// </summary>
public sealed class Router
{
    private readonly RouterDb _routerDb;
    private readonly RestrictedAreaService? _restrictions;

    /// <summary>
    /// Router を構築する。
    /// </summary>
    /// <param name="routerDb">経路計算用グラフ</param>
    /// <param name="restrictions">動的制約サービス（null の場合は制約なし）</param>
    public Router(RouterDb routerDb, RestrictedAreaService? restrictions = null)
        : this(routerDb, restrictions, autoAttachGraph: true)
    {
    }

    /// <summary>
    /// 自動 AttachGraph 有無を指定する internal コンストラクタ（Phase 3 ステップ 3B.5、計画書 §4.5-B T15=A）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="autoAttachGraph"/> = <c>false</c> は Mode "Native-Detached" ベンチ専用。
    /// graph 未注入のまま <see cref="RestrictedAreaService"/> を Router に渡すことで、3B 効果測定の
    /// 「導入前」ベースライン (Phase 1 動作) を再現する。
    /// </para>
    /// </remarks>
    internal Router(RouterDb routerDb, RestrictedAreaService? restrictions, bool autoAttachGraph)
    {
        ArgumentNullException.ThrowIfNull(routerDb);
        _routerDb = routerDb;
        _restrictions = restrictions;
        if (autoAttachGraph)
        {
            restrictions?.AttachGraph(routerDb.Graph);
        }
    }

    /// <summary>
    /// 2 点間の最短経路を計算する（REQ-RTE-001）。
    /// 経路未発見時・ネットワーク外座標時は <c>null</c> を返す（REQ-RTE-006, REQ-RTE-008）。
    /// </summary>
    /// <param name="profile">車両プロファイル</param>
    /// <param name="from">起点座標</param>
    /// <param name="to">終点座標</param>
    /// <param name="searchDistanceM">
    /// 起点・終点を最寄り道路へスナップする検索半径（メートル、既定 500m）。
    /// この半径内に道路が無い起点／終点はスナップできず <c>null</c> を返す。
    /// </param>
    /// <returns>経路、または <c>null</c></returns>
    public Route? Calculate(
        VehicleProfile profile, GeoCoordinate from, GeoCoordinate to, float searchDistanceM = 500f)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var sourceSnap = _routerDb.Snapper.Snap(profile.Name, from, searchDistanceM);
        if (sourceSnap is null) return null;

        var targetSnap = _routerDb.Snapper.Snap(profile.Name, to, searchDistanceM);
        if (targetSnap is null) return null;

        var calculator = new EdgeWeightCalculator(_routerDb.Graph, profile.Evaluator, _restrictions);
        var engine = new DijkstraEngine(_routerDb.Graph, calculator);
        var result = engine.Run(sourceSnap.Value, targetSnap.Value);
        if (result is null) return null;

        var builder = new RouteBuilder(_routerDb.Graph);
        return builder.Build(sourceSnap.Value, targetSnap.Value, result);
    }

    /// <summary>
    /// 2 点間の経路を計算し、矩形範囲 R の境界との交点および交点から範囲外側の端点までの
    /// ルート上距離・所要時間を返す（REQ-RTE-010〜012、Ver. 1.3.0）。
    /// </summary>
    /// <param name="profile">車両プロファイル</param>
    /// <param name="from">起点 A</param>
    /// <param name="to">終点 B</param>
    /// <param name="bounds">
    /// 矩形範囲 R。北西端・南東端で指定する場合は <see cref="MapBounds.FromNorthWestSouthEast"/> を使う。
    /// 読み込み済み地図の内側の任意の矩形でよい（地図範囲と一致している必要はない）。
    /// </param>
    /// <param name="searchDistanceM">起点・終点を最寄り道路へスナップする検索半径（メートル、既定 500m）</param>
    /// <returns>
    /// 判定結果。経路未発見・スナップ失敗時は <see cref="BoundaryCrossingKind.RouteSearchError"/>、
    /// 範囲 R が不正な場合は <see cref="BoundaryCrossingKind.InvalidParameter"/> を返す（<c>null</c> は返さない）。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> が <c>null</c>。</exception>
    public BoundaryCrossingResult CalculateBoundaryCrossing(
        VehicleProfile profile,
        GeoCoordinate from,
        GeoCoordinate to,
        MapBounds bounds,
        float searchDistanceM = 500f)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!BoundaryCrossingCalculator.IsValidBounds(bounds))
        {
            return BoundaryCrossingResult.Status(BoundaryCrossingKind.InvalidParameter);
        }

        var route = Calculate(profile, from, to, searchDistanceM);
        if (route is null)
        {
            return BoundaryCrossingResult.Status(BoundaryCrossingKind.RouteSearchError);
        }

        return BoundaryCrossingCalculator.Compute(route, from, to, bounds);
    }

    /// <summary>
    /// 計算済みの経路に対して、矩形範囲 R の境界との交点判定のみを行う（REQ-RTE-013、Ver. 1.3.0）。
    /// 経路を再計算せずに判定したい場合に使う。
    /// </summary>
    /// <param name="route"><paramref name="from"/> から <paramref name="to"/> へ計算済みの経路</param>
    /// <param name="from">起点 A（<paramref name="route"/> の計算に用いた生の座標）</param>
    /// <param name="to">終点 B（同上）</param>
    /// <param name="bounds">矩形範囲 R</param>
    /// <returns>判定結果（<c>null</c> は返さない）</returns>
    /// <exception cref="ArgumentNullException"><paramref name="route"/> が <c>null</c>。</exception>
    public static BoundaryCrossingResult FindBoundaryCrossing(
        Route route, GeoCoordinate from, GeoCoordinate to, MapBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(route);
        return BoundaryCrossingCalculator.Compute(route, from, to, bounds);
    }

    /// <summary>
    /// 任意座標を最寄り道路上にスナップする（REQ-RTE-002〜003）。
    /// 道路ネットワーク外の座標は <c>null</c> を返す（REQ-RTE-008）。
    /// </summary>
    /// <param name="profile">車両プロファイル</param>
    /// <param name="point">スナップ対象座標</param>
    /// <param name="searchDistanceM">検索半径（メートル、既定 500m）</param>
    /// <returns>スナップ後座標、または <c>null</c></returns>
    public GeoCoordinate? SnapToRoad(VehicleProfile profile, GeoCoordinate point, float searchDistanceM = 500f)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var result = _routerDb.Snapper.Snap(profile.Name, point, searchDistanceM);
        return result?.Location;
    }

    /// <summary>
    /// 道路ネットワークを GeoJSON FeatureCollection（LineString 列）として出力する（REQ-RTE-004）。
    /// </summary>
    /// <param name="bounds">
    /// フィルタ用 bbox（省略可）。way 拡張で FileBbox が広がる場合は
    /// <see cref="RouterDb.GetRequestedBounds"/> の値を渡すと表示範囲を抑制できる。
    /// </param>
    public RoadNetworkGeoJson GetRoadNetworkGeoJson(
        (GeoCoordinate SouthWest, GeoCoordinate NorthEast)? bounds = null)
        => new RoadNetworkGeoJson(GeoJsonWriter.WriteRoadNetwork(_routerDb.Graph, bounds));
}
