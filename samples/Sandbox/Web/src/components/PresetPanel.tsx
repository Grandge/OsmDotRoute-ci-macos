import { useRef, useState } from 'react';
import { loadOdrg, loadOdrgFile, type StatsResponse } from '../api/client';
import { panelStyle, h2Style, btnStyle, inputStyle, errorStyle } from './styles';
import { useI18n } from '../i18n';

// WASM デモ専用。事前ビルド .odrg（data/ 同梱）を選んでブラウザ内ロードする（Phase 3 ステップ 3J.5、J-3）。
const PRESETS = [{ file: 'tokyo.odrg', labelKey: 'pp.presetTokyo' }];

export function PresetPanel({ onLoaded }: { onLoaded: (stats: StatsResponse) => void }) {
  const { t } = useI18n();
  const [file, setFile] = useState(PRESETS[0].file);
  const [loading, setLoading] = useState(false);
  const [uploadMode, setUploadMode] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  async function handleLoad() {
    setLoading(true);
    setUploadMode(false);
    setError(null);
    try {
      const stats = await loadOdrg(file);
      onLoaded(stats);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }

  async function handleUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const picked = e.target.files?.[0];
    if (!picked) return;
    setLoading(true);
    setUploadMode(true);
    setError(null);
    try {
      const stats = await loadOdrgFile(picked);
      onLoaded(stats);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  }

  return (
    <div style={panelStyle}>
      <h3 style={h2Style}>{t('pp.title')}</h3>
      <select
        value={file}
        onChange={(e) => setFile(e.target.value)}
        disabled={loading}
        style={{ ...inputStyle, width: '100%', marginBottom: 8 }}
      >
        {PRESETS.map((p) => (
          <option key={p.file} value={p.file}>{t(p.labelKey)}</option>
        ))}
      </select>
      <button onClick={handleLoad} disabled={loading} style={{ ...btnStyle, width: '100%' }}>
        {loading && !uploadMode ? t('pp.loading') : t('common.load')}
      </button>

      <hr style={{ border: 'none', borderTop: '1px solid #e2e8f0', margin: '10px 0' }} />

      <input
        ref={fileInputRef}
        type="file"
        accept=".odrg"
        onChange={handleUpload}
        disabled={loading}
        style={{ display: 'none' }}
      />
      <button
        onClick={() => fileInputRef.current?.click()}
        disabled={loading}
        style={{ ...btnStyle, width: '100%' }}
      >
        {loading && uploadMode ? t('pp.loading') : t('pp.upload')}
      </button>
      <div style={{ fontSize: 11, color: '#6b7280', marginTop: 4 }}>
        {t('pp.uploadHint')}
      </div>

      <div style={{ fontSize: 11, color: '#6b7280', marginTop: 8 }}>
        {t('pp.desc')}
      </div>
      {error && <p style={errorStyle}>{error}</p>}
    </div>
  );
}
