using OsmDotRoute;

namespace Sandbox.Server.Services;

public sealed class SandboxState
{
    private readonly object _lock = new();
    private RouterDb? _routerDb;
    private Router? _router;
    private RestrictedAreaService? _restrictions;
    private string? _loadedOdrgPath;
    private string[] _profileNames = [];

    public bool IsLoaded
    {
        get { lock (_lock) { return _routerDb is not null; } }
    }

    public string? LoadedOdrgPath
    {
        get { lock (_lock) { return _loadedOdrgPath; } }
    }

    public RouterDb? RouterDb
    {
        get { lock (_lock) { return _routerDb; } }
    }

    public Router? Router
    {
        get { lock (_lock) { return _router; } }
    }

    public RestrictedAreaService? Restrictions
    {
        get { lock (_lock) { return _restrictions; } }
    }

    public string[] ProfileNames
    {
        get { lock (_lock) { return (string[])_profileNames.Clone(); } }
    }

    public void Set(RouterDb routerDb, Router router, RestrictedAreaService restrictions, string odrgPath, string[] profileNames)
    {
        ArgumentNullException.ThrowIfNull(routerDb);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(restrictions);
        ArgumentNullException.ThrowIfNull(profileNames);
        lock (_lock)
        {
            _routerDb = routerDb;
            _router = router;
            _restrictions = restrictions;
            _loadedOdrgPath = odrgPath;
            _profileNames = profileNames;
        }
    }
}
