<script lang="ts">
  import { messages, type Locale } from './lib/localization.ts';
  import { downloadSave, listSaveSlots, saveRequest, SaveError, type SaveFeedback, type SaveSlot } from './lib/save-transfer.ts';
  let { locale, epoch, disabled, onbusy }: { locale: Locale; epoch?: string; disabled: boolean; onbusy: (busy: boolean) => void } = $props();
  const text = $derived(messages[locale]);
  let feedback = $state<SaveFeedback>('idle');
  let busy = $state(false);
  let slots = $state<SaveSlot[]>([]);
  let slot = $state('slot1');
  let confirmLoad = $state(false);
  let upload: File | undefined = $state();
  const selected = $derived(slots.find(item => item.id === slot));
  const refresh = () => listSaveSlots().then(value => { slots = value; }).catch(() => { feedback = 'saveFailed'; });
  $effect(() => { if (epoch && !disabled) { void refresh(); } });
  async function act(action: () => Promise<void>, outcome: SaveFeedback) {
    if (busy || disabled) return;
    busy = true; onbusy(true); feedback = 'saving'; confirmLoad = false;
    try { await action(); feedback = outcome; await refresh(); }
    catch (error) { feedback = error instanceof SaveError ? error.code : 'saveFailed'; }
    finally { busy = false; onbusy(false); upload = undefined; }
  }
</script>
<section class="card save-controls" aria-label={text.saves}>
  <h2>{text.saves}</h2><p class="hint">{text.loadHint}</p>
  <div class="form-actions">
    <label>{text.saveSlot}<select bind:value={slot} disabled={busy || disabled} onchange={() => { confirmLoad = false; upload = undefined; }}>
      {#each ['slot1','slot2','slot3','checkpoint'] as id}<option value={id}>{id === 'checkpoint' ? text.checkpoint : `${text.saveSlot} ${id.slice(-1)}`}{slots.find(item => item.id === id)?.exists ? '' : ` (${text.emptySlot})`}</option>{/each}
    </select></label>
    <button disabled={busy || disabled || slot === 'checkpoint'} onclick={() => act(() => saveRequest(`${slot}/save`, epoch!), 'saved')}>{text.saveGame}</button>
    <button disabled={busy || disabled || !selected?.exists} onclick={() => { upload = undefined; confirmLoad = true; }}>{text.loadGame}</button>
    <button disabled={busy || disabled} onclick={() => act(downloadSave, 'saved')}>{text.downloadSave}</button>
    <label class="field">{text.uploadSave}<input type="file" accept=".json,application/json" disabled={busy || disabled} onchange={event => {
      upload = event.currentTarget.files?.[0]; confirmLoad = !!upload; event.currentTarget.value = '';
    }} /></label>
  </div>
  {#if confirmLoad}<div class="form-actions"><span>{text.replaceWorld}</span><button disabled={busy || disabled} onclick={() => act(() => saveRequest(upload ? 'upload' : `${slot}/load`, epoch!, upload), 'loaded')}>{text.confirmLoad}</button><button onclick={() => { confirmLoad = false; upload = undefined; }}>{text.cancel}</button></div>{/if}
  <p data-testid="save-feedback" aria-live="polite">{feedback === 'idle' ? '' : text[feedback]}</p>
</section>
