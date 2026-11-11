import { useEffect, useState } from 'react';
import { panelStyle } from './styles';
import { useI18n } from '../i18n';
import { FileBrowserDialog } from './FileBrowserDialog';
import { extractOdrg, loadOdrg, type ExtractCompleteEvent, type StatsResponse } from '../api/client';

interface Props {
  pbfPath: string | null;
  bbox: [number, number, number, number] | null;
  availableProfiles: string[];
  onExtracted: (result: ExtractCompleteEvent) => void;
  onLoaded: (stats: StatsResponse) => void;
  cacheDir: string;
}

const PROFILES = ['car', 'pedestrian', 'bicycle', 'truck'] as const;

export function ExtractPanel({ pbfPath, bbox, availableProfiles, onExtracted, onLoaded, cacheDir }: Props) {
  const { t } = useI18n();
  const [selectedProfiles, setSelectedProfiles] = useState<Set<string>>(new Set(['car', 'pedestrian']));

  // ロード済み .odrg のプロファイルセットをチェックボックスに反映
  useEffect(() => {
    if (availableProfiles.length > 0) {
      setSelectedProfiles(new Set(availableProfiles));
    }
  }, [availableProfiles]);
  const [extracting, setExtracting] = useState(false);
  const [phase, setPhase] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ExtractCompleteEvent | null>(null);
  const [showOdrgBrowser, setShowOdrgBrowser] = useState(false);
  const [loadingOdrg, setLoadingOdrg] = useState(false);

  function toggleProfile(p: string) {
    setSelectedProfiles((prev) => {
      const next = new Set(prev);
      if (next.has(p)) {
        if (next.size > 1) next.delete(p);
      } else {
        next.add(p);
      }
      return next;
    });
  }

  async function handleExtract() {
    if (!pbfPath || !bbox) return;
    setError(null);
    setResult(null);
    setExtracting(true);
    setPhase(t('ex.starting'));
    try {
      const r = await extractOdrg(
        pbfPath,
        bbox,
        [...selectedProfiles],
        (_phase, message) => setPhase(message),
      );
      setResult(r);
      setPhase('');
      onExtracted(r);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
      setPhase('');
    } finally {
      setExtracting(false);
    }
  }

  async function handleLoadOdrg(odrgPath: string) {
    setShowOdrgBrowser(false);
    setError(null);
    setResult(null);
    setLoadingOdrg(true);
    try {
      const stats = await loadOdrg(odrgPath);
      setResult({
        type: 'complete',
        odrgPath,
        vertexCount: stats.vertexCount,
        edgeCount: stats.edgeCount,
        fileSizeBytes: 0,
        extractSeconds: 0,
        profileNames: stats.profileNames,
      });
      onLoaded(stats);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoadingOdrg(false);
    }
  }

  const ready = !!pbfPath && !!bbox;

  return (
    <div style={panelStyle}>
      <h3 style={{ margin: '0 0 8px', fontSize: 14 }}>{t('ex.title')}</h3>

      {!ready && (
        <div style={{ fontSize: 12, color: '#6b7280', marginBottom: 8 }}>
          {!pbfPath ? t('ex.selectPbfFirst') : t('ex.drawBboxFirst')}
        </div>
      )}

      <div style={{ marginBottom: 8 }}>
        <div style={{ fontSize: 12, marginBottom: 4 }}>{t('ex.profiles')}</div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {PROFILES.map((p) => (
            <label key={p} style={{ fontSize: 12, cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={selectedProfiles.has(p)}
                onChange={() => toggleProfile(p)}
                disabled={extracting}
              />{' '}
              {p}
            </label>
          ))}
        </div>
      </div>

      <div style={{ display: 'flex', gap: 8, marginBottom: 6 }}>
        <button
          onClick={handleExtract}
          disabled={!ready || extracting || loadingOdrg}
          style={{ padding: '4px 12px' }}
        >
          {extracting ? t('ex.extracting') : t('ex.extract')}
        </button>
        <button
          onClick={() => setShowOdrgBrowser(true)}
          disabled={extracting || loadingOdrg}
          style={{ padding: '4px 12px' }}
        >
          {loadingOdrg ? t('ex.loading') : t('ex.loadOdrg')}
        </button>
      </div>

      {extracting && phase && (
        <div style={{ fontSize: 12, color: '#2563eb', marginBottom: 4 }}>{phase}</div>
      )}

      {error && <div style={{ fontSize: 12, color: '#dc2626', marginBottom: 4 }}>{error}</div>}

      {result && (
        <div style={{ fontSize: 12, color: '#059669' }}>
          <div>{t('ex.verticesPrefix')}{result.vertexCount.toLocaleString()}</div>
          <div>{t('ex.edgesPrefix')}{result.edgeCount.toLocaleString()}</div>
          {result.fileSizeBytes > 0 && <div>{t('ex.filePrefix')}{formatBytes(result.fileSizeBytes)}</div>}
          {result.extractSeconds > 0 && <div>{t('ex.timePrefix')}{result.extractSeconds.toFixed(1)}s</div>}
        </div>
      )}

      {showOdrgBrowser && (
        <FileBrowserDialog
          title={t('ex.selectOdrgFile')}
          pattern="*.odrg"
          initialPath={cacheDir}
          rememberKey="sandbox-odrg-browse"
          onClose={() => setShowOdrgBrowser(false)}
          onSelect={handleLoadOdrg}
        />
      )}
    </div>
  );
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1073741824) return `${(bytes / 1048576).toFixed(1)} MB`;
  return `${(bytes / 1073741824).toFixed(2)} GB`;
}
