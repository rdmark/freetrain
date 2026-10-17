export const messages = {
  en: {
    title: 'FreeTrain', subtitle: 'Web prototype · Foundation', language: 'Language',
    fixture: 'Static fixture', notice: 'Reference data only. Browser access to the headless simulation is planned for stage 4.',
    run: 'Run', pause: 'Pause', step: 'Single step', mapTitle: 'Reference world',
    mapPending: 'Map renderer planned for stage 5', mapSize: '64 × 64 tiles · 16 levels',
    inspector: 'Train inspector', train: 'Train', cars: 'Cars', controller: 'Controller',
    automatic: 'Automatic', manual: 'Manual', station: 'Station', stationName: 'Station name',
    draft: 'Local draft · not submitted', resetDraft: 'Reset draft', buildings: 'Building catalog',
    pier: 'Railroad pier', condo: 'Condominium', fixed: 'Fixed height', variable: 'Variable height',
    cash: 'Cash', tick: 'Tick', paused: 'Paused', scenario: 'Reference scenario v1',
  },
  ja: {
    title: 'FreeTrain', subtitle: 'ウェブ試作版 · 基盤', language: '言語',
    fixture: '静的サンプル', notice: '参照データのみ。ブラウザーとシミュレーションの接続は工程4で実装予定です。',
    run: '運転', pause: '一時停止', step: '1ステップ', mapTitle: '参照ワールド',
    mapPending: 'マップ描画は工程5で実装予定', mapSize: '64 × 64マス · 高さ16層',
    inspector: '列車情報', train: '列車', cars: '両数', controller: '運転方式',
    automatic: '自動', manual: '手動', station: '駅', stationName: '駅名',
    draft: 'ローカルの下書き · 未送信', resetDraft: '下書きを戻す', buildings: '建物一覧',
    pier: '鉄道橋脚', condo: '集合住宅', fixed: '高さ固定', variable: '高さ可変',
    cash: '資金', tick: '経過ステップ', paused: '一時停止中', scenario: '参照シナリオ v1',
  },
} as const;
export type Locale = keyof typeof messages;
export function resolveLocale(value: string | null | undefined): Locale {
  return value?.toLowerCase().split('-')[0] === 'ja' ? 'ja' : 'en';
}
export function initialLocale(): Locale {
  try {
    const saved = localStorage.getItem('freetrain.locale');
    if (saved === 'en' || saved === 'ja') return saved;
  } catch { /* A blocked storage setting must not prevent startup. */ }
  return resolveLocale(navigator.language);
}
export function persistLocale(locale: Locale): void {
  document.documentElement.lang = locale;
  try { localStorage.setItem('freetrain.locale', locale); } catch { /* Preference is optional. */ }
}
