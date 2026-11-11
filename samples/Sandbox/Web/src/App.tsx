import { useEffect, useRef, useState, type CSSProperties } from 'react';
import type maplibregl from 'maplibre-gl';
import { MapView, type MapViewHandle } from './components/MapView';
import { DownloadPanel } from './components/DownloadPanel';
import { ExtractPanel } from './components/ExtractPanel';
import { PresetPanel } from './components/PresetPanel';
import { RoutePanel, type PickMode } from './components/RoutePanel';
import { MeshGridPanel } from './components/MeshGridPanel';
import { PolygonEditorPanel } from './components/PolygonEditorPanel';
import { GmlImportPanel } from './components/GmlImportPanel';
import { RestrictionListPanel } from './components/RestrictionListPanel';
import { RestrictionIOPanel } from './components/RestrictionIOPanel';
import { panelStyle } from './components/styles';
import {
  fetchVersion, fetchRoadNetwork, fetchCacheDir, fetchRestrictionsGeoJson,
  type VersionResponse, type ExtractCompleteEvent, type StatsResponse, type RouteResponse,
} from './api/client';
import { WEB_VERSION } from './version';
import { useI18n } from './i18n';

// J-4a: --mode wasm でビルドした静的サイトはブラウザ内 WASM エンジンで動作する。
// その場合 PBF ダウンロード / 抽出は不可のため該当パネルを隠し、事前ビルド .odrg プルダウンを出す。
const isWasm = import.meta.env.MODE === 'wasm';

export function App() {
  const { lang, setLang, t } = useI18n();
  const mapRef = useRef<MapViewHandle>(null);
  const [version, setVersion] = useState<VersionResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [pbfPath, setPbfPath] = useState<string | null>(null);
  const [bbox, setBbox] = useState<[number, number, number, number] | null>(null);
  const [graphLoaded, setGraphLoaded] = useState(false);
  const [cacheDir, setCacheDir] = useState('');
  const [availableProfiles, setAvailableProfiles] = useState<string[]>([]);

  const [pickMode, setPickMode] = useState<PickMode>('idle');
  const [from, setFrom] = useState<[number, number] | null>(null);
  const [to, setTo] = useState<[number, number] | null>(null);

  // メッシュ / ポリゴン / 制約
  const [mapBounds, setMapBounds] = useState<{ sw: [number, number]; ne: [number, number] } | null>(null);
  const [selectedMeshCode, setSelectedMeshCode] = useState<number | null>(null);
  const [drawing, setDrawing] = useState(false);
  const [vertices, setVertices] = useState<[number, number][]>([]);
  const [restrictionsNonce, setRestrictionsNonce] = useState(0);

  // 道路ネットワーク表示
  const [roadNetworkData, setRoadNetworkData] = useState<GeoJSON.FeatureCollection | null>(null);
  const [roadNetworkVisible, setRoadNetworkVisible] = useState(false);
  const [roadNetworkError, setRoadNetworkError] = useState<string | null>(null);

  useEffect(() => {
    fetchVersion().then(setVersion).catch((e: Error) => setError(e.message));
    fetchCacheDir().then((r) => setCacheDir(r.path)).catch(() => {});
  }, []);

  // roadNetworkData / roadNetworkVisible が変わったら MapLibre レイヤーに反映する。
  // loadRoadNetwork や toggle からの直接呼び出しに依存せず、state を信頼できる唯一の真実とする。
  useEffect(() => {
    if (roadNetworkVisible && roadNetworkData) {
      mapRef.current?.setRoadNetwork(roadNetworkData);
    } else {
      mapRef.current?.setRoadNetwork(null);
    }
  }, [roadNetworkData, roadNetworkVisible]);

  function handlePbfReady(_regionKey: string, path: string) {
    setPbfPath(path);
  }

  function handleStartBboxDraw() {
    mapRef.current?.startBboxDraw();
  }

  function handleBboxDrawn(newBbox: [number, number, number, number]) {
    setBbox(newBbox);
    mapRef.current?.setBboxRect(newBbox);
  }

  function handleClearBbox() {
    setBbox(null);
    setRoadNetworkData(null);
    setRoadNetworkVisible(false);
    setRoadNetworkError(null);
    mapRef.current?.setBboxRect(null);
    mapRef.current?.setRoute(null);
    mapRef.current?.setRouteEndpoints({});
    mapRef.current?.setMeshGrid(null);
    mapRef.current?.setRestrictions(null);
    mapRef.current?.setPolygonDraft([]);
    setGraphLoaded(false);
    setAvailableProfiles([]);
    setFrom(null);
    setTo(null);
    setPickMode('idle');
    setSelectedMeshCode(null);
    setDrawing(false);
    setVertices([]);
  }

  function handleBboxManualChange(newBbox: [number, number, number, number]) {
    setBbox(newBbox);
    mapRef.current?.setBboxRect(newBbox);
  }

  async function loadRoadNetwork(bounds?: { southWest: { latitude: number; longitude: number }; northEast: { latitude: number; longitude: number } }) {
    setGraphLoaded(true);
    setRoadNetworkError(null);
    try {
      const geojson = await fetchRoadNetwork();
      setRoadNetworkData(geojson);
      setRoadNetworkVisible(false);
      if (bounds) {
        mapRef.current?.fitBounds([
          [bounds.southWest.longitude, bounds.southWest.latitude],
          [bounds.northEast.longitude, bounds.northEast.latitude],
        ]);
      } else if (bbox) {
        mapRef.current?.fitBounds([[bbox[0], bbox[1]], [bbox[2], bbox[3]]]);
      }
    } catch (e) {
      console.error('[OsmDotRoute] Road network load failed:', e);
      setRoadNetworkError(e instanceof Error ? e.message : String(e));
    }
    // 新しいグラフをロードしたら制約表示をリセット（サーバー側も新しい RestrictedAreaService）
    mapRef.current?.setRestrictions(null);
    setRestrictionsNonce((n) => n + 1);
  }

  function handleRoadNetworkToggle() {
    setRoadNetworkVisible((prev) => !prev);
  }

  async function handleExtracted(result: ExtractCompleteEvent) {
    setAvailableProfiles(result.profileNames);
    await loadRoadNetwork();
  }

  async function handleLoaded(stats: StatsResponse) {
    setAvailableProfiles(stats.profileNames);
    const loadedBbox: [number, number, number, number] = [
      stats.southWest.longitude,
      stats.southWest.latitude,
      stats.northEast.longitude,
      stats.northEast.latitude,
    ];
    setBbox(loadedBbox);
    mapRef.current?.setBboxRect(loadedBbox);
    await loadRoadNetwork(stats);
  }

  function handleMapClick(lngLat: { lng: number; lat: number }, feature: maplibregl.MapGeoJSONFeature | null) {
    // 1. ポリゴン描画を最優先
    if (drawing) {
      setVertices((v) => {
        const nv: [number, number][] = [...v, [lngLat.lng, lngLat.lat]];
        mapRef.current?.setPolygonDraft(nv);
        return nv;
      });
      return;
    }
    // 2. ルートピックモード
    if (pickMode === 'pickFrom') {
      const pt: [number, number] = [lngLat.lat, lngLat.lng];
      setFrom(pt);
      setPickMode('idle');
      mapRef.current?.setRouteEndpoints({ from: pt, to: to ?? undefined });
      return;
    }
    if (pickMode === 'pickTo') {
      const pt: [number, number] = [lngLat.lat, lngLat.lng];
      setTo(pt);
      setPickMode('idle');
      mapRef.current?.setRouteEndpoints({ from: from ?? undefined, to: pt });
      return;
    }
    // 3. メッシュ選択
    const meshCode = feature?.properties?.meshCode;
    if (meshCode != null) {
      setSelectedMeshCode(Number(meshCode));
    }
  }

  function handleRouteResult(result: RouteResponse) {
    mapRef.current?.setRoute(result.found && result.geometry ? result.geometry : null);
  }

  function handleClearRoute() {
    setFrom(null);
    setTo(null);
    setPickMode('idle');
    mapRef.current?.setRoute(null);
    mapRef.current?.setRouteEndpoints({});
  }

  // --- メッシュ ---
  function handleMeshGridFetched(fc: GeoJSON.FeatureCollection | null) {
    mapRef.current?.setMeshGrid(fc);
  }

  async function refreshRestrictions() {
    setRestrictionsNonce((n) => n + 1);
    try {
      const geojson = await fetchRestrictionsGeoJson();
      mapRef.current?.setRestrictions(geojson);
    } catch {
      // optional
    }
  }

  // --- ポリゴン ---
  function handleStartDrawing() {
    setDrawing(true);
    setVertices([]);
    mapRef.current?.setPolygonDraft([]);
  }

  function handleCancelDrawing() {
    setDrawing(false);
    setVertices([]);
    mapRef.current?.setPolygonDraft([]);
  }

  function handleUndoVertex() {
    setVertices((v) => {
      const nv = v.slice(0, -1);
      mapRef.current?.setPolygonDraft(nv);
      return nv;
    });
  }

  function handlePolygonRegistered() {
    setDrawing(false);
    setVertices([]);
    mapRef.current?.setPolygonDraft([]);
    void refreshRestrictions();
  }

  return (
    <div style={rootStyle}>
      <div style={sidebarStyle}>
        <div style={panelStyle}>
          <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 8 }}>
            <h2 style={{ margin: 0, fontSize: 16 }}>OsmDotRoute Sandbox</h2>
            <div style={{ display: 'flex', gap: 4, flexShrink: 0 }}>
              <button onClick={() => setLang('ja')} style={langBtnStyle(lang === 'ja')}>日本語</button>
              <button onClick={() => setLang('en')} style={langBtnStyle(lang === 'en')}>EN</button>
            </div>
          </div>
          <div style={{ fontSize: 12, color: '#6b7280', marginTop: 4 }}>
            Web {WEB_VERSION}
            {isWasm ? <> / WASM (in-browser)</> : version && <> / Server {version.version}</>}
            {error && <span style={{ color: '#dc2626' }}> ({isWasm ? 'wasm' : 'server'} error)</span>}
          </div>
        </div>

        {isWasm ? (
          <PresetPanel onLoaded={handleLoaded} />
        ) : (
          <>
            <DownloadPanel
              onPbfReady={handlePbfReady}
              bbox={bbox}
              onStartBboxDraw={handleStartBboxDraw}
              onClearBbox={handleClearBbox}
              onBboxManualChange={handleBboxManualChange}
              onCacheDirChanged={setCacheDir}
            />

            <ExtractPanel
              pbfPath={pbfPath}
              bbox={bbox}
              availableProfiles={availableProfiles}
              onExtracted={handleExtracted}
              onLoaded={handleLoaded}
              cacheDir={cacheDir}
            />
          </>
        )}

        <RoutePanel
          graphLoaded={graphLoaded}
          availableProfiles={availableProfiles}
          from={from}
          to={to}
          pickMode={pickMode}
          onSetPickMode={setPickMode}
          onRouteResult={handleRouteResult}
          onClearRoute={handleClearRoute}
        />

        {graphLoaded && (
          <>
            <MeshGridPanel
              currentBounds={mapBounds}
              selectedMeshCode={selectedMeshCode}
              onMeshGridFetched={handleMeshGridFetched}
              onMeshRegistered={refreshRestrictions}
              onClearSelection={() => setSelectedMeshCode(null)}
            />
            <PolygonEditorPanel
              drawing={drawing}
              vertices={vertices}
              onStartDrawing={handleStartDrawing}
              onCancelDrawing={handleCancelDrawing}
              onUndoVertex={handleUndoVertex}
              onPolygonRegistered={handlePolygonRegistered}
            />
            <GmlImportPanel
              currentBounds={mapBounds}
              onImported={refreshRestrictions}
            />
            <RestrictionIOPanel onChanged={refreshRestrictions} />
            <RestrictionListPanel
              refreshNonce={restrictionsNonce}
              onChanged={refreshRestrictions}
            />
          </>
        )}
      </div>
      <div style={mapAreaStyle}>
        <MapView
          ref={mapRef}
          onBboxDrawn={handleBboxDrawn}
          onMapClick={handleMapClick}
          onBoundsChange={(sw, ne) => setMapBounds({ sw, ne })}
        />
        {graphLoaded && (
          <label style={mapOverlayToggleStyle}>
            <input
              type="checkbox"
              checked={roadNetworkVisible}
              onChange={handleRoadNetworkToggle}
              style={{ marginRight: 5 }}
            />
            {t('map.roadNetwork')}
          </label>
        )}
        {roadNetworkError && (
          <div style={mapOverlayErrorStyle}>
            {t('map.roadNetworkError')}: {roadNetworkError}
          </div>
        )}
      </div>
    </div>
  );
}

const rootStyle: CSSProperties = {
  display: 'flex',
  width: '100vw',
  height: '100vh',
  margin: 0,
  padding: 0,
  fontFamily: 'system-ui, -apple-system, sans-serif',
};

const sidebarStyle: CSSProperties = {
  width: 360,
  minWidth: 360,
  height: '100%',
  overflowY: 'auto',
  padding: 10,
  boxSizing: 'border-box',
  background: '#fff',
  borderRight: '1px solid #e5e7eb',
};

const mapAreaStyle: CSSProperties = {
  flex: 1,
  height: '100%',
  position: 'relative',
};

const mapOverlayToggleStyle: CSSProperties = {
  position: 'absolute',
  top: 10,
  left: 10,
  zIndex: 10,
  display: 'flex',
  alignItems: 'center',
  background: '#fff',
  borderRadius: 6,
  padding: '5px 10px',
  fontSize: 13,
  boxShadow: '0 1px 3px rgba(0,0,0,0.2)',
  cursor: 'pointer',
  userSelect: 'none',
};

const mapOverlayErrorStyle: CSSProperties = {
  position: 'absolute',
  top: 45,
  left: 10,
  zIndex: 10,
  background: '#fee2e2',
  color: '#991b1b',
  borderRadius: 6,
  padding: '5px 10px',
  fontSize: 12,
  maxWidth: 340,
  boxShadow: '0 1px 3px rgba(0,0,0,0.2)',
};

function langBtnStyle(active: boolean): CSSProperties {
  return {
    padding: '2px 8px',
    fontSize: 11,
    cursor: 'pointer',
    borderRadius: 4,
    border: active ? '1px solid #2563eb' : '1px solid #d1d5db',
    background: active ? '#2563eb' : '#fff',
    color: active ? '#fff' : '#374151',
  };
}
