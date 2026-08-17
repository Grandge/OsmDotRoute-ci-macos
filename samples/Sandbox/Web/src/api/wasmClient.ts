// client.ts と同一インターフェースの WASM バックエンド実装（Phase 3 ステップ 3J.5、J-4a）。
// Vite の resolve.alias（--mode wasm）により、各コンポーネントの '../api/client' import が
// 本モジュールへ差し替わる。型は client.ts から再エクスポートして完全一致させる。

import { getInterop, odrgUrl } from './wasmRuntime';
import type {
  VersionResponse, RegionInfo, CachedPbfInfo, RestrictionItem, BrowseResult,
  StatsResponse, RouteResponse, SnapResponse, ExtractCompleteEvent, Kind, CoordinateDto,
  GmlImportOptions, GmlImportResult,
} from './client';

export type {
  VersionResponse, ErrorResponse, RegionInfo, CachedPbfInfo, Kind, RestrictionItem,
  CoordinateDto, BrowseResult, StatsResponse, RouteResponse, SnapResponse,
  ExtractPhaseEvent, ExtractCompleteEvent, GmlImportOptions, GmlImportResult,
} from './client';

const NOT_SUPPORTED =
  'この機能は WASM デモでは利用できません（PBF ダウンロード / 抽出はローカル版 Sandbox を使用してください）。';

export async function fetchVersion(): Promise<VersionResponse> {
  const interop = await getInterop();
  return { name: 'OsmDotRoute', version: interop.Version() };
}

// WASM モードでは odrgFile は data/ 配下の事前ビルド .odrg ファイル名。
export async function loadOdrg(odrgFile: string): Promise<StatsResponse> {
  const interop = await getInterop();
  const resp = await fetch(odrgUrl(odrgFile));
  if (!resp.ok) throw new Error(`.odrg fetch failed: HTTP ${resp.status}`);
  const bytes = new Uint8Array(await resp.arrayBuffer());
  return JSON.parse(interop.LoadOdrg(bytes)) as StatsResponse;
}

// ユーザーがアップロードした .odrg ファイルをブラウザ内でロードする。
export async function loadOdrgFile(file: File): Promise<StatsResponse> {
  const interop = await getInterop();
  const bytes = new Uint8Array(await file.arrayBuffer());
  return JSON.parse(interop.LoadOdrg(bytes)) as StatsResponse;
}

export async function fetchGraphStats(): Promise<StatsResponse> {
  const interop = await getInterop();
  return JSON.parse(interop.GetStats()) as StatsResponse;
}

export async function fetchRoadNetwork(): Promise<GeoJSON.FeatureCollection> {
  const interop = await getInterop();
  return JSON.parse(interop.GetRoadNetwork()) as GeoJSON.FeatureCollection;
}

export async function calculateRoute(req: {
  fromLat: number; fromLon: number; toLat: number; toLon: number; profile: string;
}): Promise<RouteResponse> {
  const interop = await getInterop();
  return JSON.parse(interop.CalculateRoute(JSON.stringify(req))) as RouteResponse;
}

export async function snapToRoad(lat: number, lon: number, profile: string): Promise<SnapResponse> {
  const interop = await getInterop();
  return JSON.parse(interop.Snap(JSON.stringify({ lat, lon, profile }))) as SnapResponse;
}

export async function fetchMeshGrid(
  sw: [number, number],
  ne: [number, number],
  level: '1km' | '500m' | '250m' | '125m',
): Promise<GeoJSON.FeatureCollection> {
  const interop = await getInterop();
  return JSON.parse(interop.MeshGrid(sw[0], sw[1], ne[0], ne[1], level)) as GeoJSON.FeatureCollection;
}

export async function registerPolygonRestriction(req: {
  kind: Kind; difficultyType?: string; outerBoundary: CoordinateDto[]; tag?: string;
}): Promise<{ id: string }> {
  const interop = await getInterop();
  return JSON.parse(interop.AddPolygonRestriction(JSON.stringify(req))) as { id: string };
}

export async function registerMeshRestriction(req: {
  kind: Kind; difficultyType?: string; meshCodes: number[]; tag?: string;
}): Promise<{ id: string }> {
  const interop = await getInterop();
  return JSON.parse(interop.AddMeshRestriction(JSON.stringify(req))) as { id: string };
}

export async function importGml(file: File, options: GmlImportOptions): Promise<GmlImportResult> {
  const interop = await getInterop();
  const bytes = new Uint8Array(await file.arrayBuffer());
  const res = JSON.parse(interop.AddGmlRestriction(bytes, JSON.stringify(options))) as {
    ids: string[];
    acceptedCount: number;
  };
  return { acceptedCount: res.acceptedCount };
}

export async function listRestrictions(): Promise<{ items: RestrictionItem[] }> {
  const interop = await getInterop();
  return JSON.parse(interop.ListRestrictions()) as { items: RestrictionItem[] };
}

// WASM モード: サーバーファイルシステムが無いためブラウザのダウンロードで保存する。
// downloaded=true、path はダウンロードファイル名。
export async function saveRestrictions(): Promise<{ path: string; downloaded: boolean }> {
  const { items } = await listRestrictions();
  const payload = { format: 'osmdotroute-restrictions', version: 1, items };
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  const stamp = new Date().toISOString().slice(0, 10).replace(/-/g, '');
  const fileName = `restrictions-${stamp}.json`;
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
  return { path: fileName, downloaded: true };
}

export async function fetchRestrictionsGeoJson(): Promise<GeoJSON.FeatureCollection> {
  const interop = await getInterop();
  return JSON.parse(interop.RestrictionsGeoJson()) as GeoJSON.FeatureCollection;
}

export async function deleteRestriction(id: string): Promise<void> {
  const interop = await getInterop();
  interop.DeleteRestriction(id);
}

export async function clearAllRestrictions(): Promise<void> {
  const interop = await getInterop();
  interop.ClearRestrictions();
}

// App 起動時に呼ばれるため無害化（WASM ではキャッシュディレクトリ概念なし）。
export async function fetchCacheDir(): Promise<{ path: string }> {
  return { path: '' };
}

// 以下は DownloadPanel / ExtractPanel / FileBrowserDialog 専用（WASM モードでは非表示）。
// import 解決のためにエクスポートは保つが、呼ばれた場合は明示エラーとする。
export async function fetchRegions(): Promise<RegionInfo[]> {
  throw new Error(NOT_SUPPORTED);
}

export async function fetchCacheStatus(): Promise<{ items: CachedPbfInfo[] }> {
  throw new Error(NOT_SUPPORTED);
}

export async function setCacheDir(_path: string): Promise<{ path: string }> {
  throw new Error(NOT_SUPPORTED);
}

export async function downloadPbf(
  _region: string,
  _onProgress: (downloaded: number, total: number) => void,
): Promise<{ path: string; sizeBytes: number }> {
  throw new Error(NOT_SUPPORTED);
}

export async function deleteCachedPbf(_region: string): Promise<void> {
  throw new Error(NOT_SUPPORTED);
}

export async function browseDirectory(
  _path: string | null,
  _pattern: string | null,
): Promise<BrowseResult> {
  throw new Error(NOT_SUPPORTED);
}

export async function extractOdrg(
  _pbfPath: string,
  _bbox: [number, number, number, number],
  _profiles: string[],
  _onPhase: (phase: string, message: string) => void,
): Promise<ExtractCompleteEvent> {
  throw new Error(NOT_SUPPORTED);
}
