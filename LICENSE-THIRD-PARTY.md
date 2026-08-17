# Third-Party Notices

OsmDotRoute is licensed under the MIT License (see [LICENSE](LICENSE)).
This file lists the third-party components OsmDotRoute uses and their licenses.

## Runtime — core library

The core library (`OsmDotRoute` and `OsmDotRoute.Pbf`) has **no third-party NuGet dependencies**.
It uses only the .NET base class libraries (`System.*`), which ship with .NET under the MIT License
(© .NET Foundation and Contributors).

## Optional library packages

These are pulled in only when you use the corresponding component; the routing runtime works with the
core library alone.

| Package | Used by | Version | License | Project |
| --- | --- | --- | --- | --- |
| Microsoft.Extensions.DependencyInjection.Abstractions | `OsmDotRoute.Extensions.DependencyInjection` | 9.0.0 | MIT | .NET Foundation |
| System.CommandLine | `OsmDotRoute.Extractor` (CLI) | 3.0.0-preview.4.26230.115 | MIT | .NET Foundation |

## Development-only dependencies (not distributed)

Used for tests and benchmarks; not part of any shipped artifact.

| Package | Version | License |
| --- | --- | --- |
| xunit | 2.9.2 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT |
| coverlet.collector | 6.0.2 | MIT |
| Microsoft.Extensions.DependencyInjection | 9.0.0 | MIT |
| BenchmarkDotNet | 0.15.8 | MIT |

## Sample applications (not part of the library)

The projects under `samples/` (Sandbox, MapVerifier, etc.) are demonstrations and are not part of the
distributed library. Their web front-ends use open-source JavaScript libraries (e.g. React, Leaflet /
MapLibre GL) under their respective MIT / BSD licenses; see each project's `package.json` for the full
dependency list.

## Map data — OpenStreetMap (ODbL)

OsmDotRoute processes OpenStreetMap (OSM) data. OSM data is © OpenStreetMap contributors and is
licensed under the [Open Database License (ODbL)](https://opendatacommons.org/licenses/odbl/).
A `.odrg` file produced from OSM data is a Produced Work / Derivative Database of OSM data.

When you distribute or publish routes, geometry, or `.odrg` files derived from OSM, you must:

- attribute "© OpenStreetMap contributors", and
- comply with the ODbL.

See https://www.openstreetmap.org/copyright for details.

## Map data — KSJ / National Land Numerical Information (Japan)

The dynamic-restriction features can ingest KSJ (National Land Numerical Information) GML data
(e.g. A31 flood inundation zones) provided by the Japanese Ministry of Land, Infrastructure, Transport
and Tourism (MLIT). Such data is provided under MLIT's own terms of use; comply with those terms when
you use it. KSJ data is **not** bundled with this repository.
