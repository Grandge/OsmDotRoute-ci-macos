using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsmDotRoute.Profiles;

/// <summary>
/// プロファイル JSON のソース生成シリアライザコンテキスト（Phase 3 ステップ 3J.3）。
/// </summary>
/// <remarks>
/// リフレクションベースの <see cref="JsonSerializer"/> はトリミング / AOT 環境（ブラウザ WASM では
/// 既定で <c>JsonSerializerIsReflectionEnabledByDefault=false</c>）で失敗するため、
/// 同梱 / ユーザープロファイルの読込はすべて本コンテキスト経由で行う。
/// オプションは従来の <c>JsonSerializerOptions</c>（CamelCase / 末尾カンマ許容 / コメントスキップ）を踏襲。
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(JsonProfileDefinition))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;
