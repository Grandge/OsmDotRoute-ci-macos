using Sandbox.Server.Endpoints;
using Sandbox.Server.Services;
using Microsoft.AspNetCore.ResponseCompression;

const string CorsPolicyName = "SandboxWeb";

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(opts =>
{
    opts.Limits.MaxRequestBodySize = 2_147_483_648L;
});

builder.Services.AddCors(o => o.AddPolicy(CorsPolicyName, p => p
    .WithOrigins("http://localhost:5174")
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/geo+json" });
});

builder.Services.AddSingleton<SandboxState>();
builder.Services.AddSingleton<CacheService>();
builder.Services.AddHttpClient<GeofabrikService>(client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
});

var app = builder.Build();

app.UseResponseCompression();
app.UseCors(CorsPolicyName);

app.MapVersionEndpoints();
app.MapDownloadEndpoints();
app.MapExtractEndpoints();
app.MapGraphEndpoints();
app.MapRouteEndpoints();
app.MapLoadEndpoints();
app.MapBrowseEndpoints();
app.MapMeshEndpoints();
app.MapRestrictionEndpoints();

app.Run();
