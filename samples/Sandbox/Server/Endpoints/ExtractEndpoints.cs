using System.Diagnostics;
using System.Text.Json;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using Sandbox.Server.Contracts;
using Sandbox.Server.Services;

namespace Sandbox.Server.Endpoints;

public static class ExtractEndpoints
{
    private static int _extracting;

    public static void MapExtractEndpoints(this WebApplication app)
    {
        app.MapPost("/api/extract", async (HttpContext ctx, ExtractRequest req,
            CacheService cache, SandboxState state) =>
        {
            if (req.Bbox is not { Length: 4 })
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("bad_request", "bbox must be [west, south, east, north]"));
                return;
            }

            var pbfPath = req.PbfPath;
            if (string.IsNullOrEmpty(pbfPath) || !File.Exists(pbfPath))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("bad_request", $"PBF file not found: {pbfPath}"));
                return;
            }

            var profileNames = req.Profiles ?? ["car", "pedestrian"];
            var profiles = new List<VehicleProfile>();
            foreach (var name in profileNames)
            {
                var p = ResolveProfile(name);
                if (p is null)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsJsonAsync(new ErrorResponse("bad_request", $"Unknown profile: {name}"));
                    return;
                }
                profiles.Add(p);
            }

            if (Interlocked.CompareExchange(ref _extracting, 1, 0) != 0)
            {
                ctx.Response.StatusCode = 409;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("conflict", "Another extraction is in progress"));
                return;
            }

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";

            try
            {
                var bbox = new Aabb(req.Bbox[0], req.Bbox[1], req.Bbox[2], req.Bbox[3]);
                var bboxKey = $"{req.Bbox[0]:F2}_{req.Bbox[1]:F2}_{req.Bbox[2]:F2}_{req.Bbox[3]:F2}";
                var profileKey = string.Join("_", profileNames.OrderBy(n => n));
                var odrgFileName = $"sandbox_{bboxKey}_{profileKey}.odrg";
                var odrgPath = Path.Combine(cache.CacheDir, odrgFileName);

                await WriteSseAsync(ctx, new { type = "phase", phase = "extracting", message = "Extracting road network from PBF..." });

                var pipelineOpts = new ExtractPipelineOptions(pbfPath, bbox, profiles);
                var sw = Stopwatch.StartNew();
                var result = await Task.Run(() => ExtractPipeline.Run(pipelineOpts), ctx.RequestAborted);
                sw.Stop();

                await WriteSseAsync(ctx, new
                {
                    type = "phase",
                    phase = "writing",
                    message = $"Writing .odrg ({result.Vertices.Length:N0} vertices, {result.Edges.Length:N0} edges, {sw.Elapsed.TotalSeconds:F1}s)..."
                });

                var metadataJson = JsonSerializer.Serialize(new
                {
                    source = pbfPath,
                    bbox = new { west = req.Bbox[0], south = req.Bbox[1], east = req.Bbox[2], north = req.Bbox[3] },
                    profiles = profileNames,
                    extractedAt = DateTime.UtcNow.ToString("o"),
                });

                var writeInput = new OdrgWriteInput(
                    Vertices: result.Vertices,
                    Edges: result.Edges,
                    EdgeAabbs: result.EdgeAabbs,
                    EdgeFlags: result.EdgeFlags,
                    RTree: result.RTree,
                    ProfileTable: result.ProfileTable,
                    NodeCoordLookup: result.NodeCoordLookup,
                    Bbox: result.FileBbox,
                    MetadataJson: metadataJson,
                    RequestedBbox: result.RequestedBbox);

                await Task.Run(() =>
                {
                    using var fs = new FileStream(odrgPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    OdrgWriter.Write(fs, writeInput);
                }, ctx.RequestAborted);

                await WriteSseAsync(ctx, new { type = "phase", phase = "loading", message = "Loading .odrg into router..." });

                var routerDb = await Task.Run(() => RouterDb.LoadFromOdrg(odrgPath), ctx.RequestAborted);
                var restrictions = new RestrictedAreaService();
                var router = new Router(routerDb, restrictions);
                var bakedProfileNames = result.ProfileTable.ProfileNames.ToArray();
                state.Set(routerDb, router, restrictions, odrgPath, bakedProfileNames);

                var stats = routerDb.GetStatistics();
                var fileSize = new FileInfo(odrgPath).Length;

                await WriteSseAsync(ctx, new
                {
                    type = "complete",
                    odrgPath,
                    vertexCount = stats.VertexCount,
                    edgeCount = stats.EdgeCount,
                    fileSizeBytes = fileSize,
                    extractSeconds = sw.Elapsed.TotalSeconds,
                    profileNames = bakedProfileNames,
                });
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
            }
            catch (Exception ex)
            {
                try
                {
                    await WriteSseAsync(ctx, new { type = "error", message = ex.Message });
                }
                catch { /* Response may already be committed */ }
            }
            finally
            {
                Interlocked.Exchange(ref _extracting, 0);
            }
        });
    }

    private static VehicleProfile? ResolveProfile(string name) =>
        name.ToLowerInvariant() switch
        {
            "car" => VehicleProfile.Car,
            "pedestrian" => VehicleProfile.Pedestrian,
            "bicycle" => VehicleProfile.Bicycle,
            "truck" => VehicleProfile.Truck,
            _ => null,
        };

    private static async Task WriteSseAsync(HttpContext ctx, object data)
    {
        var json = JsonSerializer.Serialize(data);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }
}
