using Sandbox.Server.Contracts;

namespace Sandbox.Server.Endpoints;

public static class VersionEndpoints
{
    public static void MapVersionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/version", () => new VersionResponse("OsmDotRoute.Sandbox", "1.0.0"));
    }
}
