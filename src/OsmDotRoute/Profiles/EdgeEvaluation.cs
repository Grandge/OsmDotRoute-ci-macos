namespace OsmDotRoute.Profiles;

/// <summary>
/// プロファイルによるエッジ評価結果。
/// </summary>
/// <param name="CanPass">このエッジを通行可能か（プロファイルの観点で）</param>
/// <param name="SpeedKmh">通行速度（km/h）。<see cref="CanPass"/> が <c>false</c> の場合は意味を持たない</param>
/// <param name="Oneway">OSM タグから決定される通行方向制限</param>
internal readonly record struct EdgeEvaluation(bool CanPass, float SpeedKmh, OnewayDirection Oneway);
