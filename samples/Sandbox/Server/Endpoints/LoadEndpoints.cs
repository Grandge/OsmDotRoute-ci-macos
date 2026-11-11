using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using Sandbox.Server.Contracts;
using Sandbox.Server.Services;
using GeoCoord = OsmDotRoute.GeoCoordinate;

namespace Sandbox.Server.Endpoints;

public static class LoadEndpoints
{
    public static void MapLoadEndpoints(this WebApplication app)
    {
        app.MapPost("/api/load", async (LoadRequest req, SandboxState state) =>
        {
            if (string.IsNullOrWhiteSpace(req.OdrgPath))
                return Results.BadRequest(new ErrorResponse("bad_request", "odrgPath is required"));

            if (!File.Exists(req.OdrgPath))
                return Results.BadRequest(new ErrorResponse("not_found", $"File not found: {req.OdrgPath}"));

            try
            {
                var loaded = await Task.Run(() =>
                {
                    var db = RouterDb.LoadFromOdrg(req.OdrgPath);
                    var meta = OdrgReader.Read(req.OdrgPath);
                    return (RouterDb: db, Meta: meta);
                });
                var routerDb = loaded.RouterDb;
                var meta = loaded.Meta;
                var profileNames = meta.ProfileTable.ProfileNames;
                var restrictions = new RestrictedAreaService();
                var router = new Router(routerDb, restrictions);
                state.Set(routerDb, router, restrictions, req.OdrgPath, profileNames);

                var stats = routerDb.GetStatistics();
                var (swLat, swLon, neLat, neLon) = ResolveBounds(meta, stats);
                return Results.Ok(new StatsResponse(
                    stats.VertexCount,
                    stats.EdgeCount,
                    new CoordinateDto(swLat, swLon),
                    new CoordinateDto(neLat, neLon),
                    profileNames));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse("load_error", ex.Message));
            }
        });
    }

    /// <summary>
    /// 表示用 bbox を解決する。優先順位: ① ヘッダーの RequestedBbox（v0.3+、要求 bbox）→
    /// ② 全頂点 AABB（way 拡張を含む実データ範囲）。
    /// </summary>
    private static (double SwLat, double SwLon, double NeLat, double NeLon) ResolveBounds(
        OdrgReadResult meta, RouterDbStatistics fallback)
    {
        var rb = meta.Header.RequestedBbox;
        bool requestedValid = meta.Header.HasRequestedBbox
            && rb.MinLon < rb.MaxLon && rb.MinLat < rb.MaxLat;
        if (requestedValid)
            return (rb.MinLat, rb.MinLon, rb.MaxLat, rb.MaxLon);

        return ComputeVertexBounds(meta.Vertices, fallback);
    }

    private static (double SwLat, double SwLon, double NeLat, double NeLon) ComputeVertexBounds(
        GeoCoord[] vertices, RouterDbStatistics fallback)
    {
        if (vertices.Length == 0)
            return (fallback.SouthWest.Latitude, fallback.SouthWest.Longitude,
                    fallback.NorthEast.Latitude, fallback.NorthEast.Longitude);

        double minLat = double.MaxValue, minLon = double.MaxValue;
        double maxLat = double.MinValue, maxLon = double.MinValue;
        foreach (var v in vertices)
        {
            if (v.Latitude < minLat) minLat = v.Latitude;
            if (v.Latitude > maxLat) maxLat = v.Latitude;
            if (v.Longitude < minLon) minLon = v.Longitude;
            if (v.Longitude > maxLon) maxLon = v.Longitude;
        }
        return (minLat, minLon, maxLat, maxLon);
    }
}
