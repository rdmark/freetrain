export const messages = {
  en: {
    title: 'FreeTrain', subtitle: 'Web prototype', language: 'Language',
    network: 'Live reference game', networkNotice: 'Connected to the authoritative game service. Map rendering arrives in stage 5.',
    accessRequired: 'Operator access required', incompatible: 'Client and server versions differ. Reload after updating.',
    operatorSecret: 'Operator access key', connect: 'Connect', accessFailed: 'Access failed. Check the key and try again.',
    unresolved: 'A command outcome is unresolved. Review the refreshed state before trying again.',
    staleEpoch: 'The world was replaced. Reconnect and review the current game.',
    notAuthorized: 'Operator access is unavailable. Reconnect to sign in.',
    queueFull: 'The server is busy. Review state before trying again.',
    retryWindowFull: 'Command history is full. Wait before sending more commands.',
    requestConflict: 'This request ID was already used for a different command.',
    invalidPlacement: 'Track cannot be placed here.', insufficientFunds: 'Insufficient funds.',
    trainState: 'State', moving: 'Moving', buffer: 'At buffer', blocked: 'Blocked',
    fixture: 'Interactive fixture', notice: 'Simulated responses only. Connection to the headless game comes in stage 4. Reload resets fixture edits.',
    run: 'Run', pause: 'Pause', step: 'Single step', mapTitle: 'Reference world',
    mapPending: 'Map renderer planned for stage 5', mapSize: '64 × 64 tiles · 16 levels',
    inspector: 'Train inspector', train: 'Train', cars: 'Cars', controller: 'Controller',
    automatic: 'Automatic', manual: 'Manual', station: 'Station', stationName: 'Station name',
    draft: 'Local draft · not submitted', resetDraft: 'Reset draft', buildings: 'Building catalog',
    pier: 'Railroad pier', condo: 'Condominium', fixed: 'Fixed height', variable: 'Variable height',
    cash: 'Cash', tick: 'Tick', paused: 'Paused', scenario: 'Reference scenario v1',
    running: 'Running', rename: 'Apply name', confirmedName: 'Confirmed name', position: 'Position',
    behavior: 'Fixture response', normal: 'Normal', delayed: 'Delayed (900 ms)', rejected: 'Reject command',
    reconnect: 'Reconnect', loading: 'Connecting…', ready: 'Connected to game', disconnected: 'Disconnected',
    idle: 'Ready', waiting: 'Waiting for server confirmation…', accepted: 'Confirmed by server',
    invalidInput: 'The command was rejected. Your draft is retained.',
    staleRevision: 'The item changed. Review the confirmed state and retry.', unavailable: 'Connection unavailable. Reconnect and review state.',
    catalogHint: '40 synthetic variants · select to inspect; construction comes later.',
    selectedBuilding: 'Building details', variant: 'Variant', height: 'Height',
  },
  ja: {
    title: 'FreeTrain', subtitle: 'ウェブ試作版', language: '言語',
    network: '参照ゲームに接続', networkNotice: 'ゲームサーバーで操作を確定します。マップ描画は工程5で実装予定です。',
    accessRequired: '操作キーが必要です', incompatible: 'クライアントとサーバーのバージョンが異なります。更新後に再読み込みしてください。',
    operatorSecret: '操作キー', connect: '接続', accessFailed: '接続できませんでした。キーを確認してください。',
    unresolved: 'コマンドの結果を確認できません。再試行前に最新状態を確認してください。',
    staleEpoch: 'ワールドが切り替わりました。再接続して現在のゲームを確認してください。',
    notAuthorized: '操作権限がありません。再接続して認証してください。',
    queueFull: 'サーバーが混雑しています。状態を確認して再試行してください。',
    retryWindowFull: 'コマンド履歴がいっぱいです。しばらく待ってから操作してください。',
    requestConflict: 'このリクエストIDは別のコマンドで使用済みです。',
    invalidPlacement: 'ここには線路を敷設できません。', insufficientFunds: '資金が不足しています。',
    trainState: '状態', moving: '走行中', buffer: '車止め', blocked: '進行不可',
    fixture: '操作サンプル', notice: '応答は模擬です。ゲームへの接続は工程4で実装します。再読み込みでサンプルを初期化します。',
    run: '運転', pause: '一時停止', step: '1ステップ', mapTitle: '参照ワールド',
    mapPending: 'マップ描画は工程5で実装予定', mapSize: '64 × 64マス · 高さ16層',
    inspector: '列車情報', train: '列車', cars: '両数', controller: '運転方式',
    automatic: '自動', manual: '手動', station: '駅', stationName: '駅名',
    draft: 'ローカルの下書き · 未送信', resetDraft: '下書きを戻す', buildings: '建物一覧',
    pier: '鉄道橋脚', condo: '集合住宅', fixed: '高さ固定', variable: '高さ可変',
    cash: '資金', tick: '経過ステップ', paused: '一時停止中', scenario: '参照シナリオ v1',
    running: '運転中', rename: '駅名を適用', confirmedName: '確定した駅名', position: '位置',
    behavior: 'サンプルの応答', normal: '通常', delayed: '遅延（900ミリ秒）', rejected: 'コマンドを拒否',
    reconnect: '再接続', loading: '接続中…', ready: 'ゲームに接続済み', disconnected: '未接続',
    idle: '準備完了', waiting: 'サーバーの確認待ち…', accepted: 'サーバーで確定しました',
    invalidInput: 'コマンドが拒否されました。下書きは保持されます。',
    staleRevision: '対象が変更されました。確定状態を確認して再試行してください。', unavailable: '接続できません。再接続して状態を確認してください。',
    catalogHint: '模擬バリエーション40件 · 選択で詳細表示。建設は後の工程で実装します。',
    selectedBuilding: '建物の詳細', variant: 'バリエーション', height: '高さ',
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
