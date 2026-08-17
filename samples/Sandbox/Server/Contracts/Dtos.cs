namespace Sandbox.Server.Contracts;

public sealed record VersionResponse(string Name, string Version);

public sealed record CoordinateDto(double Latitude, double Longitude);

public sealed record StatsResponse(
    int VertexCount,
    int EdgeCount,
    CoordinateDto SouthWest,
    CoordinateDto NorthEast,
    string[] ProfileNames);

public sealed record ErrorResponse(string Error, string Message);

public sealed record RegionResponse(string Key, string DisplayName, string Description);

public sealed record DownloadRequest(string Region);

public sealed record CachedPbfInfo(string RegionKey, string DisplayName, long SizeBytes, DateTime LastModifiedUtc);

public sealed record CacheStatusResponse(CachedPbfInfo[] Items);

public sealed record CacheDirRequest(string Path);

public sealed record CacheDirResponse(string Path);

public sealed record ExtractRequest(string PbfPath, double[]? Bbox, string[]? Profiles);

public sealed record LoadRequest(string? OdrgPath);

public sealed record SnapRequest(double Lat, double Lon, string? Profile, float? SearchDistanceM);

public sealed record SnapResponse(CoordinateDto? Snapped);

public sealed record RouteRequest(double FromLat, double FromLon, double ToLat, double ToLon, string? Profile);

public sealed record RouteResponse(bool Found, double DistanceM, double DurationSec, GeoJsonLineString? Geometry);

public sealed record GeoJsonLineString(string Type, double[][] Coordinates);

public sealed record PolygonRestrictionRequest(
    string? Kind,
    string? DifficultyType,
    CoordinateDto[]? OuterBoundary,
    string? Tag);

public sealed record MeshRestrictionRequest(
    string? Kind,
    string? DifficultyType,
    long[]? MeshCodes,
    string? Tag);

public sealed record RestrictionIdResponse(Guid Id);

public sealed record GmlImportResponse(Guid[] Ids, int AcceptedCount);

public sealed record RestrictionSaveResponse(string Path);
