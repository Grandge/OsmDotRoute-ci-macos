using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using OsmDotRoute;
using OsmRoute = OsmDotRoute.Route;

namespace Sandbox.Wasm;

/// <summary>
/// JS ⇔ C# ルーティングブリッジ（Phase 3 ステップ 3J.3）。
/// fetch 済み <c>.odrg</c> バイト列をブラウザ内でロードし、経路計算 / スナップ / 道路 NW 出力を
/// JSON 文字列（React 側 client.ts の DTO 形状）で返す。Sandbox Server の REST 相当をクライアント完結化する。
/// </summary>
public partial class Interop
{
    private static RouterDb? _routerDb;
    private static Router? _router;
    private static RestrictedAreaService? _restrictions;
    private static string[] _profileNames = [];

    /// <summary>動作確認用。コアアセンブリ（OsmDotRoute）が WASM 上でロードできることを実証する。</summary>
    [JSExport]
    internal static string Version()
    {
        var core = typeof(Router).Assembly.GetName();
        return $"Sandbox.Wasm OK — core {core.Name} v{core.Version}";
    }

    /// <summary>fetch 済み <c>.odrg</c> バイト列をロードし、統計 JSON（<see cref="StatsDto"/>）を返す。</summary>
    /// <remarks>
    /// <paramref name="data"/> は <c>byte[]</c> 既定マーシャリング（JS <c>Uint8Array</c> → マネージド配列のバルクコピー）で
    /// 受け取る。受領した配列はそのままグラフ寿命中ピン留めされる（<see cref="RouterDb.LoadFromOdrg(ReadOnlyMemory{byte})"/>）。
    /// </remarks>
    [JSExport]
    internal static string LoadOdrg(byte[] data)
    {
        var db = RouterDb.LoadFromOdrg(data);
        var restrictions = new RestrictedAreaService();

        _routerDb = db;
        _restrictions = restrictions;
        _router = new Router(db, restrictions);
        _profileNames = [.. db.GetProfileNames()];

        return StatsJson();
    }

    /// <summary>ロード済みグラフの統計 JSON（<see cref="StatsDto"/>）を返す。</summary>
    [JSExport]
    internal static string GetStats()
    {
        EnsureLoaded();
        return StatsJson();
    }

    /// <summary>
    /// 道路ネットワークの GeoJSON FeatureCollection 文字列を返す。
    /// RequestedBbox（ユーザー指定抽出範囲）が利用可能な場合はそれでフィルタリングし、
    /// way 拡張で FileBbox が広がった余剰エッジを除外する。
    /// </summary>
    [JSExport]
    internal static string GetRoadNetwork()
    {
        EnsureLoaded();
        var bounds = _routerDb!.GetRequestedBounds();
        return _router!.GetRoadNetworkGeoJson(bounds).Json;
    }

    /// <summary>経路計算リクエスト（<see cref="RouteRequestDto"/> JSON）→ <see cref="RouteDto"/> JSON。</summary>
    [JSExport]
    internal static string CalculateRoute(string requestJson)
    {
        EnsureLoaded();
        var req = JsonSerializer.Deserialize(requestJson, SandboxJsonContext.Default.RouteRequestDto)
                  ?? throw new ArgumentException("Invalid route request JSON.");

        var profile = ResolveProfile(req.Profile);
        var route = _router!.Calculate(
            profile,
            new GeoCoordinate(req.FromLat, req.FromLon),
            new GeoCoordinate(req.ToLat, req.ToLon));

        var dto = route is null
            ? new RouteDto(false, 0, 0, null)
            : new RouteDto(true, route.TotalDistanceM, route.TotalDurationSec, ToLineString(route));

        return JsonSerializer.Serialize(dto, SandboxJsonContext.Default.RouteDto);
    }

    /// <summary>スナップリクエスト（<see cref="SnapRequestDto"/> JSON）→ <see cref="SnapDto"/> JSON。</summary>
    [JSExport]
    internal static string Snap(string requestJson)
    {
        EnsureLoaded();
        var req = JsonSerializer.Deserialize(requestJson, SandboxJsonContext.Default.SnapRequestDto)
                  ?? throw new ArgumentException("Invalid snap request JSON.");

        var profile = ResolveProfile(req.Profile);
        var snapped = _router!.SnapToRoad(
            profile, new GeoCoordinate(req.Lat, req.Lon), req.SearchDistanceM ?? 500f);

        var dto = new SnapDto(snapped.HasValue
            ? new CoordinateDto(snapped.Value.Latitude, snapped.Value.Longitude)
            : null);

        return JsonSerializer.Serialize(dto, SandboxJsonContext.Default.SnapDto);
    }

    private static string StatsJson()
    {
        var s = _routerDb!.GetStatistics();
        // RequestedBbox（ユーザー指定抽出範囲）を優先。way 拡張で FileBbox が広がる場合の補正。
        var req = _routerDb!.GetRequestedBounds();
        var sw = req.HasValue ? req.Value.SouthWest : s.SouthWest;
        var ne = req.HasValue ? req.Value.NorthEast : s.NorthEast;
        var dto = new StatsDto(
            s.VertexCount,
            s.EdgeCount,
            new CoordinateDto(sw.Latitude, sw.Longitude),
            new CoordinateDto(ne.Latitude, ne.Longitude),
            _profileNames);
        return JsonSerializer.Serialize(dto, SandboxJsonContext.Default.StatsDto);
    }

    private static LineStringDto ToLineString(OsmRoute route)
    {
        var shape = route.Shape.Span;
        var coords = new double[shape.Length][];
        for (int i = 0; i < shape.Length; i++)
        {
            coords[i] = [shape[i].Longitude, shape[i].Latitude];
        }
        return new LineStringDto("LineString", coords);
    }

    private static VehicleProfile ResolveProfile(string? name) => name?.ToLowerInvariant() switch
    {
        "pedestrian" => VehicleProfile.Pedestrian,
        "bicycle" => VehicleProfile.Bicycle,
        "truck" => VehicleProfile.Truck,
        _ => VehicleProfile.Car,
    };

    private static void EnsureLoaded()
    {
        if (_router is null)
        {
            throw new InvalidOperationException("No graph loaded. Call LoadOdrg first.");
        }
    }
}
