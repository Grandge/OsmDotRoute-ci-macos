namespace OsmDotRoute;

/// <summary>
/// 範囲境界との交点判定の結果（REQ-RTE-010〜012、Ver. 1.3.0）。
/// </summary>
/// <param name="Kind">結果種別</param>
/// <param name="Crossing">
/// ルートと範囲境界の交点。<see cref="Kind"/> が <see cref="BoundaryCrossingKind.PointAOutside"/> または
/// <see cref="BoundaryCrossingKind.PointBOutside"/> のときのみ非 <c>null</c>、それ以外は <c>null</c>。
/// </param>
/// <param name="DistanceToOutsidePointM">
/// 交点から範囲外側の端点までのルート上距離（メートル）。<see cref="Crossing"/> と同じ条件で非 <c>null</c>。
/// </param>
/// <param name="DurationToOutsidePointSec">
/// 交点から範囲外側の端点までのルート上所要時間（秒）。<see cref="Crossing"/> と同じ条件で非 <c>null</c>。
/// </param>
public sealed record BoundaryCrossingResult(
    BoundaryCrossingKind Kind,
    GeoCoordinate? Crossing,
    double? DistanceToOutsidePointM,
    double? DurationToOutsidePointSec)
{
    /// <summary>交点を伴わない結果種別（範囲内・範囲外・エラー系）の結果を生成する。</summary>
    internal static BoundaryCrossingResult Status(BoundaryCrossingKind kind) => new(kind, null, null, null);
}
