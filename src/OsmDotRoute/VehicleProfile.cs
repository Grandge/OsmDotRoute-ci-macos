using System.Text.Json;
using OsmDotRoute.Profiles;

namespace OsmDotRoute;

/// <summary>
/// 車両プロファイル。JSON 外部ファイルで定義され、リビルドなしにパラメータ調整可能（REQ-PRF-007）。
/// 同梱プロファイル <see cref="Car"/> / <see cref="Pedestrian"/> は埋込リソースから遅延ロードされる（REQ-PRF-008）。
/// ユーザー独自プロファイルは <see cref="LoadFromJsonFile"/> / <see cref="LoadFromJsonString"/> /
/// <see cref="LoadFromJsonStream"/> で読み込む（REQ-PRF-009）。
/// </summary>
public sealed class VehicleProfile
{
    private static readonly Lazy<VehicleProfile> CarLazy = new(() => LoadEmbedded("car.json"));
    private static readonly Lazy<VehicleProfile> PedestrianLazy = new(() => LoadEmbedded("pedestrian.json"));
    private static readonly Lazy<VehicleProfile> BicycleLazy = new(() => LoadEmbedded("bicycle.json"));
    private static readonly Lazy<VehicleProfile> TruckLazy = new(() => LoadEmbedded("truck.json"));
    private static readonly Lazy<VehicleProfile> AmbulanceLazy = new(() => LoadEmbedded("ambulance.json"));
    private static readonly Lazy<VehicleProfile> FireEngineLazy = new(() => LoadEmbedded("fire_engine.json"));
    private static readonly Lazy<VehicleProfile> DisasterLazy = new(() => LoadEmbedded("disaster.json"));

    private readonly JsonProfileDefinition _definition;
    private readonly ProfileEvaluator _evaluator;

    private VehicleProfile(JsonProfileDefinition definition, ProfileEvaluator evaluator)
    {
        _definition = definition;
        _evaluator = evaluator;
        Name = definition.Name!;
    }

    /// <summary>プロファイル名（例: "car", "pedestrian"）</summary>
    public string Name { get; }

    /// <summary>同梱の自動車プロファイル。Profiles/car.json から遅延ロード（埋込リソース）。</summary>
    public static VehicleProfile Car => CarLazy.Value;

    /// <summary>同梱の歩行者プロファイル。Profiles/pedestrian.json から遅延ロード（埋込リソース）。</summary>
    public static VehicleProfile Pedestrian => PedestrianLazy.Value;

    /// <summary>
    /// 同梱の自転車プロファイル（REQ-PRF-003、Phase 3 ステップ 3D.1）。
    /// Profiles/bicycle.json から遅延ロード（埋込リソース）。
    /// 平均 15 km/h、cycleway/path 優先、motorway/trunk 通行不可。
    /// </summary>
    public static VehicleProfile Bicycle => BicycleLazy.Value;

    /// <summary>
    /// 同梱の 10t トラックプロファイル（REQ-PRF-004、Phase 3 ステップ 3D.3）。
    /// Profiles/truck.json から遅延ロード（埋込リソース）。
    /// 日本道路法ベース：車両総重量 20t / 全高 3.8m / 全幅 2.5m。hgv/maxweight/maxheight/maxwidth 評価、
    /// living_street/track/footway は通行不可または徐行扱い。
    /// </summary>
    public static VehicleProfile Truck => TruckLazy.Value;

    /// <summary>
    /// 同梱の救急車プロファイル（REQ-PRF-005、Phase 4）。
    /// Profiles/ambulance.json から遅延ロード（埋込リソース）。
    /// 緊急走行特例：一方通行逆走可（ignoreOneway）、emergency アクセスタグ評価、歩道(footway/path)も低速通行可。
    /// 小型寸法：車両総重量 4.0t / 全高 2.6m / 全幅 2.0m（高規格救急車相当）。難所耐性は car より高め（landslide は通行不可）。
    /// </summary>
    public static VehicleProfile Ambulance => AmbulanceLazy.Value;

    /// <summary>
    /// 同梱の消防車プロファイル（REQ-PRF-005、Phase 4）。
    /// Profiles/fire_engine.json から遅延ロード（埋込リソース）。
    /// 緊急走行特例：一方通行逆走可（ignoreOneway）、emergency/hgv アクセスタグ評価、歩道(footway/path)も徐行通行可。
    /// 大型寸法：車両総重量 8.0t / 全高 2.9m / 全幅 2.1m（水槽付消防ポンプ車相当）。難所耐性は <see cref="Ambulance"/> より控えめ（大型ゆえ冠水・渋滞に弱い）、landslide は通行不可。
    /// </summary>
    public static VehicleProfile FireEngine => FireEngineLazy.Value;

    /// <summary>
    /// 同梱の災害用車両プロファイル（REQ-PRF-006 disaster、Phase 4）。
    /// Profiles/disaster.json から遅延ロード（埋込リソース）。
    /// 災害対策基本法の緊急通行車両（重機含む）相当。寸法は truck 同等（20t / 3.8m / 2.5m）だが、
    /// 難所耐性を強化（flooding/liquefaction/construction/obstacle の speedFactor を高め）。
    /// 災害規制区間の動的な指定は上位レイヤー（RestrictedArea 付け外し）の責務とし、ignoreOneway は false。landslide は通行不可。
    /// </summary>
    public static VehicleProfile Disaster => DisasterLazy.Value;

    /// <summary>内部評価器（Dijkstra・難所判定で使用）</summary>
    internal ProfileEvaluator Evaluator => _evaluator;

    /// <summary>
    /// このプロファイルが <c>difficulty</c> セクションで定義する難所タイプキーの一覧（v1.1.1、観測性 API）。
    /// </summary>
    /// <remarks>
    /// 利用者は起動時等にこのコレクションを確認することで、<see cref="RestrictedAreaService.AddDifficultyArea(GeoPolygon, string, string?)"/> 等で
    /// 渡そうとしている難所タイプキーが当該プロファイルで効くか（speedFactor が適用されるか）を事前検証できる。
    /// 該当タイプが未定義の場合、<c>difficultyDefault</c>（既定 <c>speedFactor=1.0</c>）にフォールバックし**速度低下が発生しない**（REQ-PRF-014）。
    /// 照合は case-insensitive のため、要素は JSON 定義のままの表記で返るが、含有判定には <see cref="HasDifficulty"/> を推奨する。
    /// </remarks>
    public IReadOnlyCollection<string> KnownDifficultyTypes => _evaluator.KnownDifficultyTypes;

    /// <summary>
    /// 指定した難所タイプがこのプロファイルで明示的に定義されているかを返す（v1.1.1、観測性 API、case-insensitive 照合）。
    /// </summary>
    /// <remarks>
    /// 戻り値が <c>false</c> のとき、その難所タイプを <see cref="RestrictedAreaService.AddDifficultyArea(GeoPolygon, string, string?)"/> 等で
    /// 登録しても <see cref="ProfileEvaluator"/> 内部で <c>difficultyDefault</c> にフォールバックし**速度低下が一切適用されない**（REQ-PRF-014）。
    /// 「冠水エリアを置いたのにアニメ上で減速しない」等のサイレント無効化を事前検知する用途で利用する。
    /// </remarks>
    /// <param name="difficultyType">難所タイプ文字列（<see cref="DifficultyTypes"/> 参照）。<c>null</c> / 空 / 空白のみは <c>false</c></param>
    public bool HasDifficulty(string difficultyType) => _evaluator.HasDifficulty(difficultyType);

    /// <summary>
    /// JSON ファイルからユーザー定義プロファイルを読み込む（REQ-PRF-009）。
    /// </summary>
    /// <param name="filePath">JSON ファイルパス</param>
    /// <returns>ロードされた <see cref="VehicleProfile"/> インスタンス</returns>
    /// <exception cref="ArgumentException">パスが null または空</exception>
    /// <exception cref="FileNotFoundException">ファイルが存在しない</exception>
    /// <exception cref="InvalidProfileException">JSON 形式不正または検証失敗</exception>
    public static VehicleProfile LoadFromJsonFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("ファイルパスを指定してください。", nameof(filePath));
        }
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("プロファイル JSON ファイルが見つかりません。", filePath);
        }

        using var stream = File.OpenRead(filePath);
        return LoadFromJsonStream(stream);
    }

    /// <summary>
    /// JSON 文字列からユーザー定義プロファイルを読み込む（REQ-PRF-009）。
    /// </summary>
    /// <param name="json">JSON 文字列</param>
    /// <returns>ロードされた <see cref="VehicleProfile"/> インスタンス</returns>
    /// <exception cref="ArgumentException">JSON が null または空</exception>
    /// <exception cref="InvalidProfileException">JSON 形式不正または検証失敗</exception>
    public static VehicleProfile LoadFromJsonString(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON 文字列を指定してください。", nameof(json));
        }

        JsonProfileDefinition? def;
        try
        {
            def = JsonSerializer.Deserialize(json, ProfileJsonContext.Default.JsonProfileDefinition);
        }
        catch (JsonException ex)
        {
            throw new InvalidProfileException("プロファイル JSON のパースに失敗しました。", ex);
        }

        if (def is null)
        {
            throw new InvalidProfileException("プロファイル JSON が null です。");
        }

        var evaluator = new ProfileEvaluator(def);
        return new VehicleProfile(def, evaluator);
    }

    /// <summary>
    /// JSON Stream からユーザー定義プロファイルを読み込む（REQ-PRF-009）。
    /// </summary>
    /// <param name="stream">JSON Stream</param>
    /// <returns>ロードされた <see cref="VehicleProfile"/> インスタンス</returns>
    /// <exception cref="ArgumentNullException">Stream が null</exception>
    /// <exception cref="InvalidProfileException">JSON 形式不正または検証失敗</exception>
    public static VehicleProfile LoadFromJsonStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        JsonProfileDefinition? def;
        try
        {
            def = JsonSerializer.Deserialize(stream, ProfileJsonContext.Default.JsonProfileDefinition);
        }
        catch (JsonException ex)
        {
            throw new InvalidProfileException("プロファイル JSON のパースに失敗しました。", ex);
        }

        if (def is null)
        {
            throw new InvalidProfileException("プロファイル JSON が null です。");
        }

        var evaluator = new ProfileEvaluator(def);
        return new VehicleProfile(def, evaluator);
    }

    private static VehicleProfile LoadEmbedded(string resourceFileName)
    {
        var assembly = typeof(VehicleProfile).Assembly;
        var resourceName = $"OsmDotRoute.Profiles.{resourceFileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"埋込リソースが見つかりません: {resourceName}");
        return LoadFromJsonStream(stream);
    }
}
