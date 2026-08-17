namespace OsmDotRoute;

/// <summary>
/// 組込みの難所タイプ文字列定数（REQ-PRF-012）。
/// ユーザー定義タイプは任意の英数字＋アンダースコア文字列を使用可能（REQ-PRF-013）。
/// </summary>
/// <remarks>
/// 正準キーは**小文字**。<see cref="OsmDotRoute.RestrictedAreaService.AddDifficultyArea(GeoPolygon, string, string?)"/> 等への
/// 引数渡しは本クラスの定数を使うのが最も安全。プロファイル評価側の照合は v1.1.1 以降 case-insensitive のため
/// <c>"Flooding"</c> 等の表記揺れでも一致するが、未定義タイプ（タイプミス含む）はサイレントに <c>difficultyDefault</c> へ
/// フォールバックする（REQ-PRF-014、= 速度低下が発生しない）。事前確認には
/// <see cref="OsmDotRoute.VehicleProfile.HasDifficulty"/> / <see cref="OsmDotRoute.VehicleProfile.KnownDifficultyTypes"/> を利用する。
/// </remarks>
public static class DifficultyTypes
{
    /// <summary>冠水（flooding）— 河川氾濫・内水氾濫・津波後の冠水</summary>
    public const string Flooding = "flooding";

    /// <summary>液状化（liquefaction）— 地震による液状化現象</summary>
    public const string Liquefaction = "liquefaction";

    /// <summary>土砂崩れ（landslide）— 崖崩れ・地すべり・落石による道路寸断</summary>
    public const string Landslide = "landslide";

    /// <summary>工事中（construction）— 道路工事・復旧工事による通行困難</summary>
    public const string Construction = "construction";

    /// <summary>障害物（obstacle）— 瓦礫・倒木・落下物・放置車両等</summary>
    public const string Obstacle = "obstacle";

    /// <summary>交通集中（congestion）— 避難集中・通常混雑による速度低下</summary>
    public const string Congestion = "congestion";

    /// <summary>積雪（snow）— 降雪後の未除雪区間</summary>
    public const string Snow = "snow";

    /// <summary>凍結（ice）— 路面凍結によるスリップリスク</summary>
    public const string Ice = "ice";
}
