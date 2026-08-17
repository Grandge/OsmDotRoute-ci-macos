namespace OsmDotRoute.Routing;

/// <summary>
/// 任意座標を道路ネットワーク上の最寄り点にスナップする抽象（REQ-RTE-002〜003）。
/// Phase 1: <c>ItineroSnapper</c>（Itinero <c>Router.Resolve</c> を呼び出し）、
/// Phase 2 以降: 独自空間インデックスベース実装に差し替え可能。
/// </summary>
internal interface IRoadSnapper
{
    /// <summary>
    /// 指定プロファイル名で利用可能な道路に対し、指定座標を最寄り点へスナップする。
    /// </summary>
    /// <param name="profileName">プロファイル名（例: "car", "pedestrian"）</param>
    /// <param name="point">スナップ対象の入力座標</param>
    /// <param name="searchDistanceM">スナップ検索半径（メートル）</param>
    /// <returns>
    /// スナップ結果（座標 + エッジ ID + オフセット）。
    /// プロファイル未対応、検索半径内に該当道路無し、その他失敗時は <c>null</c>。
    /// </returns>
    SnapResult? Snap(string profileName, GeoCoordinate point, float searchDistanceM);
}

/// <summary>
/// スナップ結果。経路探索（ステップ 5b）では <see cref="EdgeId"/> と <see cref="Offset"/> を Dijkstra 始点として使用する。
/// </summary>
/// <param name="Location">スナップ後の道路上座標</param>
/// <param name="EdgeId">スナップしたエッジ ID</param>
/// <param name="Offset">エッジ上の位置（0=From 頂点、65535=To 頂点）</param>
internal readonly record struct SnapResult(GeoCoordinate Location, uint EdgeId, ushort Offset);
