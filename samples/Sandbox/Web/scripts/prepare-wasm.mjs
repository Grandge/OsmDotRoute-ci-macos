// Sandbox.Wasm を publish し、_framework / data を React の public/ へコピーする（Phase 3 ステップ 3J.5、J-5a）。
// これにより vite build --mode wasm が単一静的サイト（React + WASM + 事前ビルド .odrg）を生成できる。
import { execSync } from 'node:child_process';
import { cpSync, rmSync, mkdirSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const webDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');     // samples/Sandbox/Web
const repoRoot = resolve(webDir, '..', '..', '..');                        // リポジトリルート
const wasmProj = resolve(repoRoot, 'samples', 'Sandbox.Wasm', 'Sandbox.Wasm.csproj');
const publishDir = resolve(repoRoot, 'samples', 'Sandbox.Wasm', 'bin', 'Release', 'net9.0', 'publish');
const publishWww = resolve(publishDir, 'wwwroot');
const publicDir = resolve(webDir, 'public');

// 古いフィンガープリント付きアセットが残ると dist に dead file が混入するため publish 出力を一掃する。
console.log('[prepare-wasm] cleaning previous publish output …');
rmSync(publishDir, { recursive: true, force: true });

console.log('[prepare-wasm] dotnet publish Sandbox.Wasm …');
execSync(`dotnet publish "${wasmProj}" -c Release`, { stdio: 'inherit' });

for (const sub of ['_framework', 'data']) {
  const src = resolve(publishWww, sub);
  const dst = resolve(publicDir, sub);
  if (!existsSync(src)) {
    throw new Error(`[prepare-wasm] expected publish output missing: ${src}`);
  }
  rmSync(dst, { recursive: true, force: true });
  mkdirSync(dst, { recursive: true });
  cpSync(src, dst, { recursive: true });
  console.log(`[prepare-wasm] copied ${sub} -> public/${sub}`);
}

console.log('[prepare-wasm] done');
