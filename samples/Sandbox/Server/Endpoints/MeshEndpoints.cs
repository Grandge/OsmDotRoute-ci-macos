using System.Text.Json;
using OsmDotRoute;
using Sandbox.Server.Contracts;

namespace Sandbox.Server.Endpoints;

public static class MeshEndpoints
{
    public static void MapMeshEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/mesh/grid?swLat=&swLon=&neLat=&neLon=&level=1km|500m|250m|125m
        app.MapGet("/api/mesh/grid", (double swLat, double swLon, double neLat, double neLon, string level) =>
        {
            var meshLevel = ParseLevel(level);
            if (meshLevel is null)
                return Results.BadRequest(new ErrorResponse("invalid_level", $"level は 1km / 500m / 250m / 125m のいずれか (受信: {level})"));

            var bounds = new MapBounds(
                new GeoCoordinate(Math.Min(swLat, neLat), Math.Min(swLon, neLon)),
                new GeoCoordinate(Math.Max(swLat, neLat), Math.Max(swLon, neLon)));

            const int hardCap = 10_000;
            var cells = new List<(MeshCode code, MapBounds bounds)>();
            foreach (var code in MeshCode.EnumerateInBounds(bounds, meshLevel.Value))
            {
                cells.Add((code, code.ToBounds()));
                if (cells.Count > hardCap)
                    return Results.BadRequest(new ErrorResponse(
                        "too_many_cells",
                        $"範囲内のメッシュ数が上限 {hardCap} を超えました。階層を粗くするか範囲を狭めてください。"));
            }

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("type", "FeatureCollection");
                w.WritePropertyName("features");
                w.WriteStartArray();
                foreach (var (code, b) in cells)
                    WriteFeature(w, code, b);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return Results.Text(System.Text.Encoding.UTF8.GetString(ms.ToArray()), "application/geo+json");
        });
    }

    private static MeshLevel? ParseLevel(string s) => s.Trim().ToLowerInvariant() switch
    {
        "1km" or "mesh3rd" or "8" => MeshLevel.Mesh3rd,
        "500m" or "halfmesh" or "9" => MeshLevel.HalfMesh,
        "250m" or "quartermesh" or "10" => MeshLevel.QuarterMesh,
        "125m" or "eighthmesh" or "11" => MeshLevel.EighthMesh,
        _ => null,
    };

    private static void WriteFeature(Utf8JsonWriter w, MeshCode code, MapBounds b)
    {
        w.WriteStartObject();
        w.WriteString("type", "Feature");
        w.WritePropertyName("properties");
        w.WriteStartObject();
        w.WriteString("meshCode", code.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteEndObject();
        w.WritePropertyName("geometry");
        w.WriteStartObject();
        w.WriteString("type", "Polygon");
        w.WritePropertyName("coordinates");
        w.WriteStartArray();
        w.WriteStartArray();
        WriteCoord(w, b.SouthWest.Longitude, b.SouthWest.Latitude);
        WriteCoord(w, b.NorthEast.Longitude, b.SouthWest.Latitude);
        WriteCoord(w, b.NorthEast.Longitude, b.NorthEast.Latitude);
        WriteCoord(w, b.SouthWest.Longitude, b.NorthEast.Latitude);
        WriteCoord(w, b.SouthWest.Longitude, b.SouthWest.Latitude);
        w.WriteEndArray();
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteCoord(Utf8JsonWriter w, double lon, double lat)
    {
        w.WriteStartArray();
        w.WriteNumberValue(lon);
        w.WriteNumberValue(lat);
        w.WriteEndArray();
    }
}
