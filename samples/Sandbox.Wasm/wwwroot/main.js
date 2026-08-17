import { dotnet } from './_framework/dotnet.js'

const statusEl = document.getElementById('status');
const outEl = document.getElementById('out');

try {
    const { getAssemblyExports, getConfig } = await dotnet
        .withDiagnosticTracing(false)
        .create();

    const config = getConfig();
    const exports = await getAssemblyExports(config.mainAssemblyName);
    const interop = exports.Sandbox.Wasm.Interop;

    statusEl.textContent = 'fetching tsushima.odrg…';
    const resp = await fetch('./data/tsushima.odrg');
    if (!resp.ok) throw new Error(`fetch .odrg failed: HTTP ${resp.status}`);
    const bytes = new Uint8Array(await resp.arrayBuffer());

    const stats = JSON.parse(interop.LoadOdrg(bytes));

    // 津島市範囲内で経路計算（既存 E2E テストと同じ座標）
    const routeReq = JSON.stringify({
        fromLat: 35.18, fromLon: 136.73, toLat: 35.19, toLon: 136.74, profile: 'car',
    });
    const route = JSON.parse(interop.CalculateRoute(routeReq));

    statusEl.innerHTML = '<span class="ok">OK — routing engine runs entirely in the browser (no server)</span>';
    outEl.textContent = JSON.stringify({
        stats: {
            vertexCount: stats.vertexCount,
            edgeCount: stats.edgeCount,
            profileNames: stats.profileNames,
            southWest: stats.southWest,
            northEast: stats.northEast,
        },
        route: {
            found: route.found,
            distanceM: route.distanceM,
            durationSec: route.durationSec,
            shapePoints: route.geometry ? route.geometry.coordinates.length : 0,
        },
    }, null, 2);

    await dotnet.run();
} catch (e) {
    statusEl.innerHTML = `<span class="err">FAILED: ${e}</span>`;
    console.error(e);
}
