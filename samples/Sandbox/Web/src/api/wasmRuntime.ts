// ブラウザ内 .NET WASM ランタイムの遅延初期化と Interop ブリッジ取得（Phase 3 ステップ 3J.5）。
// dotnet.js / .odrg は単一静的サイトの BASE_URL 直下（_framework/ ・ data/）に配置される。

export interface WasmInterop {
  LoadOdrg(bytes: Uint8Array): string;
  GetStats(): string;
  GetRoadNetwork(): string;
  CalculateRoute(json: string): string;
  Snap(json: string): string;
  AddPolygonRestriction(json: string): string;
  AddMeshRestriction(json: string): string;
  AddGmlRestriction(bytes: Uint8Array, optionsJson: string): string;
  ListRestrictions(): string;
  RestrictionsGeoJson(): string;
  DeleteRestriction(id: string): void;
  ClearRestrictions(): void;
  MeshGrid(swLat: number, swLon: number, neLat: number, neLon: number, level: string): string;
  Version(): string;
}

let interopPromise: Promise<WasmInterop> | null = null;

export function getInterop(): Promise<WasmInterop> {
  interopPromise ??= init();
  return interopPromise;
}

/** BASE_URL を考慮した data/ 配下のアセット URL を解決する。 */
export function odrgUrl(file: string): string {
  const base = import.meta.env.BASE_URL || '/';
  return new URL(`${base}data/${file}`, location.origin).href;
}

async function init(): Promise<WasmInterop> {
  const base = import.meta.env.BASE_URL || '/';
  const dotnetUrl = new URL(`${base}_framework/dotnet.js`, location.origin).href;
  const { dotnet } = await import(/* @vite-ignore */ dotnetUrl);
  const { getAssemblyExports, getConfig } = await dotnet
    .withDiagnosticTracing(false)
    .create();
  const config = getConfig();
  const exports = await getAssemblyExports(config.mainAssemblyName);
  return exports.Sandbox.Wasm.Interop as WasmInterop;
}
