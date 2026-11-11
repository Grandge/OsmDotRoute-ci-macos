using System.Text.Json;

namespace Sandbox.Server.Services;

public sealed class CacheService
{
    private readonly object _lock = new();
    private string _cacheDir;
    private readonly string _settingsPath;

    public CacheService()
    {
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OsmDotRoute.Sandbox", "settings.json");

        _cacheDir = LoadPersistedCacheDir()
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OsmDotRoute.Sandbox", "cache");

        Directory.CreateDirectory(_cacheDir);
    }

    public string CacheDir
    {
        get { lock (_lock) { return _cacheDir; } }
    }

    public void SetCacheDir(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(full);
        lock (_lock) { _cacheDir = full; }
        PersistCacheDir(full);
    }

    public string GetPbfPath(string regionKey)
    {
        lock (_lock) { return Path.Combine(_cacheDir, $"{regionKey}-latest.osm.pbf"); }
    }

    public bool IsPbfCached(string regionKey) =>
        File.Exists(GetPbfPath(regionKey));

    public long GetPbfSize(string regionKey)
    {
        var path = GetPbfPath(regionKey);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    public CachedFileInfo[] ListCachedPbfs()
    {
        return GeofabrikService.Regions
            .Where(r => File.Exists(GetPbfPath(r.Key)))
            .Select(r =>
            {
                var fi = new FileInfo(GetPbfPath(r.Key));
                return new CachedFileInfo(r.Key, r.DisplayName, fi.Length, fi.LastWriteTimeUtc);
            })
            .ToArray();
    }

    public bool DeletePbf(string regionKey)
    {
        var path = GetPbfPath(regionKey);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private string? LoadPersistedCacheDir()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return null;
            var json = File.ReadAllText(_settingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("cacheDir", out var prop))
            {
                var val = prop.GetString();
                if (!string.IsNullOrWhiteSpace(val) && Directory.Exists(val))
                    return val;
            }
        }
        catch { /* ignore corrupt settings */ }
        return null;
    }

    private void PersistCacheDir(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = JsonSerializer.Serialize(new { cacheDir = path });
            File.WriteAllText(_settingsPath, json);
        }
        catch { /* best effort */ }
    }
}

public sealed record CachedFileInfo(string RegionKey, string DisplayName, long SizeBytes, DateTime LastModifiedUtc);
