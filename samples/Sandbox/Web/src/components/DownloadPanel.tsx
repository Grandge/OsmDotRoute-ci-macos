import { useEffect, useState } from 'react';
import { panelStyle } from './styles';
import { useI18n } from '../i18n';
import { FileBrowserDialog } from './FileBrowserDialog';
import {
  fetchRegions,
  fetchCacheStatus,
  fetchCacheDir,
  setCacheDir,
  downloadPbf,
  type RegionInfo,
  type CachedPbfInfo,
} from '../api/client';

interface Props {
  onPbfReady: (regionKey: string, pbfPath: string) => void;
  bbox: [number, number, number, number] | null;
  onStartBboxDraw: () => void;
  onClearBbox: () => void;
  onBboxManualChange: (bbox: [number, number, number, number]) => void;
  onCacheDirChanged: (dir: string) => void;
}

export function DownloadPanel({ onPbfReady, bbox, onStartBboxDraw, onClearBbox, onBboxManualChange, onCacheDirChanged }: Props) {
  const { t } = useI18n();
  const [regions, setRegions] = useState<RegionInfo[]>([]);
  const [cached, setCached] = useState<Map<string, CachedPbfInfo>>(new Map());
  const [selectedRegion, setSelectedRegion] = useState('chubu');
  const [downloading, setDownloading] = useState(false);
  const [downloaded, setDownloaded] = useState(0);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [cacheDir, setCacheDirState] = useState('');
  const [cacheDirInput, setCacheDirInput] = useState('');
  const [cacheDirError, setCacheDirError] = useState<string | null>(null);
  const [showFolderBrowser, setShowFolderBrowser] = useState(false);
  const [showPbfBrowser, setShowPbfBrowser] = useState(false);

  useEffect(() => {
    fetchRegions().then(setRegions).catch(() => {});
    refreshCache();
    fetchCacheDir().then((r) => {
      setCacheDirState(r.path);
      setCacheDirInput(r.path);
    }).catch(() => {});
  }, []);

  function refreshCache() {
    fetchCacheStatus()
      .then((r) => setCached(new Map(r.items.map((c) => [c.regionKey, c]))))
      .catch(() => {});
  }

  const cachedInfo = cached.get(selectedRegion);
  const selectedInfo = regions.find((r) => r.key === selectedRegion);

  async function handleDownload() {
    setError(null);
    setDownloading(true);
    setDownloaded(0);
    setTotal(0);
    try {
      const result = await downloadPbf(selectedRegion, (dl, tot) => {
        setDownloaded(dl);
        setTotal(tot);
      });
      refreshCache();
      onPbfReady(selectedRegion, result.path);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setDownloading(false);
    }
  }

  async function handleChangeCacheDir(newPath?: string) {
    const target = newPath ?? cacheDirInput;
    setCacheDirError(null);
    try {
      const r = await setCacheDir(target);
      setCacheDirState(r.path);
      setCacheDirInput(r.path);
      onCacheDirChanged(r.path);
      refreshCache();
    } catch (e: unknown) {
      setCacheDirError(e instanceof Error ? e.message : String(e));
    }
  }

  function handleFolderSelected(folderPath: string) {
    setShowFolderBrowser(false);
    setCacheDirInput(folderPath);
    void handleChangeCacheDir(folderPath);
  }

  function handlePbfSelected(pbfPath: string) {
    setShowPbfBrowser(false);
    const name = pbfPath.replace(/\\/g, '/').split('/').pop() ?? '';
    const regionKey = name.replace(/-latest\.osm\.pbf$/, '');
    onPbfReady(regionKey, pbfPath);
  }

  const pct = total > 0 ? Math.round((downloaded / total) * 100) : 0;
  const cacheDirChanged = cacheDirInput !== cacheDir;

  return (
    <>
      <div style={panelStyle}>
        <h3 style={{ margin: '0 0 8px', fontSize: 14 }}>{t('dl.saveLocation')}</h3>
        <div style={{ display: 'flex', gap: 4, marginBottom: 4 }}>
          <input
            type="text"
            value={cacheDirInput}
            onChange={(e) => setCacheDirInput(e.target.value)}
            style={{ flex: 1, padding: '3px 6px', fontSize: 11, fontFamily: 'monospace' }}
          />
          <button onClick={() => setShowFolderBrowser(true)} style={{ padding: '3px 8px', fontSize: 11 }}>
            {t('common.browse')}
          </button>
          <button
            onClick={() => handleChangeCacheDir()}
            disabled={!cacheDirChanged}
            style={{ padding: '3px 8px', fontSize: 11 }}
          >
            {t('common.apply')}
          </button>
        </div>
        {cacheDirError && <div style={{ fontSize: 11, color: '#dc2626' }}>{cacheDirError}</div>}
      </div>

      <div style={panelStyle}>
        <h3 style={{ margin: '0 0 8px', fontSize: 14 }}>{t('dl.pbfSource')}</h3>

        <div style={{ marginBottom: 8 }}>
          <select
            value={selectedRegion}
            onChange={(e) => setSelectedRegion(e.target.value)}
            disabled={downloading}
            style={{ width: '100%', padding: '4px 6px' }}
          >
            {regions.map((r) => (
              <option key={r.key} value={r.key}>
                {r.displayName} ({r.key})
              </option>
            ))}
          </select>
          {selectedInfo && (
            <div style={{ fontSize: 11, color: '#6b7280', marginTop: 2 }}>{selectedInfo.description}</div>
          )}
        </div>

        <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 6, flexWrap: 'wrap' }}>
          <button onClick={handleDownload} disabled={downloading} style={{ padding: '4px 12px' }}>
            {downloading ? t('dl.downloading') : cachedInfo ? t('dl.redownload') : t('dl.download')}
          </button>
          <button onClick={() => setShowPbfBrowser(true)} style={{ padding: '4px 12px' }}>
            {t('dl.browsePbf')}
          </button>
          {cachedInfo && (
            <span style={{ fontSize: 12, color: '#059669' }}>
              {t('dl.cachedPrefix')}{formatBytes(cachedInfo.sizeBytes)}
            </span>
          )}
        </div>

        {downloading && (
          <div style={{ marginBottom: 6 }}>
            <div style={progressBarBg}>
              <div style={{ ...progressBarFill, width: `${pct}%` }} />
            </div>
            <div style={{ fontSize: 11, color: '#6b7280', marginTop: 2 }}>
              {formatBytes(downloaded)} / {total > 0 ? formatBytes(total) : '?'} ({pct}%)
            </div>
          </div>
        )}

        {error && <div style={{ fontSize: 12, color: '#dc2626', marginBottom: 6 }}>{error}</div>}
      </div>

      <div style={panelStyle}>
        <h3 style={{ margin: '0 0 8px', fontSize: 14 }}>{t('dl.bbox')}</h3>

        <div style={{ display: 'grid', gridTemplateColumns: '50px 1fr 8px 50px 1fr', gap: '4px 4px', fontSize: 12, marginBottom: 8, alignItems: 'center' }}>
          <label>{t('dir.west')}</label>
          <input type="number" step="0.01" value={bbox ? bbox[0].toFixed(4) : ''} onChange={(e) => { if (bbox) onBboxManualChange([parseFloat(e.target.value) || 0, bbox[1], bbox[2], bbox[3]]); }} style={{ width: '100%', padding: 2 }} />
          <div />
          <label>{t('dir.east')}</label>
          <input type="number" step="0.01" value={bbox ? bbox[2].toFixed(4) : ''} onChange={(e) => { if (bbox) onBboxManualChange([bbox[0], bbox[1], parseFloat(e.target.value) || 0, bbox[3]]); }} style={{ width: '100%', padding: 2 }} />
          <label>{t('dir.south')}</label>
          <input type="number" step="0.01" value={bbox ? bbox[1].toFixed(4) : ''} onChange={(e) => { if (bbox) onBboxManualChange([bbox[0], parseFloat(e.target.value) || 0, bbox[2], bbox[3]]); }} style={{ width: '100%', padding: 2 }} />
          <div />
          <label>{t('dir.north')}</label>
          <input type="number" step="0.01" value={bbox ? bbox[3].toFixed(4) : ''} onChange={(e) => { if (bbox) onBboxManualChange([bbox[0], bbox[1], bbox[2], parseFloat(e.target.value) || 0]); }} style={{ width: '100%', padding: 2 }} />
        </div>

        <div style={{ display: 'flex', gap: 8 }}>
          <button onClick={onStartBboxDraw} style={{ padding: '4px 12px' }}>{t('dl.drawOnMap')}</button>
          <button onClick={onClearBbox} disabled={!bbox} style={{ padding: '4px 12px' }}>{t('common.clear')}</button>
        </div>
      </div>

      {showFolderBrowser && (
        <FileBrowserDialog
          title={t('dl.selectSaveFolder')}
          folderMode
          initialPath={cacheDir}
          rememberKey="sandbox-cache-dir"
          onClose={() => setShowFolderBrowser(false)}
          onSelect={handleFolderSelected}
        />
      )}

      {showPbfBrowser && (
        <FileBrowserDialog
          title={t('dl.selectPbfFile')}
          pattern="*.osm.pbf;*.pbf"
          initialPath={cacheDir}
          rememberKey="sandbox-pbf-browse"
          onClose={() => setShowPbfBrowser(false)}
          onSelect={handlePbfSelected}
        />
      )}
    </>
  );
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1073741824) return `${(bytes / 1048576).toFixed(1)} MB`;
  return `${(bytes / 1073741824).toFixed(2)} GB`;
}

const progressBarBg: React.CSSProperties = { height: 8, background: '#e5e7eb', borderRadius: 4, overflow: 'hidden' };
const progressBarFill: React.CSSProperties = { height: '100%', background: '#3b82f6', borderRadius: 4, transition: 'width 0.2s' };
