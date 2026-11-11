using Sandbox.Server.Contracts;
using OsmRoute = OsmDotRoute.Route;

namespace Sandbox.Server.Services;

public static class GeoJsonConverter
{
    public static GeoJsonLineString ToLineString(OsmRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var shape = route.Shape.Span;
        var coords = new double[shape.Length][];
        for (var i = 0; i < shape.Length; i++)
        {
            var c = shape[i];
            coords[i] = [c.Longitude, c.Latitude];
        }
        return new GeoJsonLineString("LineString", coords);
    }
}
