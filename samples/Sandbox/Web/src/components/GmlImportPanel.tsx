import { useRef, useState } from 'react';
import { importGml, type Kind } from '../api/client';
import { panelStyle, h2Style, btnStyle, inputStyle, errorStyle, BUILTIN_DIFFICULTIES } from './styles';
import { useI18n } from '../i18n';

interface Props {
  currentBounds: { sw: [number, number]; ne: [number, number] } | null;
  onImported: () => void;
}

// KSJ スキーマ準拠 GML 3.2 ファイルをアップロードして block / difficulty 制約を一括登録する。
// Server モードは multipart アップロード、WASM モードはブラウザ内パース（client.ts の差し替えで吸収）。
export function GmlImportPanel({ currentBounds, onImported }: Props) {
  const { t } = useI18n();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [kind, setKind] = useState<Kind>('difficulty');
  const [difficulty, setDifficulty] = useState<string>('flooding');
  const [useBounds, setUseBounds] = useState(true);
  const [tag, setTag] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<string | null>(null);

  async function handleImport() {
    if (!file) return;
    if (useBounds && !currentBounds) {
      setError(t('gml.needBounds'));
      return;
    }
    setBusy(true);
    setError(null);
    setLastResult(null);
    try {
      const t0 = performance.now();
      const res = await importGml(file, {
        kind,
        difficultyType: kind === 'difficulty' ? difficulty : undefined,
        useMapBounds: useBounds,
        mapBoundsSouthWest: useBounds && currentBounds
          ? { latitude: currentBounds.sw[0], longitude: currentBounds.sw[1] }
          : undefined,
        mapBoundsNorthEast: useBounds && currentBounds
          ? { latitude: currentBounds.ne[0], longitude: currentBounds.ne[1] }
          : undefined,
        tag: tag.trim() === '' ? undefined : tag.trim(),
      });
      const elapsed = ((performance.now() - t0) / 1000).toFixed(1);
      setLastResult(t('gml.imported', { n: res.acceptedCount, sec: elapsed }));
      onImported();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section style={panelStyle}>
      <h2 style={h2Style}>{t('gml.title')}</h2>
      <div style={{ display: 'flex', gap: 8, marginBottom: 6, alignItems: 'center' }}>
        <input
          ref={fileInputRef}
          type="file"
          accept=".xml,.gml"
          onChange={(e) => { setFile(e.target.files?.[0] ?? null); setLastResult(null); }}
          disabled={busy}
          style={{ flex: 1, fontSize: 12 }}
        />
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '4px 8px', fontSize: 13, alignItems: 'center' }}>
        <label>{t('common.kind')}</label>
        <select value={kind} onChange={(e) => setKind(e.target.value as Kind)} style={inputStyle}>
          <option value="block">{t('kind.block')}</option>
          <option value="difficulty">{t('kind.difficulty')}</option>
        </select>
        {kind === 'difficulty' && (
          <>
            <label>{t('common.difficultyType')}</label>
            <select value={difficulty} onChange={(e) => setDifficulty(e.target.value)} style={inputStyle}>
              {BUILTIN_DIFFICULTIES.map((d) => (
                <option key={d} value={d}>{d}</option>
              ))}
            </select>
          </>
        )}
        <label>{t('common.tagOptional')}</label>
        <input value={tag} onChange={(e) => setTag(e.target.value)} style={inputStyle} />
      </div>
      <label style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 6, fontSize: 13 }}>
        <input
          type="checkbox"
          checked={useBounds}
          onChange={(e) => setUseBounds(e.target.checked)}
        />
        {t('gml.filterByBounds')}
      </label>
      <div style={{ marginTop: 8 }}>
        <button onClick={handleImport} disabled={busy || !file} style={btnStyle}>
          {busy ? t('gml.importing') : t('gml.import')}
        </button>
      </div>
      {lastResult && <p style={{ fontSize: 13, margin: '6px 0 0', color: '#15803d' }}>{lastResult}</p>}
      {error && <p style={errorStyle}>{error}</p>}
    </section>
  );
}
