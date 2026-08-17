using System.Text.Json;
using Sandbox.Server.Contracts;
using Sandbox.Server.Services;

namespace Sandbox.Server.Endpoints;

public static class DownloadEndpoints
{
    private static int _downloading;

    public static void MapDownloadEndpoints(this WebApplication app)
    {
        app.MapGet("/api/regions", () =>
            GeofabrikService.Regions.Select(r => new RegionResponse(r.Key, r.DisplayName, r.Description)));

        app.MapGet("/api/cache/status", (CacheService cache) =>
            new CacheStatusResponse(cache.ListCachedPbfs()
                .Select(c => new CachedPbfInfo(c.RegionKey, c.DisplayName, c.SizeBytes, c.LastModifiedUtc))
                .ToArray()));

        app.MapGet("/api/cache/dir", (CacheService cache) =>
            new CacheDirResponse(cache.CacheDir));

        app.MapPost("/api/cache/dir", (CacheDirRequest req, CacheService cache) =>
        {
            try
            {
                cache.SetCacheDir(req.Path);
                return Results.Ok(new CacheDirResponse(cache.CacheDir));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse("bad_request", ex.Message));
            }
        });

        app.MapPost("/api/download", async (HttpContext ctx, DownloadRequest req,
            GeofabrikService geofabrik, CacheService cache) =>
        {
            var region = GeofabrikService.FindRegion(req.Region);
            if (region is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("bad_request", $"Unknown region: {req.Region}"));
                return;
            }

            if (Interlocked.CompareExchange(ref _downloading, 1, 0) != 0)
            {
                ctx.Response.StatusCode = 409;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("conflict", "Another download is in progress"));
                return;
            }

            try
            {
                var pbfPath = cache.GetPbfPath(region.Key);

                if (cache.IsPbfCached(region.Key))
                {
                    ctx.Response.ContentType = "text/event-stream";
                    ctx.Response.Headers["Cache-Control"] = "no-cache";
                    var size = cache.GetPbfSize(region.Key);
                    await WriteSseAsync(ctx, new { type = "cached", path = pbfPath, sizeBytes = size });
                    return;
                }

                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers["Cache-Control"] = "no-cache";

                var url = geofabrik.GetDownloadUrl(region);
                using var response = await geofabrik.SendDownloadRequestAsync(url, ctx.RequestAborted);
                var total = response.Content.Headers.ContentLength ?? -1;

                var tempPath = pbfPath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(pbfPath)!);
                await using var stream = await response.Content.ReadAsStreamAsync(ctx.RequestAborted);
                await using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long downloaded = 0;
                    long lastReported = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, ctx.RequestAborted)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), ctx.RequestAborted);
                        downloaded += read;
                        if (downloaded - lastReported >= 524_288)
                        {
                            lastReported = downloaded;
                            await WriteSseAsync(ctx, new { type = "progress", downloaded, total });
                        }
                    }
                }

                File.Move(tempPath, pbfPath, overwrite: true);
                var finalSize = new FileInfo(pbfPath).Length;
                await WriteSseAsync(ctx, new { type = "complete", path = pbfPath, sizeBytes = finalSize });
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
                catch
                {
                    // Response may already be committed
                }
            }
            finally
            {
                Interlocked.Exchange(ref _downloading, 0);
            }
        });

        app.MapDelete("/api/cache/{region}", (string region, CacheService cache) =>
            cache.DeletePbf(region)
                ? Results.Ok()
                : Results.NotFound());
    }

    private static async Task WriteSseAsync(HttpContext ctx, object data)
    {
        var json = JsonSerializer.Serialize(data);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }
}
