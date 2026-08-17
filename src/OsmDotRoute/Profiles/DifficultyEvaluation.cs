namespace OsmDotRoute.Profiles;

/// <summary>
/// プロファイルによる難所タイプ評価結果（REQ-PRF-011, REQ-PRF-014）。
/// </summary>
/// <param name="SpeedFactor">速度低下係数 (0.0〜1.0)。1.0 が通常、0.0 は通行不可レベル</param>
/// <param name="CanPass">通行可能か（false なら難所として通行不可、短絡評価の対象）</param>
internal readonly record struct DifficultyEvaluation(float SpeedFactor, bool CanPass);
