using System.Text.Json.Serialization;

namespace OsmDotRoute.Profiles;

/// <summary>
/// プロファイル JSON のルート DTO。<see cref="System.Text.Json"/> でデシリアライズされる。
/// プロパティ命名は CamelCase (JsonNamingPolicy.CamelCase)。
/// </summary>
internal sealed class JsonProfileDefinition
{
    /// <summary>プロファイル名（例: "car"）</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>車両種別（情報用、例: "motor_vehicle"）</summary>
    [JsonPropertyName("vehicleType")]
    public string? VehicleType { get; set; }

    /// <summary>oneway タグを無視するか（歩行者プロファイルで true）</summary>
    [JsonPropertyName("ignoreOneway")]
    public bool IgnoreOneway { get; set; }

    /// <summary>
    /// アクセスタグキー列。配列順に評価し後ろが優先（より特化したタグを配列の後ろに置く）。
    /// 例: ["access", "vehicle", "motor_vehicle"] → access より motor_vehicle が優先
    /// </summary>
    [JsonPropertyName("accessTagKeys")]
    public List<string>? AccessTagKeys { get; set; }

    /// <summary>highway タグ別ルール</summary>
    [JsonPropertyName("highway")]
    public Dictionary<string, JsonHighwayRule>? Highway { get; set; }

    /// <summary>access タグ値マッピング (例: "yes" → "allow", "private" → "deny")</summary>
    [JsonPropertyName("accessValueMap")]
    public Dictionary<string, string>? AccessValueMap { get; set; }

    /// <summary>maxspeed タグのキー名（既定 "maxspeed"）</summary>
    [JsonPropertyName("maxspeedTagKey")]
    public string? MaxspeedTagKey { get; set; }

    /// <summary>maxspeed の単位省略時の既定 ("kmh" / "mph")</summary>
    [JsonPropertyName("maxspeedUnitDefault")]
    public string? MaxspeedUnitDefault { get; set; }

    /// <summary>highway 不明・access 不定時の fallback ルール</summary>
    [JsonPropertyName("fallback")]
    public JsonFallbackRule? Fallback { get; set; }

    /// <summary>速度の最小・最大クランプ値</summary>
    [JsonPropertyName("speedBounds")]
    public JsonSpeedBounds? SpeedBounds { get; set; }

    /// <summary>難所タイプ別ルール（REQ-PRF-011, 012, 013）</summary>
    [JsonPropertyName("difficulty")]
    public Dictionary<string, JsonDifficultyRule>? Difficulty { get; set; }

    /// <summary>未定義難所タイプの既定値（REQ-PRF-014）</summary>
    [JsonPropertyName("difficultyDefault")]
    public JsonDifficultyRule? DifficultyDefault { get; set; }

    /// <summary>
    /// 全速度値（highway 別速度、maxspeed タグ、fallback 速度）に乗じる係数。
    /// 既定 <c>1.0</c>。Itinero <c>Vehicle.Car.Fastest()</c> 相当の動作
    /// （実走平均 ≒ 法定速度 × 0.75）には <c>0.75</c> を指定する。
    /// </summary>
    [JsonPropertyName("speedMultiplier")]
    public double? SpeedMultiplier { get; set; }

    /// <summary>
    /// 車両物理制限（Phase 3 ステップ 3D.2、REQ-PRF-004 Truck 用）。
    /// OSM タグ <c>maxweight</c> / <c>maxheight</c> / <c>maxwidth</c> の数値と比較し、
    /// 制限を超過するエッジを通行不可（hard-deny）とする。null の場合は評価しない。
    /// </summary>
    [JsonPropertyName("vehicleLimits")]
    public JsonVehicleLimits? VehicleLimits { get; set; }
}

/// <summary>highway 別ルール</summary>
internal sealed class JsonHighwayRule
{
    [JsonPropertyName("speedKmh")]
    public double? SpeedKmh { get; set; }

    /// <summary>"yes" / "no" — このプロファイルで通行可能か</summary>
    [JsonPropertyName("access")]
    public string? Access { get; set; }
}

/// <summary>fallback ルール（highway 未定義時）</summary>
internal sealed class JsonFallbackRule
{
    [JsonPropertyName("speedKmh")]
    public double SpeedKmh { get; set; }

    [JsonPropertyName("access")]
    public string? Access { get; set; }
}

/// <summary>速度クランプ</summary>
internal sealed class JsonSpeedBounds
{
    [JsonPropertyName("minKmh")]
    public double MinKmh { get; set; }

    [JsonPropertyName("maxKmh")]
    public double MaxKmh { get; set; }
}

/// <summary>難所タイプ別ルール</summary>
internal sealed class JsonDifficultyRule
{
    [JsonPropertyName("speedFactor")]
    public double SpeedFactor { get; set; }

    [JsonPropertyName("canPass")]
    public bool CanPass { get; set; }
}

/// <summary>
/// 車両物理制限（Phase 3 ステップ 3D.2）。全て optional、未指定の制限は評価しない。
/// OSM 既定単位: maxWeight=t / maxHeight=m / maxWidth=m。
/// 未対応単位（kg, ft, in 等）の OSM タグ値は制限を発火させない（安全側＝通行可、計画書 T1 リスク）。
/// </summary>
internal sealed class JsonVehicleLimits
{
    /// <summary>車両総重量上限（トン）。例: 20.0 = 20t トラック。</summary>
    [JsonPropertyName("maxWeightTon")]
    public double? MaxWeightTon { get; set; }

    /// <summary>車両高さ上限（メートル）。例: 3.8 = 全高 3.8m。</summary>
    [JsonPropertyName("maxHeightMeter")]
    public double? MaxHeightMeter { get; set; }

    /// <summary>車両幅上限（メートル）。例: 2.5 = 全幅 2.5m。</summary>
    [JsonPropertyName("maxWidthMeter")]
    public double? MaxWidthMeter { get; set; }
}
