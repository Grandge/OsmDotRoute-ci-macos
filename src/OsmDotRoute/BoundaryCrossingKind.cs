namespace OsmDotRoute;

/// <summary>
/// 範囲境界との交点判定の結果種別（REQ-RTE-010、Ver. 1.3.0）。
/// </summary>
public enum BoundaryCrossingKind
{
    /// <summary>起点・終点がともに範囲内（「範囲内」）。交点・距離・所要時間は <c>null</c>。</summary>
    BothInside = 0,

    /// <summary>起点・終点がともに範囲外（「範囲外」）。交点・距離・所要時間は <c>null</c>。</summary>
    BothOutside = 1,

    /// <summary>起点 A が範囲外・終点 B が範囲内（「A」）。交点・距離・所要時間は非 <c>null</c>。</summary>
    PointAOutside = 2,

    /// <summary>終点 B が範囲外・起点 A が範囲内（「B」）。交点・距離・所要時間は非 <c>null</c>。</summary>
    PointBOutside = 3,

    /// <summary>
    /// ルート探査エラー（「ルート探査エラー」）。交点・距離・所要時間は <c>null</c>。
    /// 経路未発見・スナップ失敗のほか、端点の内外判定とルート形状が矛盾する場合
    /// （両端が範囲内なのにルートが範囲外へ膨らむ／両端が範囲外なのにルートが範囲内を通る）も本値になる。
    /// </summary>
    RouteSearchError = 4,

    /// <summary>
    /// パラメータ異常（「パラメータ異常」）。交点・距離・所要時間は <c>null</c>。
    /// 範囲 R の南北・東西が逆転している、面積がゼロ、座標が NaN／無限大、緯度経度が定義域外の場合。
    /// </summary>
    InvalidParameter = 5,
}
