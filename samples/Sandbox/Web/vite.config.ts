import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// J-4a: ビルドモードで API バックエンドを切替える。
//   dev / 通常 build → src/api/client.ts（ローカル Sandbox Server へ HTTP）
//   --mode wasm      → src/api/wasmClient.ts（ブラウザ内 WASM ルーティングエンジン）
// J-5a: WASM 成果物（_framework / data）は React の public 経由で単一静的サイトに同梱する。
export default defineConfig(({ mode }) => {
  const isWasm = mode === 'wasm';
  // GitHub Pages のサブパス対応（3J.6 で SANDBOX_BASE を設定）。未指定なら '/'。
  const base = (globalThis as { process?: { env?: Record<string, string | undefined> } })
    .process?.env?.SANDBOX_BASE ?? '/';

  return {
    base: isWasm ? base : '/',
    plugins: [react()],
    resolve: {
      alias: isWasm
        ? [{ find: /^(\.\.?)\/api\/client$/, replacement: '$1/api/wasmClient' }]
        : [],
    },
    server: {
      port: 5174,
      proxy: {
        '/api': 'http://127.0.0.1:5280',
      },
    },
  };
});
