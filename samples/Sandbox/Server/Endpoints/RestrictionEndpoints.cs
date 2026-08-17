using System.Text.Json;
using OsmDotRoute;
using Sandbox.Server.Contracts;
using Sandbox.Server.Services;

namespace Sandbox.Server.Endpoints;

public static class RestrictionEndpoints
{
    public static void MapRestrictionEndpoints(this WebApplication app)
    {
        // POST /api/restrictions/polygon
        app.MapPost("/api/restrictions/polygon", (PolygonRestrictionRequest? req, SandboxState state) =>
        {
            var restrictions = state.Restrictions;
            if (restrictions is null)
                return Results.Conflict(new ErrorResponse("not_loaded", "No graph loaded."));

            if (req is null || req.OuterBoundary is null || req.OuterBoundary.Length < 3)
                return Results.BadRequest(new ErrorResponse("invalid_polygon", "outerBoundary は 3 頂点以上必要です。"));

            var kind = (req.Kind ?? "").Trim().ToLowerInvariant();
            var polygon = new GeoPolygon(req.OuterBoundary.Select(c => new GeoCoordinate(c.Latitude, c.Longitude)).ToArray());

            Guid id;
            switch (kind)
            {
                case "block":
                    id = restrictions.AddBlockArea(polygon, req.Tag).Value;
                    break;
                case "difficulty":
                    if (string.IsNullOrWhiteSpace(req.DifficultyType))
                        return Results.BadRequest(new ErrorResponse("missing_difficulty_type", "kind=difficulty の場合は difficultyType が必要です。"));
                    id = restrictions.AddDifficultyArea(polygon, req.DifficultyType, req.Tag).Value;
                    break;
                default:
                    return Results.BadRequest(new ErrorResponse("invalid_kind", $"kind は block / difficulty のいずれか (受信: {req.Kind})"));
            }
            return Results.Ok(new RestrictionIdResponse(id));
        });

        // POST /api/restrictions/mesh
        app.MapPost("/api/restrictions/mesh", (MeshRestrictionRequest? req, SandboxState state) =>
        {
            var restrictions = state.Restrictions;
            if (restrictions is null)
                return Results.Conflict(new ErrorResponse("not_loaded", "No graph loaded."));

            if (req is null || req.MeshCodes is null || req.MeshCodes.Length == 0)
                return Results.BadRequest(new ErrorResponse("invalid_mesh", "meshCodes は 1 個以上必要です。"));

            var kind = (req.Kind ?? "").Trim().ToLowerInvariant();
            var codes = req.MeshCodes.Select(v => new MeshCode(v)).ToArray();

            Guid id;
            try
            {
                switch (kind)
                {
                    case "block":
                        id = codes.Length == 1
                            ? restrictions.AddBlockArea(codes[0], req.Tag).Value
                            : restrictions.AddBlockArea(codes, req.Tag).Value;
                        break;
                    case "difficulty":
                        if (string.IsNullOrWhiteSpace(req.DifficultyType))
                            return Results.BadRequest(new ErrorResponse("missing_difficulty_type", "kind=difficulty の場合は difficultyType が必要です。"));
                        id = codes.Length == 1
                            ? restrictions.AddDifficultyArea(codes[0], req.DifficultyType, req.Tag).Value
                            : restrictions.AddDifficultyArea(codes, req.DifficultyType, req.Tag).Value;
                        break;
                    default:
                        return Results.BadRequest(new ErrorResponse("invalid_kind", $"kind は block / difficulty のいずれか (受信: {req.Kind})"));
                }
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse("invalid_argument", ex.Message));
            }
            return Results.Ok(new RestrictionIdResponse(id));
        });

        // POST /api/restrictions/gml-upload — アップロードした GML を取込む（multipart/form-data）。
        // フィールド: file(必須), kind, difficultyType, useMapBounds, swLat, swLon, neLat, neLon, tag。
        // フォームを手動読取するため antiforgery 検証の対象外（IFormFile パラメータ束縛を避ける）。
        app.MapPost("/api/restrictions/gml-upload", async (HttpRequest request, SandboxState state) =>
        {
            var restrictions = state.Restrictions;
            if (restrictions is null)
                return Results.Conflict(new ErrorResponse("not_loaded", "No graph loaded."));

            if (!request.HasFormContentType)
                return Results.BadRequest(new ErrorResponse("invalid_form", "multipart/form-data で送信してください。"));

            var form = await request.ReadFormAsync();
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
                return Results.BadRequest(new ErrorResponse("missing_file", "GML ファイル (file) を指定してください。"));

            var kind = (form["kind"].ToString() ?? "").Trim().ToLowerInvariant();
            var difficultyType = form["difficultyType"].ToString();
            var tag = form["tag"].ToString();
            if (string.IsNullOrWhiteSpace(tag)) tag = null;

            MapBounds? bounds = null;
            if (string.Equals(form["useMapBounds"].ToString(), "true", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseBounds(form, out var b))
                    return Results.BadRequest(new ErrorResponse("missing_bounds", "useMapBounds=true の場合は swLat/swLon/neLat/neLon が必要です。"));
                bounds = b;
            }

            RestrictedAreaId[] ids;
            try
            {
                await using var stream = file.OpenReadStream();
                switch (kind)
                {
                    case "block":
                        ids = restrictions.AddBlockAreaFromGmlStream(stream, bounds, tag);
                        break;
                    case "difficulty":
                        if (string.IsNullOrWhiteSpace(difficultyType))
                            return Results.BadRequest(new ErrorResponse("missing_difficulty_type", "kind=difficulty の場合は difficultyType が必要です。"));
                        ids = restrictions.AddDifficultyAreaFromGmlStream(stream, difficultyType, bounds, tag);
                        break;
                    default:
                        return Results.BadRequest(new ErrorResponse("invalid_kind", $"kind は block / difficulty のいずれか (受信: {kind})"));
                }
            }
            catch (OsmDotRoute.Gml.InvalidGmlException ex)
            {
                return Results.BadRequest(new ErrorResponse("invalid_gml", ex.Message));
            }
            catch (NotSupportedException ex)
            {
                return Results.BadRequest(new ErrorResponse("not_supported", ex.Message));
            }

            var guids = ids.Select(i => i.Value).ToArray();
            return Results.Ok(new GmlImportResponse(guids, guids.Length));
        });

        // POST /api/restrictions/save — 制約を「保存先フォルダ設定」のフォルダへ JSON 保存
        app.MapPost("/api/restrictions/save", (SandboxState state, CacheService cache) =>
        {
            var restrictions = state.Restrictions;
            if (restrictions is null)
                return Results.Conflict(new ErrorResponse("not_loaded", "No graph loaded."));

            var fileName = $"restrictions-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var path = Path.Combine(cache.CacheDir, fileName);
            restrictions.SaveToJsonFile(path);
            return Results.Ok(new RestrictionSaveResponse(path));
        });

        // GET /api/restrictions — 一覧
        app.MapGet("/api/restrictions", (SandboxState state) =>
        {
            var restrictions = state.Restrictions;
            if (restrictions is null)
                return Results.Ok(new { items = Array.Empty<object>() });
            var items = restrictions.ListAll().Select(BuildItemDto).ToArray();
            return Results.Ok(new { items });
        });

        // GET /api/restrictions/geojson — マップ描画用
        app.MapGet("/api/restrictions/geojson", (SandboxState state) =>
        {
            var restrictions = state.Restrictions;
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("type", "FeatureCollection");
                w.WritePropertyName("features");
                w.WriteStartArray();
                if (restrictions is not null)
                    foreach (var area in restrictions.ListAll())
                        WriteAreaFeatures(w, area);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return Results.Text(System.Text.Encoding.UTF8.GetString(ms.ToArray()), "application/geo+json");
        });

        // DELETE /api/restrictions/{id}
        app.MapDelete("/api/restrictions/{id:guid}", (Guid id, SandboxState state) =>
        {
            state.Restrictions?.Remove(new RestrictedAreaId(id));
            return Results.NoContent();
        });

        // DELETE /api/restrictions (全クリア or ?tag=X)
        app.MapDelete("/api/restrictions", (string? tag, SandboxState state) =>
        {
            var restrictions = state.Restrictions;
            if (restrictions is not null)
            {
                if (!string.IsNullOrEmpty(tag)) restrictions.RemoveByTag(tag);
                else restrictions.ClearAll();
            }
            return Results.NoContent();
        });
    }

    private static bool TryParseBounds(IFormCollection form, out MapBounds bounds)
    {
        bounds = default!;
        const System.Globalization.NumberStyles ns = System.Globalization.NumberStyles.Float;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (double.TryParse(form["swLat"], ns, ci, out var swLat) &&
            double.TryParse(form["swLon"], ns, ci, out var swLon) &&
            double.TryParse(form["neLat"], ns, ci, out var neLat) &&
            double.TryParse(form["neLon"], ns, ci, out var neLon))
        {
            bounds = new MapBounds(new GeoCoordinate(swLat, swLon), new GeoCoordinate(neLat, neLon));
            return true;
        }
        return false;
    }

    private static object BuildItemDto(RestrictedArea area)
    {
        var (kind, difficultyType) = area switch
        {
            BlockArea => ("block", (string?)null),
            DifficultyArea d => ("difficulty", d.DifficultyType),
            _ => ("unknown", (string?)null),
        };
        var polygon = (area as BlockArea)?.Polygon ?? (area as DifficultyArea)?.Polygon;
        var meshCodes = (area as BlockArea)?.MeshCodes ?? (area as DifficultyArea)?.MeshCodes;

        return new
        {
            id = area.Id.Value,
            kind,
            difficultyType,
            shapeType = polygon is not null ? "polygon" : "mesh",
            outerBoundary = polygon?.OuterBoundary.Select(c => new CoordinateDto(c.Latitude, c.Longitude)).ToArray(),
            meshCodes = meshCodes?.Select(m => m.Value).ToArray(),
            tag = area.Tag,
        };
    }

    private static void WriteAreaFeatures(Utf8JsonWriter w, RestrictedArea area)
    {
        var (kind, difficultyType) = area switch
        {
            BlockArea => ("block", (string?)null),
            DifficultyArea d => ("difficulty", d.DifficultyType),
            _ => ("unknown", (string?)null),
        };
        var polygon = (area as BlockArea)?.Polygon ?? (area as DifficultyArea)?.Polygon;
        var meshCodes = (area as BlockArea)?.MeshCodes ?? (area as DifficultyArea)?.MeshCodes;

        if (polygon is not null)
        {
            var ring = polygon.OuterBoundary.Select(c => new CoordinateDto(c.Latitude, c.Longitude)).ToArray();
            WritePolygonFeature(w, area.Id.Value, kind, difficultyType, area.Tag, "polygon", ring, null);
        }
        else if (meshCodes is not null)
        {
            foreach (var m in meshCodes)
            {
                var b = m.ToBounds();
                var ring = new[]
                {
                    new CoordinateDto(b.SouthWest.Latitude, b.SouthWest.Longitude),
                    new CoordinateDto(b.SouthWest.Latitude, b.NorthEast.Longitude),
                    new CoordinateDto(b.NorthEast.Latitude, b.NorthEast.Longitude),
                    new CoordinateDto(b.NorthEast.Latitude, b.SouthWest.Longitude),
                };
                WritePolygonFeature(w, area.Id.Value, kind, difficultyType, area.Tag, "mesh", ring, m.Value);
            }
        }
    }

    private static void WritePolygonFeature(
        Utf8JsonWriter w, Guid id, string kind, string? difficultyType, string? tag,
        string shapeType, CoordinateDto[] boundary, long? meshCode)
    {
        w.WriteStartObject();
        w.WriteString("type", "Feature");
        w.WritePropertyName("properties");
        w.WriteStartObject();
        w.WriteString("id", id.ToString());
        w.WriteString("kind", kind);
        if (difficultyType is not null) w.WriteString("difficultyType", difficultyType);
        if (tag is not null) w.WriteString("tag", tag);
        w.WriteString("shapeType", shapeType);
        if (meshCode.HasValue) w.WriteString("meshCode", meshCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteEndObject();
        w.WritePropertyName("geometry");
        w.WriteStartObject();
        w.WriteString("type", "Polygon");
        w.WritePropertyName("coordinates");
        w.WriteStartArray();
        w.WriteStartArray();
        foreach (var c in boundary)
        {
            w.WriteStartArray();
            w.WriteNumberValue(c.Longitude);
            w.WriteNumberValue(c.Latitude);
            w.WriteEndArray();
        }
        w.WriteStartArray();
        w.WriteNumberValue(boundary[0].Longitude);
        w.WriteNumberValue(boundary[0].Latitude);
        w.WriteEndArray();
        w.WriteEndArray();
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();
    }
}
