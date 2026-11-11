import { useCallback, useEffect, useState } from 'react';
import { browseDirectory, type BrowseResult } from '../api/client';
import { useI18n } from '../i18n';

interface Props {
  title?: string;
  pattern?: string;
  rememberKey?: string;
  initialPath?: string;
  folderMode?: boolean;
  onClose: () => void;
  onSelect: (path: string) => void;
}

export function FileBrowserDialog({ title = 'Select', pattern, rememberKey, initialPath, folderMode, onClose, onSelect }: Props) {
  const { t } = useI18n();
  const [data, setData] = useState<BrowseResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedFile, setSelectedFile] = useState<string | null>(null);
  const [directInput, setDirectInput] = useState('');

  const activePattern = folderMode ? null : (pattern ?? null);

  const load = useCallback(
    async (path: string | null) => {
      setLoading(true);
      setError(null);
      try {
        const r = await browseDirectory(path, activePattern);
        setData(r);
        setSelectedFile(null);
        setDirectInput(r.currentPath);
      } catch (e) {
        setError((e as Error).message);
      } finally {
        setLoading(false);
      }
    },
    [activePattern],
  );

  useEffect(() => {
    const start = initialPath ?? (rememberKey ? localStorage.getItem(rememberKey) : null);
    void load(start);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function joinPath(base: string, name: string): string {
    const sep = base.includes('/') && !base.includes('\\') ? '/' : '\\';
    return base.endsWith(sep) ? base + name : base + sep + name;
  }

  function commitPath(fullPath: string) {
    if (rememberKey && data) localStorage.setItem(rememberKey, data.currentPath);
    onSelect(fullPath);
  }

  function commitFile(filename: string) {
    if (!data) return;
    commitPath(joinPath(data.currentPath, filename));
  }

  function commitFolder() {
    if (!data) return;
    commitPath(data.currentPath);
  }

  async function handleDirectInput() {
    const trimmed = directInput.trim();
    if (trimmed === '') return;
    try {
      await browseDirectory(trimmed, activePattern);
      void load(trimmed);
    } catch {
      commitPath(trimmed);
    }
  }

  const canSelect = folderMode ? !!data : !!selectedFile;

  return (
    <div style={overlayStyle} onClick={onClose}>
      <div style={modalStyle} onClick={(e) => e.stopPropagation()}>
        <div style={headerStyle}>
          <strong style={{ fontSize: 14 }}>{title}</strong>
          <button onClick={onClose} style={closeBtnStyle}>&times;</button>
        </div>

        {data && data.drives.length > 0 && (
          <div style={driveRowStyle}>
            <span style={{ fontSize: 12, color: '#6b7280' }}>{t('fb.drive')}</span>
            {data.drives.map((d) => (
              <button key={d} onClick={() => load(d)} style={driveBtnStyle}>
                {d.replace('\\', '')}
              </button>
            ))}
          </div>
        )}

        {data && (
          <div style={statusRowStyle}>
            <span>
              {t('fb.foldersPrefix')}<strong>{data.directories.length}</strong>
              {!folderMode && <> / {t('fb.filesPrefix')}<strong>{data.files.length}</strong></>}
              {pattern && !folderMode && (
                <> (<code style={{ background: '#e5e7eb', padding: '0 4px', borderRadius: 2 }}>{pattern}</code>)</>
              )}
            </span>
          </div>
        )}

        <div style={listStyle}>
          {loading && <div style={messageStyle}>{t('fb.loading')}</div>}
          {error && <div style={{ ...messageStyle, color: '#dc2626' }}>{error}</div>}
          {!loading && data && (
            <>
              {data.parentPath !== null && (
                <div onClick={() => load(data.parentPath)} style={rowStyle}>
                  <span style={iconStyle}>&#128193;</span>
                  <span>..</span>
                </div>
              )}
              {data.directories.map((d) => (
                <div key={d.name} onClick={() => load(joinPath(data.currentPath, d.name))} style={rowStyle}>
                  <span style={iconStyle}>&#128193;</span>
                  <span>{d.name}</span>
                </div>
              ))}
              {!folderMode && data.files.map((f) => (
                <div
                  key={f.name}
                  onClick={() => setSelectedFile(f.name)}
                  onDoubleClick={() => commitFile(f.name)}
                  style={{ ...rowStyle, background: selectedFile === f.name ? '#dbeafe' : undefined }}
                >
                  <span style={iconStyle}>&#128196;</span>
                  <span style={{ flex: 1 }}>{f.name}</span>
                  <span style={{ color: '#6b7280', fontSize: 11 }}>{formatSize(f.size)}</span>
                </div>
              ))}
            </>
          )}
        </div>

        <div style={inputRowStyle}>
          <input
            style={{ flex: 1, padding: '4px 6px', fontFamily: 'monospace', fontSize: 12 }}
            value={directInput}
            placeholder={t('fb.pastePath')}
            onChange={(e) => setDirectInput(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') void handleDirectInput(); }}
          />
          <button onClick={handleDirectInput} style={btnStyle}>{t('fb.go')}</button>
        </div>

        <div style={footerStyle}>
          <button onClick={onClose} style={btnStyle}>{t('common.cancel')}</button>
          <button
            onClick={() => folderMode ? commitFolder() : (selectedFile && commitFile(selectedFile))}
            disabled={!canSelect}
            style={{ ...btnStyle, background: canSelect ? '#16a34a' : '#9ca3af', color: '#fff' }}
          >
            {folderMode ? t('fb.selectThisFolder') : t('fb.select')}
          </button>
        </div>
      </div>
    </div>
  );
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1073741824) return `${(bytes / 1048576).toFixed(1)} MB`;
  return `${(bytes / 1073741824).toFixed(2)} GB`;
}

const overlayStyle: React.CSSProperties = { position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000 };
const modalStyle: React.CSSProperties = { width: 560, maxHeight: '80vh', background: '#fff', borderRadius: 8, display: 'flex', flexDirection: 'column', overflow: 'hidden', boxShadow: '0 10px 25px rgba(0,0,0,0.2)' };
const headerStyle: React.CSSProperties = { padding: '8px 12px', borderBottom: '1px solid #e5e7eb', display: 'flex', alignItems: 'center', justifyContent: 'space-between' };
const closeBtnStyle: React.CSSProperties = { border: 'none', background: 'transparent', fontSize: 20, cursor: 'pointer', padding: '0 6px' };
const driveRowStyle: React.CSSProperties = { padding: '6px 12px', borderBottom: '1px solid #e5e7eb', display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' };
const driveBtnStyle: React.CSSProperties = { padding: '2px 10px', fontSize: 12, border: '1px solid #d1d5db', borderRadius: 3, background: '#f3f4f6', cursor: 'pointer' };
const statusRowStyle: React.CSSProperties = { padding: '4px 12px', borderBottom: '1px solid #e5e7eb', fontSize: 12, color: '#374151', background: '#fafafa', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 };
const listStyle: React.CSSProperties = { flex: 1, overflowY: 'auto', minHeight: 200 };
const rowStyle: React.CSSProperties = { display: 'flex', gap: 8, alignItems: 'center', padding: '4px 12px', cursor: 'pointer', fontSize: 13, borderBottom: '1px solid #f3f4f6' };
const iconStyle: React.CSSProperties = { fontSize: 14 };
const messageStyle: React.CSSProperties = { padding: 16, fontSize: 12, color: '#6b7280' };
const inputRowStyle: React.CSSProperties = { padding: '6px 12px', borderTop: '1px solid #e5e7eb', display: 'flex', alignItems: 'center', gap: 8 };
const footerStyle: React.CSSProperties = { padding: '8px 12px', borderTop: '1px solid #e5e7eb', display: 'flex', gap: 8, justifyContent: 'flex-end' };
const btnStyle: React.CSSProperties = { padding: '4px 12px', fontSize: 13, border: '1px solid #d1d5db', borderRadius: 4, cursor: 'pointer' };
