import { useState } from 'react';
import { panelStyle } from './styles';
import { useI18n } from '../i18n';
import { calculateRoute, type RouteResponse } from '../api/client';

export type PickMode = 'idle' | 'pickFrom' | 'pickTo';

interface Props {
  graphLoaded: boolean;
  availableProfiles: string[];
  from: [number, number] | null;
  to: [number, number] | null;
  pickMode: PickMode;
  onSetPickMode: (mode: PickMode) => void;
  onRouteResult: (result: RouteResponse) => void;
  onClearRoute: () => void;
}

export function RoutePanel({ graphLoaded, availableProfiles, from, to, pickMode, onSetPickMode, onRouteResult, onClearRoute }: Props) {
  const { t } = useI18n();
  const [profile, setProfile] = useState<string>(availableProfiles[0] ?? 'car');

  // Auto-select first available profile when the list changes
  if (availableProfiles.length > 0 && !availableProfiles.includes(profile)) {
    setProfile(availableProfiles[0]);
  }
  const [calculating, setCalculating] = useState(false);
  const [result, setResult] = useState<RouteResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleRoute() {
    if (!from || !to) return;
    setError(null);
    setCalculating(true);
    try {
      const r = await calculateRoute({
        fromLat: from[0],
        fromLon: from[1],
        toLat: to[0],
        toLon: to[1],
        profile,
      });
      setResult(r);
      onRouteResult(r);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setCalculating(false);
    }
  }

  function handleClear() {
    setResult(null);
    setError(null);
    onClearRoute();
  }

  if (!graphLoaded) return null;

  const pickFromActive = pickMode === 'pickFrom';
  const pickToActive = pickMode === 'pickTo';

  return (
    <div style={panelStyle}>
      <h3 style={{ margin: '0 0 8px', fontSize: 14 }}>{t('rt.title')}</h3>

      <div style={{ display: 'grid', gridTemplateColumns: '40px 1fr auto', gap: '4px 6px', fontSize: 12, marginBottom: 8, alignItems: 'center' }}>
        <label>{t('rt.from')}</label>
        <span style={{ fontFamily: 'monospace', fontSize: 11 }}>
          {from ? `${from[0].toFixed(5)}, ${from[1].toFixed(5)}` : t('rt.clickMap')}
        </span>
        <button
          onClick={() => onSetPickMode(pickFromActive ? 'idle' : 'pickFrom')}
          style={{ ...pickBtnStyle, background: pickFromActive ? '#bbf7d0' : undefined }}
        >
          {pickFromActive ? t('rt.picking') : t('rt.pick')}
        </button>

        <label>{t('rt.to')}</label>
        <span style={{ fontFamily: 'monospace', fontSize: 11 }}>
          {to ? `${to[0].toFixed(5)}, ${to[1].toFixed(5)}` : t('rt.clickMap')}
        </span>
        <button
          onClick={() => onSetPickMode(pickToActive ? 'idle' : 'pickTo')}
          style={{ ...pickBtnStyle, background: pickToActive ? '#fecaca' : undefined }}
        >
          {pickToActive ? t('rt.picking') : t('rt.pick')}
        </button>
      </div>

      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 8 }}>
        <select value={profile} onChange={(e) => setProfile(e.target.value)} style={{ padding: '3px 6px' }}>
          {availableProfiles.map((p) => (
            <option key={p} value={p}>{p}</option>
          ))}
        </select>
        <button
          onClick={handleRoute}
          disabled={!from || !to || calculating}
          style={{ padding: '4px 12px' }}
        >
          {calculating ? t('rt.calculating') : result ? t('rt.reroute') : t('rt.route')}
        </button>
        <button onClick={handleClear} disabled={!result && !from && !to} style={{ padding: '4px 12px' }}>
          {t('common.clear')}
        </button>
      </div>

      {error && <div style={{ fontSize: 12, color: '#dc2626', marginBottom: 4 }}>{error}</div>}

      {result && (
        <div style={{ fontSize: 12 }}>
          {result.found ? (
            <div style={{ color: '#059669' }}>
              <div><strong>{(result.distanceM / 1000).toFixed(2)} km</strong> / <strong>{formatDuration(result.durationSec)}</strong></div>
              <div style={{ color: '#6b7280', fontSize: 11 }}>{t('rt.profilePrefix')}{profile}</div>
            </div>
          ) : (
            <div style={{ color: '#dc2626' }}>{t('rt.noRoute')}</div>
          )}
        </div>
      )}
    </div>
  );
}

function formatDuration(sec: number): string {
  if (sec < 60) return `${sec.toFixed(0)}s`;
  const m = Math.floor(sec / 60);
  const s = Math.round(sec % 60);
  if (m < 60) return `${m}m ${s}s`;
  const h = Math.floor(m / 60);
  const rm = m % 60;
  return `${h}h ${rm}m`;
}

const pickBtnStyle: React.CSSProperties = {
  padding: '2px 8px',
  fontSize: 11,
  borderRadius: 3,
};
