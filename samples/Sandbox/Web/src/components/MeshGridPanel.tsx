import { useState } from 'react';
import { fetchMeshGrid, registerMeshRestriction, type Kind } from '../api/client';
import { panelStyle, h2Style, btnStyle, inputStyle, errorStyle, BUILTIN_DIFFICULTIES } from './styles';
import { useI18n } from '../i18n';

type Level = '1km' | '500m' | '250m' | '125m';

interface Props {
  currentBounds: { sw: [number, number]; ne: [number, number] } | null;
  selectedMeshCode: number | null;
  onMeshGridFetched: (geojson: GeoJSON.FeatureCollection | null) => void;
  onMeshRegistered: () => void;
  onClearSelection: () => void;
}

export function MeshGridPanel({
  currentBounds,
  selectedMeshCode,
  onMeshGridFetched,
  onMeshRegistered,
  onClearSelection,
}: Props) {
  const { t } = useI18n();
  const [level, setLevel] = useState<Level>('1km');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [visible, setVisible] = useState(false);

  const [kind, setKind] = useState<Kind>('block');
  const [difficulty, setDifficulty] = useState<string>('flooding');
  const [tag, setTag] = useState('');
  const [registerBusy, setRegisterBusy] = useState(false);

  async function handleShow() {
    if (!currentBounds) return;
    setBusy(true);
    setError(null);
    try {
      const fc = await fetchMeshGrid(currentBounds.sw, currentBounds.ne, level);
      onMeshGridFetched(fc);
      setVisible(true);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  function handleClear() {
    onMeshGridFetched(null);
    setVisible(false);
    onClearSelection();
  }

  async function handleRegister() {
    if (selectedMeshCode === null) return;
    setRegisterBusy(true);
    setError(null);
    try {
      await registerMeshRestriction({
        kind,
        difficultyType: kind === 'difficulty' ? difficulty : undefined,
        meshCodes: [selectedMeshCode],
        tag: tag.trim() === '' ? undefined : tag.trim(),
      });
      onMeshRegistered();
      onClearSelection();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setRegisterBusy(false);
    }
  }

  return (
    <section style={panelStyle}>
      <h2 style={h2Style}>{t('mg.title')}</h2>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <label>{t('mg.level')}</label>
        <select value={level} onChange={(e) => setLevel(e.target.value as Level)} disabled={busy} style={inputStyle}>
          <option value="1km">{t('mg.level1km')}</option>
          <option value="500m">{t('mg.level500m')}</option>
          <option value="250m">{t('mg.level250m')}</option>
          <option value="125m">{t('mg.level125m')}</option>
        </select>
      </div>
      <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
        <button onClick={handleShow} disabled={busy || !currentBounds} style={btnStyle}>
          {busy ? t('mg.fetching') : t('mg.drawMesh')}
        </button>
        <button onClick={handleClear} disabled={!visible} style={btnStyle}>{t('common.clear')}</button>
      </div>
      <p style={{ fontSize: 12, color: '#6b7280', margin: '6px 0 0' }}>
        {t('mg.hint')}
      </p>
      {error && <p style={errorStyle}>{error}</p>}

      {selectedMeshCode !== null && (
        <div style={{ marginTop: 10, padding: 8, background: '#eef2ff', border: '1px solid #c7d2fe', borderRadius: 4 }}>
          <div style={{ fontSize: 13, marginBottom: 6 }}>
            {t('mg.selectedMeshPrefix')}<code style={{ fontWeight: 600 }}>{selectedMeshCode}</code>
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '4px 8px', fontSize: 13 }}>
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
          <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
            <button onClick={handleRegister} disabled={registerBusy} style={btnStyle}>
              {registerBusy ? t('common.registering') : t('common.register')}
            </button>
            <button onClick={onClearSelection} disabled={registerBusy} style={btnStyle}>{t('common.cancel')}</button>
          </div>
        </div>
      )}
    </section>
  );
}
