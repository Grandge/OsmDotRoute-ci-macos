using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsmDotRoute;

/// <summary>
/// 制約ファイル（メッシュ／ポリゴンの block / difficulty 一括）のシリアライズ用 DTO。
/// Sandbox デモの保存形式（<c>{ format, version, items }</c>）と互換。
/// </summary>
internal sealed record RestrictionDocument(
    string Format,
    int Version,
    RestrictionRecord[] Items);

/// <summary>
/// 制約 1 件分のレコード。<c>id</c> は保存形式に含まれても読込時は無視する（読込時に再採番）。
/// </summary>
internal sealed record RestrictionRecord(
    string Kind,
    string? DifficultyType,
    string ShapeType,
    long[]? MeshCodes,
    CoordinateRecord[]? OuterBoundary,
    string? Tag);

internal sealed record CoordinateRecord(double Latitude, double Longitude);

/// <summary>
/// 制約 JSON のソース生成シリアライザコンテキスト。
/// </summary>
/// <remarks>
/// リフレクションベースの <see cref="JsonSerializer"/> はトリミング / AOT 環境（ブラウザ WASM では
/// 既定で <c>JsonSerializerIsReflectionEnabledByDefault=false</c>）で失敗するため、
/// 制約の保存 / 読込はすべて本コンテキスト経由で行う（<see cref="OsmDotRoute.Profiles.ProfileJsonContext"/> と同方針）。
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(RestrictionDocument))]
internal sealed partial class RestrictionJsonContext : JsonSerializerContext;
