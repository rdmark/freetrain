export type SaveFeedback = 'idle' | 'saving' | 'saved' | 'loaded' | 'invalidSave' | 'saveTooLarge' | 'missingSave' | 'saveFailed' | 'staleEpoch';
export interface SaveSlot { id: string; exists: boolean }
export class SaveError extends Error { constructor(readonly code: SaveFeedback) { super(code); } }
export async function saveRequest(path: string, epoch: string, body?: File): Promise<void> {
  if (body && body.size > 1024 * 1024) throw new SaveError('saveTooLarge');
  let response: Response;
  try { response = await fetch(`/api/saves/${path}?expectedWorldEpoch=${encodeURIComponent(epoch)}`, {
    method: 'POST', headers: body ? { 'Content-Type': 'application/json' } : undefined, body,
    signal: AbortSignal.timeout(15000), credentials: 'same-origin',
  }); } catch { throw new SaveError('saveFailed'); }
  if (!response.ok) {
    const result = await response.json().catch(() => ({}));
    const code = ['invalidSave', 'saveTooLarge', 'missingSave', 'staleEpoch'].includes(result.code) ? result.code : 'saveFailed';
    throw new SaveError(code);
  }
}
export async function downloadSave(): Promise<void> {
  const response = await fetch('/api/saves/download', { signal: AbortSignal.timeout(15000), credentials: 'same-origin' });
  if (!response.ok) throw new SaveError('saveFailed');
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a'); link.href = url; link.download = 'freetrain-save.json'; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
export async function listSaveSlots(): Promise<SaveSlot[]> {
  const response = await fetch('/api/saves', { signal: AbortSignal.timeout(5000), credentials: 'same-origin' });
  if (!response.ok) throw new SaveError('saveFailed');
  return response.json();
}
