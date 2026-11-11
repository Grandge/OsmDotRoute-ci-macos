import type { CSSProperties } from 'react';

export const panelStyle: CSSProperties = {
  background: '#f9fafb',
  border: '1px solid #d1d5db',
  borderRadius: 6,
  padding: 12,
  marginBottom: 10,
};

export const h2Style: CSSProperties = {
  margin: '0 0 8px',
  fontSize: 14,
  fontWeight: 600,
};

export const btnStyle: CSSProperties = {
  padding: '4px 10px',
  fontSize: 13,
  cursor: 'pointer',
};

export const inputStyle: CSSProperties = {
  padding: '4px 6px',
  fontSize: 13,
  fontFamily: 'inherit',
};

export const errorStyle: CSSProperties = {
  color: '#b91c1c',
  margin: '8px 0 0',
  fontSize: 13,
};

export const BUILTIN_DIFFICULTIES = [
  'flooding',
  'liquefaction',
  'landslide',
  'construction',
  'obstacle',
  'congestion',
  'snow',
  'ice',
] as const;
