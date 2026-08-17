using System.Text.Json.Serialization;

namespace Sandbox.Wasm;

// React 側 client.ts の DTO 形状に一致させる（camelCase）。3J.5 の wasmClient.ts で
// そのままパース可能にすることで、既存 React UI を無改変で流用する。

internal sealed record CoordinateDto(double Latitude, double Longitude);

internal sealed record StatsDto(
    int VertexCount,
    int EdgeCount,
    CoordinateDto SouthWest,
    CoordinateDto NorthEast,
    string[] ProfileNames);

internal sealed record LineStringDto(string Type, double[][] Coordinates);

internal sealed record RouteDto(bool Found, double DistanceM, double DurationSec, LineStringDto? Geometry);

internal sealed record SnapDto(CoordinateDto? Snapped);

internal sealed record RouteRequestDto(double FromLat, double FromLon, double ToLat, double ToLon, string? Profile);

internal sealed record SnapRequestDto(double Lat, double Lon, string? Profile, float? SearchDistanceM);

internal sealed record PolygonRestrictionRequestDto(
    string? Kind, string? DifficultyType, CoordinateDto[]? OuterBoundary, string? Tag);

internal sealed record MeshRestrictionRequestDto(
    string? Kind, string? DifficultyType, long[]? MeshCodes, string? Tag);

internal sealed record RestrictionIdDto(string Id);

internal sealed record RestrictionItemDto(
    string Id,
    string Kind,
    string? DifficultyType,
    string ShapeType,
    CoordinateDto[]? OuterBoundary,
    long[]? MeshCodes,
    string? Tag);

internal sealed record RestrictionListDto(RestrictionItemDto[] Items);

internal sealed record GmlImportOptionsDto(
    string? Kind,
    string? DifficultyType,
    bool UseMapBounds,
    CoordinateDto? MapBoundsSouthWest,
    CoordinateDto? MapBoundsNorthEast,
    string? Tag);

internal sealed record GmlImportResultDto(string[] Ids, int AcceptedCount);

/// <summary>
/// WASM 環境では既定でトリミングが効くため、リフレクションベースの
/// <see cref="System.Text.Json.JsonSerializer"/> ではなくソース生成（trim/AOT 安全）を用いる。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatsDto))]
[JsonSerializable(typeof(RouteDto))]
[JsonSerializable(typeof(SnapDto))]
[JsonSerializable(typeof(RouteRequestDto))]
[JsonSerializable(typeof(SnapRequestDto))]
[JsonSerializable(typeof(PolygonRestrictionRequestDto))]
[JsonSerializable(typeof(MeshRestrictionRequestDto))]
[JsonSerializable(typeof(RestrictionIdDto))]
[JsonSerializable(typeof(RestrictionListDto))]
[JsonSerializable(typeof(GmlImportOptionsDto))]
[JsonSerializable(typeof(GmlImportResultDto))]
internal partial class SandboxJsonContext : JsonSerializerContext;
