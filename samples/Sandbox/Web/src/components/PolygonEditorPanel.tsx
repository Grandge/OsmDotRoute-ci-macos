import { useState } from 'react';
import { registerPolygonRestriction, type Kind } from '../api/client';
import { panelStyle, h2Style, btnStyle, inputStyle, errorStyle, BUILTIN_DIFFICULTIES } from './styles';
import { useI18n } from '../i18n';

interface Props {
  drawing: boolean;
  vertices: [number, number][]; // [lon, lat]
  onStartDrawing: () => void;
  onCancelDrawing: () => void;
  onUndoVertex: () => void;
  onPolygonRegistered: () => void;
}

export function PolygonEditorPanel({
  drawing,
  vertices,
  onStartDrawing,
  onCancelDrawing,
  onUndoVertex,
  onPolygonRegistered,
}: Props) {
  const { t } = useI18n();
  const [kind, setKind] = useState<Kind>('block');
  const [difficulty, setDifficulty] = useState<string>('flooding');
  const [tag, setTag] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleRegister() {
    if (vertices.length < 3) {
      setError(t('pg.needThree'));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await registerPolygonRestriction({
        kind,
        difficultyType: kind === 'difficulty' ? difficulty : undefined,
        outerBoundary: vertices.map(([lon, lat]) => ({ latitude: lat, longitude: lon })),
        tag: tag.trim() === '' ? undefined : tag.trim(),
      });
      onPolygonRegistered();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section style={panelStyle}>
      <h2 style={h2Style}>{t('pg.title')}</h2>
      {!drawing && (
        <button onClick={onStartDrawing} style={btnStyle}>{t('pg.startDraw')}</button>
      )}
      {drawing && (
        <>
          <p style={{ fontSize: 12, color: '#6b7280', margin: '0 0 6px' }}>
            {t('pg.hint')}
          </p>
          <div style={{ fontSize: 13, marginBottom: 6 }}>{t('pg.vertexCountPrefix')}<strong>{vertices.length}</strong></div>
          <div style={{ display: 'flex', gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
            <button onClick={onUndoVertex} disabled={vertices.length === 0} style={btnStyle}>{t('pg.undoVertex')}</button>
            <button onClick={onCancelDrawing} style={btnStyle}>{t('common.cancel')}</button>
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
          <button
            onClick={handleRegister}
            disabled={busy || vertices.length < 3}
            style={{ ...btnStyle, marginTop: 8 }}
          >
            {busy ? t('common.registering') : t('common.register')}
          </button>
          {error && <p style={errorStyle}>{error}</p>}
        </>
      )}
    </section>
  );
}
