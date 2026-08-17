export interface VersionResponse {
  name: string;
  version: string;
}

export interface ErrorResponse {
  error: string;
  message: string;
}

export interface RegionInfo {
  key: string;
  displayName: string;
  description: string;
}

export interface CachedPbfInfo {
  regionKey: string;
  displayName: string;
  sizeBytes: number;
  lastModifiedUtc: string;
}

async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let detail: ErrorResponse | undefined;
    try {
      detail = (await res.json()) as ErrorResponse;
    } catch {
      // ignore
    }
    throw new Error(detail?.message ?? `HTTP ${res.status} ${res.statusText}`);
  }
  return (await res.json()) as T;
}

export const fetchVersion = async () => handle<VersionResponse>(await fetch('/api/version'));

export const fetchRegions = async () => handle<RegionInfo[]>(await fetch('/api/regions'));

export const fetchCacheStatus = async () =>
  handle<{ items: CachedPbfInfo[] }>(await fetch('/api/cache/status'));

export const fetchCacheDir = async () =>
  handle<{ path: string }>(await fetch('/api/cache/dir'));

export async function setCacheDir(path: string): Promise<{ path: string }> {
  return handle<{ path: string }>(
    await fetch('/api/cache/dir', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path }),
    }),
  );
}

export async function downloadPbf(
  region: string,
  onProgress: (downloaded: number, total: number) => void,
): Promise<{ path: string; sizeBytes: number }> {
  const res = await fetch('/api/download', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ region }),
  });

  if (res.status === 409) {
    throw new Error('Another download is already in progress');
  }

  if (!res.ok && res.headers.get('content-type')?.includes('application/json')) {
    const err = (await res.json()) as ErrorResponse;
    throw new Error(err.message);
  }

  const reader = res.body!.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let result: { path: string; sizeBytes: number } | null = null;

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    const lines = buffer.split('\n');
    buffer = lines.pop()!;

    for (const line of lines) {
      if (!line.startsWith('data: ')) continue;
      const data = JSON.parse(line.slice(6));

      if (data.type === 'progress') {
        onProgress(data.downloaded, data.total);
      } else if (data.type === 'complete' || data.type === 'cached') {
        result = { path: data.path, sizeBytes: data.sizeBytes };
        onProgress(data.sizeBytes, data.sizeBytes);
      } else if (data.type === 'error') {
        throw new Error(data.message);
      }
    }
  }

  if (!result) throw new Error('Download stream ended without completion');
  return result;
}

export async function deleteCachedPbf(region: string): Promise<void> {
  const res = await fetch(`/api/cache/${region}`, { method: 'DELETE' });
  if (!res.ok && res.status !== 404) {
    throw new Error(`HTTP ${res.status}`);
  }
}

// --- Mesh / Restrictions ---

export type Kind = 'block' | 'difficulty';

export interface RestrictionItem {
  id: string;
  kind: Kind;
  difficultyType: string | null;
  shapeType: 'polygon' | 'mesh';
  outerBoundary: CoordinateDto[] | null;
  meshCodes: number[] | null;
  tag: string | null;
}

export interface CoordinateDto {
  latitude: number;
  longitude: number;
}

export interface GmlImportOptions {
  kind: Kind;
  difficultyType?: string;
  useMapBounds: boolean;
  mapBoundsSouthWest?: CoordinateDto;
  mapBoundsNorthEast?: CoordinateDto;
  tag?: string;
}

export interface GmlImportResult {
  acceptedCount: number;
}

export async function fetchMeshGrid(
  sw: [number, number],
  ne: [number, number],
  level: '1km' | '500m' | '250m' | '125m',
): Promise<GeoJSON.FeatureCollection> {
  const qs = new URLSearchParams({
    swLat: sw[0].toString(),
    swLon: sw[1].toString(),
    neLat: ne[0].toString(),
    neLon: ne[1].toString(),
    level,
  });
  const res = await fetch(`/api/mesh/grid?${qs}`);
  if (!res.ok) {
    let detail: ErrorResponse | undefined;
    try { detail = (await res.json()) as ErrorResponse; } catch { /* ignore */ }
    throw new Error(detail?.message ?? `HTTP ${res.status}`);
  }
  return (await res.json()) as GeoJSON.FeatureCollection;
}

export async function registerPolygonRestriction(req: {
  kind: Kind;
  difficultyType?: string;
  outerBoundary: CoordinateDto[];
  tag?: string;
}): Promise<{ id: string }> {
  return handle<{ id: string }>(
    await fetch('/api/restrictions/polygon', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    }),
  );
}

export async function registerMeshRestriction(req: {
  kind: Kind;
  difficultyType?: string;
  meshCodes: number[];
  tag?: string;
}): Promise<{ id: string }> {
  return handle<{ id: string }>(
    await fetch('/api/restrictions/mesh', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    }),
  );
}

export async function importGml(file: File, options: GmlImportOptions): Promise<GmlImportResult> {
  const fd = new FormData();
  fd.append('file', file);
  fd.append('kind', options.kind);
  if (options.difficultyType) fd.append('difficultyType', options.difficultyType);
  fd.append('useMapBounds', String(options.useMapBounds));
  if (options.useMapBounds && options.mapBoundsSouthWest && options.mapBoundsNorthEast) {
    fd.append('swLat', String(options.mapBoundsSouthWest.latitude));
    fd.append('swLon', String(options.mapBoundsSouthWest.longitude));
    fd.append('neLat', String(options.mapBoundsNorthEast.latitude));
    fd.append('neLon', String(options.mapBoundsNorthEast.longitude));
  }
  if (options.tag) fd.append('tag', options.tag);
  return handle<GmlImportResult>(
    await fetch('/api/restrictions/gml-upload', { method: 'POST', body: fd }),
  );
}

export async function listRestrictions(): Promise<{ items: RestrictionItem[] }> {
  return handle<{ items: RestrictionItem[] }>(await fetch('/api/restrictions'));
}

// Server モード: 制約を「保存先フォルダ設定」のフォルダにサーバー側で JSON 保存する。
// downloaded=false（フォルダ保存）。path はサーバー上の絶対パス。
export async function saveRestrictions(): Promise<{ path: string; downloaded: boolean }> {
  const res = await handle<{ path: string }>(
    await fetch('/api/restrictions/save', { method: 'POST' }),
  );
  return { path: res.path, downloaded: false };
}

export async function fetchRestrictionsGeoJson(): Promise<GeoJSON.FeatureCollection> {
  const res = await fetch('/api/restrictions/geojson');
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return (await res.json()) as GeoJSON.FeatureCollection;
}

export async function deleteRestriction(id: string): Promise<void> {
  const res = await fetch(`/api/restrictions/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
}

export async function clearAllRestrictions(): Promise<void> {
  const res = await fetch('/api/restrictions', { method: 'DELETE' });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
}

// --- Browse ---

export interface BrowseResult {
  currentPath: string;
  parentPath: string | null;
  directories: { name: string }[];
  files: { name: string; size: number }[];
  drives: string[];
}

export async function browseDirectory(path: string | null, pattern: string | null): Promise<BrowseResult> {
  const qs = new URLSearchParams();
  if (path) qs.set('path', path);
  if (pattern) qs.set('pattern', pattern);
  return handle<BrowseResult>(await fetch(`/api/files/browse?${qs}`));
}

// --- Load ---

export async function loadOdrg(odrgPath: string): Promise<StatsResponse> {
  return handle<StatsResponse>(
    await fetch('/api/load', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ odrgPath }),
    }),
  );
}

// .odrg ファイルのアップロードは WASM モード専用（wasmClient.ts が実装）。
// サーバーモードでは PresetPanel 自体が非表示のため呼ばれないが、import 解決のためスタブを置く。
export async function loadOdrgFile(_file: File): Promise<StatsResponse> {
  throw new Error('.odrg のアップロードは WASM デモのみ利用できます。');
}

// --- Route ---

export interface RouteResponse {
  found: boolean;
  distanceM: number;
  durationSec: number;
  geometry: GeoJSON.LineString | null;
}

export interface SnapResponse {
  snapped: { latitude: number; longitude: number } | null;
}

export async function snapToRoad(lat: number, lon: number, profile: string): Promise<SnapResponse> {
  return handle<SnapResponse>(
    await fetch('/api/snap', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ lat, lon, profile }),
    }),
  );
}

export async function calculateRoute(req: {
  fromLat: number;
  fromLon: number;
  toLat: number;
  toLon: number;
  profile: string;
}): Promise<RouteResponse> {
  return handle<RouteResponse>(
    await fetch('/api/route', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    }),
  );
}

// --- Extract ---

export interface ExtractPhaseEvent {
  type: 'phase';
  phase: string;
  message: string;
}

export interface ExtractCompleteEvent {
  type: 'complete';
  odrgPath: string;
  vertexCount: number;
  edgeCount: number;
  fileSizeBytes: number;
  extractSeconds: number;
  profileNames: string[];
}

export async function extractOdrg(
  pbfPath: string,
  bbox: [number, number, number, number],
  profiles: string[],
  onPhase: (phase: string, message: string) => void,
): Promise<ExtractCompleteEvent> {
  const res = await fetch('/api/extract', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ pbfPath, bbox, profiles }),
  });

  if (res.status === 409) {
    throw new Error('Another extraction is already in progress');
  }

  if (!res.ok && res.headers.get('content-type')?.includes('application/json')) {
    const err = (await res.json()) as ErrorResponse;
    throw new Error(err.message);
  }

  const reader = res.body!.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let result: ExtractCompleteEvent | null = null;

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    const lines = buffer.split('\n');
    buffer = lines.pop()!;

    for (const line of lines) {
      if (!line.startsWith('data: ')) continue;
      const data = JSON.parse(line.slice(6));

      if (data.type === 'phase') {
        onPhase(data.phase, data.message);
      } else if (data.type === 'complete') {
        result = data as ExtractCompleteEvent;
      } else if (data.type === 'error') {
        throw new Error(data.message);
      }
    }
  }

  if (!result) throw new Error('Extract stream ended without completion');
  return result;
}

// --- Graph ---

export interface StatsResponse {
  vertexCount: number;
  edgeCount: number;
  southWest: { latitude: number; longitude: number };
  northEast: { latitude: number; longitude: number };
  profileNames: string[];
}

export const fetchGraphStats = async () => handle<StatsResponse>(await fetch('/api/graph/stats'));

export async function fetchRoadNetwork(): Promise<GeoJSON.FeatureCollection> {
  const res = await fetch('/api/road-network');
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return (await res.json()) as GeoJSON.FeatureCollection;
}
