# OsmDotRoute

English | [日本語](README.md)

A .NET-native OSM routing library. It provides Dijkstra-based path finding with
**dynamic travel restrictions** (no-entry / difficult-to-traverse areas).

It targets **scenarios that require a large volume of route calculations in a short time, such as
multi-agent simulations**. A pre-extracted custom binary `.odrg` is read with zero copies via
`MemoryMappedFile` + `ReadOnlySpan<T>`, so many agents can query the same graph repeatedly with
minimal allocation — high-frequency, high-volume routing is handled at low overhead.

On top of that, its defining strength is that it lets you **add/remove travel restrictions during a
running simulation and re-route instantly** (no rebuild required).

> For the differences between OsmDotRoute and Itinero and which one fits your use case, see the
> [Comparison & Selection Guide](Documents/comparison_with_itinero.en.md).

## Features

- **.NET 9 / pure C#, runtime dependencies are System.* only** (zero external NuGet packages)
- **Custom `.odrg` graph format**: zero-copy reads via `MemoryMappedFile` + `ReadOnlySpan<T>`
- **Dynamic restrictions**: polygon, JIS X0410 mesh code, and KSJ GML (e.g. A31) input; reflected in the next calculation without a rebuild
- **JSON-externalized profiles**: tune access/speed/difficulty reactions without rebuilding
- **4 built-in profiles**: car / pedestrian / bicycle / truck (10t, based on Japanese road law)
- **8 built-in difficulty types**: flooding, liquefaction, landslide, construction, obstacle, congestion, snow, ice
- **MIT License**

## Quick start

```csharp
using OsmDotRoute;

// 1. Load a pre-extracted .odrg (no PBF or Itinero needed)
var routerDb = RouterDb.LoadFromOdrg(@"D:\odrg\tokyo.odrg");
var router = new Router(routerDb);

// 2. Tokyo Station -> Shibuya Station with the Car profile (GeoCoordinate is latitude, longitude order)
var route = router.Calculate(
    VehicleProfile.Car,
    new GeoCoordinate(35.68040208522669, 139.769056008911),
    new GeoCoordinate(35.659, 139.700));

Console.WriteLine(route is null
    ? "No route could be calculated."
    : $"distance {route.TotalDistanceM:F0} m, duration {route.TotalDurationSec:F0} s");
```

For everything from building a `.odrg` (preparing a PBF and extracting) to profiles and dynamic
restrictions, see the **[Usage Guide](Documents/usage_guide.en.md)**.

## Dynamic restriction example

```csharp
var restrictions = new RestrictedAreaService();
var router = new Router(routerDb, restrictions);

// Register a no-entry area by polygon
var polygon = new GeoPolygon(new[]
{
    new GeoCoordinate(35.68, 139.76),
    new GeoCoordinate(35.68, 139.78),
    new GeoCoordinate(35.66, 139.78),
    new GeoCoordinate(35.66, 139.76),
});
restrictions.AddBlockArea(polygon, tag: "incident-2026-05-19");

// Register a difficulty area (flooding) by mesh code
restrictions.AddDifficultyArea(
    new MeshCode(53394611),
    DifficultyTypes.Flooding,
    tag: "typhoon-15");

// Bulk-register KSJ A31 (flood inundation zones) from GML (filtered by map bounds)
var bounds = new MapBounds(
    new GeoCoordinate(35.65, 139.74),
    new GeoCoordinate(35.70, 139.79));
restrictions.AddDifficultyAreaFromGmlFile(
    @"D:\hazard\A31-12_24_GML\A31-12_24.xml",
    DifficultyTypes.Flooding,
    mapBounds: bounds,
    tag: "ksj-a31");

// Bulk remove by tag -> reflected in the next calculation
restrictions.RemoveByTag("typhoon-15");
```

## DI integration

```csharp
using Microsoft.Extensions.DependencyInjection;
using OsmDotRoute.Extensions.DependencyInjection;

services.AddOsmDotRoute(@"D:\odrg\tokyo.odrg");

// Consumer side
var router = serviceProvider.GetRequiredService<Router>();
var restrictions = serviceProvider.GetRequiredService<RestrictedAreaService>();
```

`Router` / `RouterDb` / `RestrictedAreaService` are all registered as singletons.
By sharing the `RestrictedAreaService`, any dynamic restriction change made during a simulation
is reflected from the next `Router.Calculate` call (REQ-RST-012).

## Try-it demo (Sandbox)

**[→ Try it now in your browser (GitHub Pages)](https://grandge.github.io/OsmDotRoute/)** — no install required

The core engine is compiled to WebAssembly and served as a static site on GitHub Pages.
You can experience the full flow — PBF download → bbox extraction → routing →
mesh/polygon restriction → Re-Route — entirely in the browser.

To build locally:

```powershell
cd samples/Sandbox/Web ; npm run build:wasm
```

## Installation

There are currently no plans to publish to NuGet (nuget.org). Use it via **source reference**
(runtime depends on System.* only). Add a project reference:

```xml
<ProjectReference Include="path/to/OsmDotRoute/src/OsmDotRoute/OsmDotRoute.csproj" />
```

To use DI integration, also add:

```xml
<ProjectReference Include="path/to/OsmDotRoute/src/OsmDotRoute.Extensions.DependencyInjection/OsmDotRoute.Extensions.DependencyInjection.csproj" />
```

For the `osmdotroute-extractor` tool that generates `.odrg`, see
[Usage Guide §4](Documents/usage_guide.en.md#4-creating-an-odrg).

## Current phase

| Phase | Goal | Status |
| --- | --- | --- |
| Phase 0 | Requirements definition | Done (2026-05-18) |
| Phase 1 | Custom routing engine (Itinero kept as data layer) | Done |
| Phase 2 | Custom intermediate graph format `.odrg` | Done |
| Phase 3 | `.odrg` runtime, full Itinero removal, bicycle/truck, benchmarks, parent integration, demo, OSS release | Done (2026-06-02) |
| Phase 4 | Profile additions (Emergency / Disaster, etc.), multi-platform support | Done (2026-06-03) |

All originally planned feature work is complete through Phase 4. **For the foreseeable future only
bug fixes will be made; no new features are planned.**
The Itinero dependency has been removed from the runtime (System.* only), and all tests pass on
macOS ARM64 / Linux x64 as well.

## Versioning policy

From 1.0.0 onward, **strict semantic versioning applies** (REQ-API-008).
Breaking API changes are made only on major version bumps.

## Contributing

Bug reports and pull requests are welcome. For build/test/PR instructions, see
[CONTRIBUTING.md](CONTRIBUTING.md) (Japanese).

## License

[MIT License](LICENSE) — Copyright (c) 2026 Grandge.
For third-party components and their licenses, see [LICENSE-THIRD-PARTY.md](LICENSE-THIRD-PARTY.md).

The OSM data itself is under ODbL. When distributing or publishing a `.odrg`, the attribution
"© OpenStreetMap contributors" is required.

## Documentation

- [Usage Guide](Documents/usage_guide.en.md) — PBF prep -> `.odrg` extraction -> routing -> profiles -> code examples
- [Comparison & Selection Guide vs. Itinero](Documents/comparison_with_itinero.en.md) — design philosophy, data structures, performance, and fit by use case
- [`.odrg` binary format specification](Documents/phase2_graph_format_spec.en.md)

> Detailed design and requirement documents (Phase 1–3 design, requirements definition) are
> currently available in Japanese only.
