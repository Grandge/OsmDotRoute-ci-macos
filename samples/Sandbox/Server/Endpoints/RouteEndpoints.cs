using OsmDotRoute;
using Sandbox.Server.Contracts;
using Sandbox.Server.Services;

namespace Sandbox.Server.Endpoints;

public static class RouteEndpoints
{
    public static void MapRouteEndpoints(this WebApplication app)
    {
        app.MapPost("/api/snap", (SnapRequest req, SandboxState state) =>
        {
            var router = state.Router;
            if (router is null)
                return Results.Conflict(new ErrorResponse("not_loaded", "No graph loaded."));

            var profile = ResolveProfile(req.Profile);
            var searchDist = req.SearchDistanceM ?? 500f;
            var snapped = router.SnapToRoad(profile, new GeoCoordinate(req.Lat, req.Lon), searchDist);

            return Results.Ok(new SnapResponse(
                snapped.HasValue ? new CoordinateDto(snapped.Value.Latitude, snapped.Value.Longitude) : null));
        });

        app.MapPost("/api/route", (RouteRequest req, SandboxState state) =>
        {
            var router = state.Router;
            if (router is null)
                return Results.Conflict(new ErrorResponse("not_loaded", "No graph loaded."));

            var profile = ResolveProfile(req.Profile);
            var from = new GeoCoordinate(req.FromLat, req.FromLon);
            var to = new GeoCoordinate(req.ToLat, req.ToLon);

            var route = router.Calculate(profile, from, to);
            if (route is null)
                return Results.Ok(new RouteResponse(false, 0, 0, null));

            var geometry = GeoJsonConverter.ToLineString(route);
            return Results.Ok(new RouteResponse(true, route.TotalDistanceM, route.TotalDurationSec, geometry));
        });
    }

    private static VehicleProfile ResolveProfile(string? name) =>
        (name?.ToLowerInvariant()) switch
        {
            "pedestrian" => VehicleProfile.Pedestrian,
            "bicycle" => VehicleProfile.Bicycle,
            "truck" => VehicleProfile.Truck,
            _ => VehicleProfile.Car,
        };
}
