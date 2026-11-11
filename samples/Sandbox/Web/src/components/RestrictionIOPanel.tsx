import { useRef, useState } from 'react';
import {
  saveRestrictions, registerMeshRestriction, registerPolygonRestriction,
  type RestrictionItem,
} from '../api/client';
import { panelStyle, h2Style, btnStyle, errorStyle } from './styles';
import { useI18n } from '../i18n';

const FORMAT = 'osmdotroute-restrictions';

interface SavedFile {
  format: string;
  version: number;
  items: RestrictionItem[];
}

interface Props {
  // 制約が変化したら親に再描画を促す（読込後）。
  onChanged: () => void;
}

// メッシュ / ポリゴン制約を JSON ファイルへ保存・復元する。
// 保存は saveRestrictions（Server=保存先フォルダへ書込 / WASM=ブラウザダウンロード）、
// 読込はアップロード JSON を register* で再登録する。Server / WASM 両モードで動作する。
export function RestrictionIOPanel({ onChanged }: Props) {
  const { t } = useI18n();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  async function handleSave() {
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const res = await saveRestrictions();
      setInfo(res.downloaded ? t('rio.downloaded', { path: res.path }) : t('rio.savedTo', { path: res.path }));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function handleLoadFile(file: File) {
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const text = await file.text();
      const parsed = JSON.parse(text) as Partial<SavedFile>;
      if (parsed.format !== FORMAT || !Array.isArray(parsed.items)) {
        throw new Error(t('rio.invalidFile'));
      }
      let ok = 0;
      let skipped = 0;
      for (const item of parsed.items) {
        const difficultyType = item.kind === 'difficulty' ? item.difficultyType ?? undefined : undefined;
        const tag = item.tag ?? undefined;
        if (item.shapeType === 'mesh' && item.meshCodes && item.meshCodes.length > 0) {
          await registerMeshRestriction({ kind: item.kind, difficultyType, meshCodes: item.meshCodes, tag });
          ok++;
        } else if (item.shapeType === 'polygon' && item.outerBoundary && item.outerBoundary.length >= 3) {
          await registerPolygonRestriction({ kind: item.kind, difficultyType, outerBoundary: item.outerBoundary, tag });
          ok++;
        } else {
          skipped++;
        }
      }
      setInfo(t('rio.loaded', { ok, skipped }));
      onChanged();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  }

  return (
    <section style={panelStyle}>
      <h2 style={h2Style}>{t('rio.title')}</h2>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <button onClick={handleSave} disabled={busy} style={btnStyle}>{t('rio.save')}</button>
        <button onClick={() => fileInputRef.current?.click()} disabled={busy} style={btnStyle}>{t('rio.load')}</button>
        <input
          ref={fileInputRef}
          type="file"
          accept=".json,application/json"
          style={{ display: 'none' }}
          onChange={(e) => {
            const f = e.target.files?.[0];
            if (f) void handleLoadFile(f);
          }}
        />
      </div>
      <p style={{ fontSize: 12, color: '#6b7280', margin: '6px 0 0' }}>{t('rio.hint')}</p>
      {info && <p style={{ fontSize: 13, margin: '6px 0 0', color: '#15803d' }}>{info}</p>}
      {error && <p style={errorStyle}>{error}</p>}
    </section>
  );
}
