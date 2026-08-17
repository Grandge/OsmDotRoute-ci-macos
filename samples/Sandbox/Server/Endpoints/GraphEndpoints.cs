using Sandbox.Server.Contracts;
using Sandbox.Server.Services;

namespace Sandbox.Server.Endpoints;

public static class GraphEndpoints
{
    public static void MapGraphEndpoints(this WebApplication app)
    {
        app.MapGet("/api/graph/stats", (SandboxState state) =>
        {
            var routerDb = state.RouterDb;
            if (routerDb is null)
                return Results.Conflict(new ErrorResponse("not_loaded", "No graph loaded."));

            var stats = routerDb.GetStatistics();
            return Results.Ok(new StatsResponse(
                stats.VertexCount,
                stats.EdgeCount,
                new CoordinateDto(stats.SouthWest.Latitude, stats.SouthWest.Longitude),
                new CoordinateDto(stats.NorthEast.Latitude, stats.NorthEast.Longitude),
                state.ProfileNames));
        });

        app.MapGet("/api/road-network", (SandboxState state) =>
        {
            var router = state.Router;
            if (router is null)
                return Results.Conflict(new ErrorResponse("not_loaded", "No graph loaded."));

            var geoJson = router.GetRoadNetworkGeoJson().Json;
            return Results.Text(geoJson, "application/geo+json");
        });
    }
}
