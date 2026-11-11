using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using OsmDotRoute;

namespace Sandbox.Wasm;

/// <summary>
/// JS ⇔ C# 動的制約 + メッシュブリッジ（Phase 3 ステップ 3J.4）。
/// メッシュ / ポリゴンへの block / difficulty 付与、一覧 / 削除 / GeoJSON、メッシュグリッド生成を
/// クライアント完結で提供する。制約は <see cref="Router"/> に attach 済みの <see cref="RestrictedAreaService"/>
/// に反映されるため、付与後に <see cref="Interop.CalculateRoute"/> を再呼出すれば Re-Route（回避経路）になる。
/// GeoJSON 出力は Server と同じく <see cref="Utf8JsonWriter"/> で生成（trim/AOT 安全）。
/// </summary>
public partial class Interop
{
    /// <summary>ポリゴン制約を登録（<see cref="PolygonRestrictionRequestDto"/> JSON）→ <see cref="RestrictionIdDto"/> JSON。</summary>
    [JSExport]
    internal static string AddPolygonRestriction(string requestJson)
    {
        var restrictions = RequireRestrictions();
        var req = JsonSerializer.Deserialize(requestJson, SandboxJsonContext.Default.PolygonRestrictionRequestDto)
                  ?? throw new ArgumentException("Invalid polygon restriction request JSON.");

        if (req.OuterBoundary is null || req.OuterBoundary.Length < 3)
        {
            throw new ArgumentException("outerBoundary は 3 頂点以上必要です。");
        }

        var polygon = new GeoPolygon([.. req.OuterBoundary.Select(c => new GeoCoordinate(c.Latitude, c.Longitude))]);
        var kind = (req.Kind ?? "").Trim().ToLowerInvariant();

        var id = kind switch
        {
            "block" => restrictions.AddBlockArea(polygon, req.Tag).Value,
            "difficulty" => restrictions.AddDifficultyArea(
                polygon,
                RequireDifficultyType(req.DifficultyType),
                req.Tag).Value,
            _ => throw new ArgumentException($"kind は block / difficulty のいずれか (受信: {req.Kind})"),
        };

        return JsonSerializer.Serialize(new RestrictionIdDto(id.ToString()), SandboxJsonContext.Default.RestrictionIdDto);
    }

    /// <summary>メッシュ制約を登録（<see cref="MeshRestrictionRequestDto"/> JSON）→ <see cref="RestrictionIdDto"/> JSON。</summary>
    [JSExport]
    internal static string AddMeshRestriction(string requestJson)
    {
        var restrictions = RequireRestrictions();
        var req = JsonSerializer.Deserialize(requestJson, SandboxJsonContext.Default.MeshRestrictionRequestDto)
                  ?? throw new ArgumentException("Invalid mesh restriction request JSON.");

        if (req.MeshCodes is null || req.MeshCodes.Length == 0)
        {
            throw new ArgumentException("meshCodes は 1 個以上必要です。");
        }

        var codes = req.MeshCodes.Select(v => new MeshCode(v)).ToArray();
        var kind = (req.Kind ?? "").Trim().ToLowerInvariant();

        var id = kind switch
        {
            "block" => codes.Length == 1
                ? restrictions.AddBlockArea(codes[0], req.Tag).Value
                : restrictions.AddBlockArea(codes, req.Tag).Value,
            "difficulty" => codes.Length == 1
                ? restrictions.AddDifficultyArea(codes[0], RequireDifficultyType(req.DifficultyType), req.Tag).Value
                : restrictions.AddDifficultyArea(codes, RequireDifficultyType(req.DifficultyType), req.Tag).Value,
            _ => throw new ArgumentException($"kind は block / difficulty のいずれか (受信: {req.Kind})"),
        };

        return JsonSerializer.Serialize(new RestrictionIdDto(id.ToString()), SandboxJsonContext.Default.RestrictionIdDto);
    }

    /// <summary>
    /// アップロードされた GML バイト列から制約を一括登録する（<see cref="GmlImportOptionsDto"/> JSON）
    /// → <see cref="GmlImportResultDto"/> JSON。GML パースはブラウザ内（XmlReader）で完結する。
    /// </summary>
    [JSExport]
    internal static string AddGmlRestriction(byte[] gmlBytes, string optionsJson)
    {
        var restrictions = RequireRestrictions();
        var opt = JsonSerializer.Deserialize(optionsJson, SandboxJsonContext.Default.GmlImportOptionsDto)
                  ?? throw new ArgumentException("Invalid GML import options JSON.");

        var tag = string.IsNullOrWhiteSpace(opt.Tag) ? null : opt.Tag;
        MapBounds? bounds = null;
        if (opt.UseMapBounds)
        {
            if (opt.MapBoundsSouthWest is null || opt.MapBoundsNorthEast is null)
            {
                throw new ArgumentException("useMapBounds=true の場合は mapBoundsSouthWest / mapBoundsNorthEast が必要です。");
            }
            bounds = new MapBounds(
                new GeoCoordinate(opt.MapBoundsSouthWest.Latitude, opt.MapBoundsSouthWest.Longitude),
                new GeoCoordinate(opt.MapBoundsNorthEast.Latitude, opt.MapBoundsNorthEast.Longitude));
        }

        var kind = (opt.Kind ?? "").Trim().ToLowerInvariant();
        using var stream = new MemoryStream(gmlBytes);
        var ids = kind switch
        {
            "block" => restrictions.AddBlockAreaFromGmlStream(stream, bounds, tag),
            "difficulty" => restrictions.AddDifficultyAreaFromGmlStream(
                stream, RequireDifficultyType(opt.DifficultyType), bounds, tag),
            _ => throw new ArgumentException($"kind は block / difficulty のいずれか (受信: {opt.Kind})"),
        };

        var result = new GmlImportResultDto(ids.Select(i => i.Value.ToString()).ToArray(), ids.Length);
        return JsonSerializer.Serialize(result, SandboxJsonContext.Default.GmlImportResultDto);
    }

    /// <summary>登録済み制約の一覧（<see cref="RestrictionListDto"/> JSON）。</summary>
    [JSExport]
    internal static string ListRestrictions()
    {
        var items = _restrictions is null
            ? []
            : _restrictions.ListAll().Select(BuildItemDto).ToArray();
        return JsonSerializer.Serialize(new RestrictionListDto(items), SandboxJsonContext.Default.RestrictionListDto);
    }

    /// <summary>登録済み制約の GeoJSON FeatureCollection（マップ描画用）。</summary>
    [JSExport]
    internal static string RestrictionsGeoJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", "FeatureCollection");
            w.WritePropertyName("features");
            w.WriteStartArray();
            if (_restrictions is not null)
            {
                foreach (var area in _restrictions.ListAll())
                {
                    WriteAreaFeatures(w, area);
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>指定 ID の制約を削除する。</summary>
    [JSExport]
    internal static void DeleteRestriction(string id)
    {
        _restrictions?.Remove(new RestrictedAreaId(Guid.Parse(id)));
    }

    /// <summary>全制約をクリアする。</summary>
    [JSExport]
    internal static void ClearRestrictions()
    {
        _restrictions?.ClearAll();
    }

    /// <summary>指定範囲・階層の地域メッシュグリッドを GeoJSON FeatureCollection として返す。</summary>
    [JSExport]
    internal static string MeshGrid(double swLat, double swLon, double neLat, double neLon, string level)
    {
        var meshLevel = ParseLevel(level)
            ?? throw new ArgumentException($"level は 1km / 500m / 250m / 125m のいずれか (受信: {level})");

        var bounds = new MapBounds(
            new GeoCoordinate(Math.Min(swLat, neLat), Math.Min(swLon, neLon)),
            new GeoCoordinate(Math.Max(swLat, neLat), Math.Max(swLon, neLon)));

        const int hardCap = 10_000;
        var cells = new List<(MeshCode Code, MapBounds Bounds)>();
        foreach (var code in MeshCode.EnumerateInBounds(bounds, meshLevel))
        {
            cells.Add((code, code.ToBounds()));
            if (cells.Count > hardCap)
            {
                throw new InvalidOperationException(
                    $"範囲内のメッシュ数が上限 {hardCap} を超えました。階層を粗くするか範囲を狭めてください。");
            }
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", "FeatureCollection");
            w.WritePropertyName("features");
            w.WriteStartArray();
            foreach (var (code, b) in cells)
            {
                WriteMeshFeature(w, code, b);
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static RestrictedAreaService RequireRestrictions()
        => _restrictions ?? throw new InvalidOperationException("No graph loaded. Call LoadOdrg first.");

    private static string RequireDifficultyType(string? difficultyType)
        => string.IsNullOrWhiteSpace(difficultyType)
            ? throw new ArgumentException("kind=difficulty の場合は difficultyType が必要です。")
            : difficultyType;

    private static MeshLevel? ParseLevel(string s) => s.Trim().ToLowerInvariant() switch
    {
        "1km" or "mesh3rd" or "8" => MeshLevel.Mesh3rd,
        "500m" or "halfmesh" or "9" => MeshLevel.HalfMesh,
        "250m" or "quartermesh" or "10" => MeshLevel.QuarterMesh,
        "125m" or "eighthmesh" or "11" => MeshLevel.EighthMesh,
        _ => null,
    };

    private static RestrictionItemDto BuildItemDto(RestrictedArea area)
    {
        var (kind, difficultyType) = area switch
        {
            BlockArea => ("block", (string?)null),
            DifficultyArea d => ("difficulty", d.DifficultyType),
            _ => ("unknown", (string?)null),
        };
        var polygon = (area as BlockArea)?.Polygon ?? (area as DifficultyArea)?.Polygon;
        var meshCodes = (area as BlockArea)?.MeshCodes ?? (area as DifficultyArea)?.MeshCodes;

        return new RestrictionItemDto(
            area.Id.Value.ToString(),
            kind,
            difficultyType,
            polygon is not null ? "polygon" : "mesh",
            polygon?.OuterBoundary.Select(c => new CoordinateDto(c.Latitude, c.Longitude)).ToArray(),
            meshCodes?.Select(m => m.Value).ToArray(),
            area.Tag);
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
                CoordinateDto[] ring =
                [
                    new(b.SouthWest.Latitude, b.SouthWest.Longitude),
                    new(b.SouthWest.Latitude, b.NorthEast.Longitude),
                    new(b.NorthEast.Latitude, b.NorthEast.Longitude),
                    new(b.NorthEast.Latitude, b.SouthWest.Longitude),
                ];
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
        if (meshCode.HasValue) w.WriteString("meshCode", meshCode.Value.ToString(CultureInfo.InvariantCulture));
        w.WriteEndObject();
        w.WritePropertyName("geometry");
        w.WriteStartObject();
        w.WriteString("type", "Polygon");
        w.WritePropertyName("coordinates");
        w.WriteStartArray();
        w.WriteStartArray();
        foreach (var c in boundary)
        {
            WriteCoord(w, c.Longitude, c.Latitude);
        }
        WriteCoord(w, boundary[0].Longitude, boundary[0].Latitude);
        w.WriteEndArray();
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteMeshFeature(Utf8JsonWriter w, MeshCode code, MapBounds b)
    {
        w.WriteStartObject();
        w.WriteString("type", "Feature");
        w.WritePropertyName("properties");
        w.WriteStartObject();
        w.WriteString("meshCode", code.Value.ToString(CultureInfo.InvariantCulture));
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
