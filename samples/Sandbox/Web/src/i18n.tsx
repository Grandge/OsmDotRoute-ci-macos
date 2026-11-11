import { createContext, useCallback, useContext, useState, type ReactNode } from 'react';

export type Lang = 'ja' | 'en';

type Dict = Record<string, string>;

const en: Dict = {
  // common
  'common.browse': 'Browse',
  'common.apply': 'Apply',
  'common.clear': 'Clear',
  'common.cancel': 'Cancel',
  'common.register': 'Register',
  'common.registering': 'Registering...',
  'common.delete': 'Delete',
  'common.load': 'Load',
  'common.kind': 'Kind',
  'common.difficultyType': 'Difficulty type',
  'common.tagOptional': 'Tag (optional)',
  'kind.block': 'block (no-entry)',
  'kind.difficulty': 'difficulty (difficult area)',
  // DownloadPanel
  'dl.saveLocation': 'Save Location',
  'dl.pbfSource': 'PBF Source',
  'dl.download': 'Download',
  'dl.redownload': 'Re-download',
  'dl.downloading': 'Downloading...',
  'dl.browsePbf': 'Browse PBF...',
  'dl.cachedPrefix': 'Cached: ',
  'dl.bbox': 'Bbox',
  'dir.west': 'West',
  'dir.east': 'East',
  'dir.south': 'South',
  'dir.north': 'North',
  'dl.drawOnMap': 'Draw on map',
  'dl.selectSaveFolder': 'Select save folder',
  'dl.selectPbfFile': 'Select PBF file',
  // ExtractPanel
  'ex.title': 'Extract / Load .odrg',
  'ex.selectPbfFirst': 'Select a PBF source first',
  'ex.drawBboxFirst': 'Draw a bbox on the map',
  'ex.profiles': 'Profiles:',
  'ex.extract': 'Extract',
  'ex.extracting': 'Extracting...',
  'ex.loadOdrg': 'Load .odrg...',
  'ex.loading': 'Loading...',
  'ex.starting': 'Starting...',
  'ex.verticesPrefix': 'Vertices: ',
  'ex.edgesPrefix': 'Edges: ',
  'ex.filePrefix': 'File: ',
  'ex.timePrefix': 'Time: ',
  'ex.selectOdrgFile': 'Select .odrg file',
  // RoutePanel
  'rt.title': 'Route',
  'rt.from': 'From',
  'rt.to': 'To',
  'rt.clickMap': '(click map)',
  'rt.pick': 'Pick',
  'rt.picking': 'Picking...',
  'rt.route': 'Route',
  'rt.reroute': 'Re-Route',
  'rt.calculating': 'Calculating...',
  'rt.profilePrefix': 'Profile: ',
  'rt.noRoute': 'No route found',
  // MeshGridPanel
  'mg.title': 'Mesh grid',
  'mg.level': 'Level',
  'mg.level1km': '1km (level 3)',
  'mg.level500m': '500m (1/2 subdivision)',
  'mg.level250m': '250m (1/4 subdivision)',
  'mg.level125m': '125m (1/8 subdivision)',
  'mg.fetching': 'Fetching...',
  'mg.drawMesh': 'Draw mesh for current view',
  'mg.hint': 'Click a mesh on the map, then choose attributes below and register',
  'mg.selectedMeshPrefix': 'Selected mesh: ',
  // PolygonEditorPanel
  'pg.title': 'Polygon',
  'pg.startDraw': 'Start drawing with mouse',
  'pg.hint': 'Click the map to add vertices, fill in the form below, then "Register"',
  'pg.vertexCountPrefix': 'Vertices: ',
  'pg.undoVertex': 'Undo 1 vertex',
  'pg.needThree': 'A polygon needs at least 3 vertices.',
  // RestrictionListPanel
  'rl.title': 'Registered restrictions',
  'rl.reload': 'Reload',
  'rl.clearAll': 'Clear all',
  'rl.confirmClear': 'Delete all {n} restrictions?',
  'rl.empty': 'No restrictions registered',
  'rl.colKind': 'Kind',
  'rl.colDifficulty': 'Difficulty',
  'rl.colShape': 'Shape',
  'rl.colTag': 'Tag',
  // GmlImportPanel
  'gml.title': 'Import GML file',
  'gml.import': 'Import',
  'gml.importing': 'Importing...',
  'gml.imported': 'Imported {n} item(s) ({sec}s)',
  'gml.filterByBounds': 'Filter by current map view',
  'gml.needBounds': 'Current map view is unavailable (load a graph first).',
  // RestrictionIOPanel
  'rio.title': 'Save / load restrictions',
  'rio.save': 'Save to JSON',
  'rio.load': 'Load from JSON',
  'rio.hint': 'Save the current restrictions to a file, or restore them from a saved JSON.',
  'rio.savedTo': 'Saved to: {path}',
  'rio.downloaded': 'Downloaded: {path}',
  'rio.loaded': 'Loaded {ok} restriction(s) ({skipped} skipped)',
  'rio.invalidFile': 'Not a valid restrictions JSON file.',
  // PresetPanel
  'pp.title': 'Load a pre-built .odrg',
  'pp.presetTokyo': 'Around Tokyo Station',
  'pp.loading': 'Loading...',
  'pp.upload': 'Upload .odrg',
  'pp.uploadHint': 'Load an .odrg file you extracted locally',
  'pp.desc': 'Runs routing, restrictions, and Re-Route in the browser (no server).',
  // Map overlay
  'map.roadNetwork': 'Road network',
  'map.roadNetworkError': 'Road network error',
  // FileBrowserDialog
  'fb.drive': 'Drive:',
  'fb.foldersPrefix': 'Folders: ',
  'fb.filesPrefix': 'Files: ',
  'fb.loading': 'Loading...',
  'fb.pastePath': 'Paste path...',
  'fb.go': 'Go',
  'fb.selectThisFolder': 'Select this folder',
  'fb.select': 'Select',
};

const ja: Dict = {
  // common
  'common.browse': '参照',
  'common.apply': '適用',
  'common.clear': 'クリア',
  'common.cancel': 'キャンセル',
  'common.register': '登録',
  'common.registering': '登録中…',
  'common.delete': '削除',
  'common.load': '読込',
  'common.kind': '種別',
  'common.difficultyType': '難所タイプ',
  'common.tagOptional': 'タグ (任意)',
  'kind.block': 'block (進入不可)',
  'kind.difficulty': 'difficulty (難所)',
  // DownloadPanel
  'dl.saveLocation': '保存先',
  'dl.pbfSource': 'PBF ソース',
  'dl.download': 'ダウンロード',
  'dl.redownload': '再ダウンロード',
  'dl.downloading': 'ダウンロード中...',
  'dl.browsePbf': 'PBF を参照...',
  'dl.cachedPrefix': 'キャッシュ済: ',
  'dl.bbox': '範囲 (Bbox)',
  'dir.west': '西',
  'dir.east': '東',
  'dir.south': '南',
  'dir.north': '北',
  'dl.drawOnMap': 'マップ上で描画',
  'dl.selectSaveFolder': '保存先フォルダを選択',
  'dl.selectPbfFile': 'PBF ファイルを選択',
  // ExtractPanel
  'ex.title': '.odrg の抽出 / 読込',
  'ex.selectPbfFirst': '先に PBF ソースを選択してください',
  'ex.drawBboxFirst': 'マップ上で bbox を描画してください',
  'ex.profiles': 'プロファイル:',
  'ex.extract': '抽出',
  'ex.extracting': '抽出中...',
  'ex.loadOdrg': '.odrg を読込...',
  'ex.loading': '読込中...',
  'ex.starting': '開始中...',
  'ex.verticesPrefix': '頂点数: ',
  'ex.edgesPrefix': 'エッジ数: ',
  'ex.filePrefix': 'ファイル: ',
  'ex.timePrefix': '所要: ',
  'ex.selectOdrgFile': '.odrg ファイルを選択',
  // RoutePanel
  'rt.title': 'ルート',
  'rt.from': '出発',
  'rt.to': '目的',
  'rt.clickMap': '(マップをクリック)',
  'rt.pick': '指定',
  'rt.picking': '指定中...',
  'rt.route': '探索',
  'rt.reroute': '再探索',
  'rt.calculating': '計算中...',
  'rt.profilePrefix': 'プロファイル: ',
  'rt.noRoute': '経路が見つかりません',
  // MeshGridPanel
  'mg.title': 'メッシュグリッド',
  'mg.level': '階層',
  'mg.level1km': '1km (第3次)',
  'mg.level500m': '500m (1/2 細分)',
  'mg.level250m': '250m (1/4 細分)',
  'mg.level125m': '125m (1/8 細分)',
  'mg.fetching': '取得中…',
  'mg.drawMesh': '現在の表示範囲のメッシュを描画',
  'mg.hint': 'マップ上のメッシュをクリック → 下の登録フォームで属性を選んで登録',
  'mg.selectedMeshPrefix': '選択中メッシュ: ',
  // PolygonEditorPanel
  'pg.title': 'ポリゴン作成',
  'pg.startDraw': 'マウスで描画開始',
  'pg.hint': 'マップをクリックして頂点を追加 → 下のフォームで属性を入力 → 「登録」',
  'pg.vertexCountPrefix': '頂点数: ',
  'pg.undoVertex': '1 頂点取消',
  'pg.needThree': 'ポリゴンは 3 頂点以上必要です。',
  // RestrictionListPanel
  'rl.title': '登録済み制約',
  'rl.reload': '再読込',
  'rl.clearAll': '全削除',
  'rl.confirmClear': '全制約 {n} 件を削除しますか?',
  'rl.empty': '制約は登録されていません',
  'rl.colKind': '種別',
  'rl.colDifficulty': '難所',
  'rl.colShape': '形状',
  'rl.colTag': 'タグ',
  // GmlImportPanel
  'gml.title': 'GML ファイル インポート',
  'gml.import': 'インポート',
  'gml.importing': 'インポート中…',
  'gml.imported': '{n} 件をインポートしました ({sec} 秒)',
  'gml.filterByBounds': '現在のマップ範囲でフィルタする',
  'gml.needBounds': '現在のマップ範囲が未取得です（先にグラフを読み込んでください）。',
  // RestrictionIOPanel
  'rio.title': '制約の保存 / 読込',
  'rio.save': 'JSON に保存',
  'rio.load': 'JSON から読込',
  'rio.hint': '現在の制約をファイルに保存、または保存済み JSON から復元します。',
  'rio.savedTo': '保存しました: {path}',
  'rio.downloaded': 'ダウンロードしました: {path}',
  'rio.loaded': '{ok} 件を読み込みました（{skipped} 件スキップ）',
  'rio.invalidFile': '制約 JSON ファイルではありません。',
  // PresetPanel
  'pp.title': '事前ビルド .odrg を読み込む',
  'pp.presetTokyo': '東京駅周辺',
  'pp.loading': '読み込み中…',
  'pp.upload': '.odrg をアップロード',
  'pp.uploadHint': '自分でローカル抽出した .odrg ファイルを読み込む',
  'pp.desc': 'ブラウザ内で経路計算・制約・Re-Route を実行します（サーバー不要）。',
  // Map overlay
  'map.roadNetwork': '道路ネットワーク',
  'map.roadNetworkError': '道路NW エラー',
  // FileBrowserDialog
  'fb.drive': 'ドライブ:',
  'fb.foldersPrefix': 'フォルダ: ',
  'fb.filesPrefix': 'ファイル: ',
  'fb.loading': '読込中...',
  'fb.pastePath': 'パスを貼り付け...',
  'fb.go': '移動',
  'fb.selectThisFolder': 'このフォルダを選択',
  'fb.select': '選択',
};

const messages: Record<Lang, Dict> = { ja, en };

const STORAGE_KEY = 'sandbox-lang';

function detectInitialLang(): Lang {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === 'ja' || saved === 'en') return saved;
  } catch {
    // localStorage unavailable
  }
  if (typeof navigator !== 'undefined' && navigator.language?.toLowerCase().startsWith('ja')) {
    return 'ja';
  }
  return 'en';
}

export type TranslateFn = (key: string, params?: Record<string, string | number>) => string;

interface I18nContextValue {
  lang: Lang;
  setLang: (lang: Lang) => void;
  t: TranslateFn;
}

const I18nContext = createContext<I18nContextValue | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(detectInitialLang);

  const setLang = useCallback((next: Lang) => {
    setLangState(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // localStorage unavailable
    }
  }, []);

  const t = useCallback<TranslateFn>(
    (key, params) => {
      let s = messages[lang][key] ?? en[key] ?? key;
      if (params) {
        for (const [k, v] of Object.entries(params)) {
          s = s.replace(`{${k}}`, String(v));
        }
      }
      return s;
    },
    [lang],
  );

  return <I18nContext.Provider value={{ lang, setLang, t }}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18nContextValue {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error('useI18n must be used within I18nProvider');
  return ctx;
}
