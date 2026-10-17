<script lang="ts">
  import { onMount } from 'svelte';
  import { messages, persistLocale, type Locale } from './lib/localization.ts';
  import { initialLocale } from './lib/localization.ts';
  import { createShellModel } from './lib/shell-model.ts';
  import { authorize, createRemoteSessionClient } from './lib/remote-session-client.ts';
  import { catalog } from './lib/shell-model.ts';
  import MapView from './MapView.svelte';
  import type { Selection } from './lib/map-renderer.ts';

  const model = createShellModel(createRemoteSessionClient, initialLocale());
  const dispose = () => model.dispose();
  let secret = $state('');
  let accessFailed = $state(false);
  let view = $state(model.state);
  let mapSelection = $state<Selection>();
  const text = $derived(messages[view.locale]);
  const read = $derived(view.read);
  const mapObjectPosition = $derived(mapSelection?.kind === 'train'
    ? read?.trains.find(train => train.id === mapSelection?.id)?.position
    : mapSelection?.position);
  const disabled = $derived(view.pending || view.connection !== 'ready');
  const selected = $derived(catalog.find(item => item.id === view.selectedBuilding)!);
  $effect(() => persistLocale(view.locale));
  onMount(() => {
    const unsubscribe = model.subscribe(next => { view = next; });
    const pagehide = (event: PageTransitionEvent) => { if (!event.persisted) dispose(); };
    window.addEventListener('pagehide', pagehide);
    return () => { window.removeEventListener('pagehide', pagehide); unsubscribe(); dispose(); };
  });
</script>

<svelte:head><title>{text.title} · {text.network}</title></svelte:head>

<div class="app-shell" data-shell="svelte">
  <header>
    <div class="brand"><span class="brand-mark" aria-hidden="true">FT</span><div><h1>{text.title}</h1><p>{text.subtitle}</p></div></div>
    <label class="language">{text.language}<select value={view.locale} onchange={event => model.setLocale(event.currentTarget.value as Locale)}><option value="en">English</option><option value="ja">日本語</option></select></label>
  </header>
  <div class="fixture-notice"><strong>{text.network}</strong><span>{text.networkNotice}</span></div>
  <div class="fixture-controls">
    <button onclick={model.reconnect}>{text.reconnect}</button><span data-testid="connection">{text[view.connection]}</span>
  </div>
  {#if view.connection === 'accessRequired'}
    <form class="card" onsubmit={async event => { event.preventDefault(); accessFailed = !await authorize(secret).catch(() => false); secret = ''; if (!accessFailed) model.reconnect(); }}>
      <label class="field">{text.operatorSecret}<input type="password" autocomplete="current-password" bind:value={secret} required /></label>
      <button type="submit">{text.connect}</button>{#if accessFailed}<p role="alert">{text.accessFailed}</p>{/if}
    </form>
  {/if}
  <nav class="toolbar" aria-label={text.controller}>
    <div class="transport"><button disabled={disabled || !read?.paused} onclick={() => model.send({ kind: 'run' })}>▶ {text.run}</button><button disabled={disabled || read?.paused} onclick={() => model.send({ kind: 'pause' })}>Ⅱ {text.pause}</button><button disabled={disabled || !read?.paused} onclick={() => model.send({ kind: 'step' })}>▸| {text.step}</button></div>
    <span class="status-dot" data-testid="clock-state">{text[read?.paused === false ? 'running' : 'paused']}</span>
  </nav>
  <p class="command-feedback" role="status" aria-live="polite" aria-atomic="true">{text[view.feedback === 'idle' && view.connection === 'loading' ? 'loading' : view.feedback]}</p>
  {#if read}
    <main>
      <section class="world-panel" aria-labelledby="world-title">
        <div class="panel-heading"><h2 id="world-title">{text.mapTitle}</h2><span>{text.mapSize}</span></div>
        <MapView {model} {view} onselect={selection => {
          mapSelection = selection;
          if (selection?.kind === 'building') {
            const building = read?.buildings.find(item => item.id === selection.id);
            const entry = catalog.find(item => item.height === building?.height);
            if (entry) model.selectBuilding(entry.id);
          }
        }} />
        {#if mapSelection && mapObjectPosition}<p class="map-help" data-testid="selected-object">{text[mapSelection.kind]} · {mapSelection.id} · {mapObjectPosition.x}, {mapObjectPosition.y}, {mapObjectPosition.z}</p>{/if}
        <dl class="world-stats"><div><dt>{text.cash}</dt><dd>{new Intl.NumberFormat(view.locale).format(BigInt(read.cash))}</dd></div><div><dt>{text.tick}</dt><dd data-testid="tick">{new Intl.NumberFormat(view.locale).format(BigInt(read.tick))}</dd></div></dl>
      </section>
      <aside>
        <section class="card" aria-labelledby="train-title"><h2 id="train-title">{text.inspector}</h2><dl class="details"><div><dt>{text.train}</dt><dd>{read.trains[0].name}</dd></div><div><dt>{text.cars}</dt><dd>{read.trains[0].cars}</dd></div><div><dt>{text.position}</dt><dd data-testid="train-position">{read.trains[0].position.x}, {read.trains[0].position.y}, {read.trains[0].position.z}</dd></div><div><dt>{text.trainState}</dt><dd data-testid="train-state">{text[read.trains[0].state]}</dd></div></dl><label class="field controller-field">{text.controller}<select disabled={disabled} value={read.trains[0].controller} onchange={event => { const value = event.currentTarget.value as 'automatic' | 'manual'; event.currentTarget.value = read.trains[0].controller; void model.controller(value); }}><option value="automatic">{text.automatic}</option><option value="manual">{text.manual}</option></select></label></section>
        <section class="card" aria-labelledby="station-title"><h2 id="station-title">{text.station}</h2><dl class="details"><div><dt>{text.confirmedName}</dt><dd data-testid="confirmed-name">{read.stations[0].name}</dd></div><div><dt>{text.position}</dt><dd>{read.stations[0].position.x}, {read.stations[0].position.y}, {read.stations[0].position.z}</dd></div></dl>
          <form onsubmit={event => { event.preventDefault(); void model.rename(); }}>
            <label class="field">{text.stationName}<input value={view.draft} oninput={event => model.setDraft(event.currentTarget.value)} oncompositionstart={() => model.setComposing(true)} oncompositionend={() => model.setComposing(false)} onblur={() => model.setComposing(false)} onkeydown={event => { if (event.key === 'Enter' && (event.isComposing || event.keyCode === 229 || view.composing)) event.preventDefault(); }} maxlength="120" aria-describedby="draft-hint" /></label>
            <p class="hint" id="draft-hint">{text.draft}</p><div class="form-actions"><button type="submit" disabled={disabled || view.composing || !view.draft.trim()}>{text.rename}</button><button type="button" class="secondary" onclick={model.resetDraft}>{text.resetDraft}</button></div>
          </form>
        </section>
        <section class="card" aria-labelledby="catalog-title"><h2 id="catalog-title">{text.buildings}</h2><p class="hint">{text.catalogHint}</p><ul class="catalog" aria-label={text.buildings}>
          {#each catalog as item (item.id)}<li><button type="button" aria-pressed={selected.id === item.id} onclick={() => model.selectBuilding(item.id)}><img class="building-icon" src={item.family === 'pier' ? '/assets/pier.png' : '/assets/condo-top.png'} alt="" /><span><strong>{text[item.family]} {item.variant}</strong><small>{text[item.family === 'pier' ? 'fixed' : 'variable']} · {item.height}</small></span></button></li>{/each}
        </ul></section>
        <section class="card" aria-labelledby="building-title"><h2 id="building-title">{text.selectedBuilding}</h2><dl class="details"><div><dt>{text.variant}</dt><dd data-testid="building-name">{text[selected.family]} {selected.variant}</dd></div><div><dt>{text.height}</dt><dd>{selected.height}</dd></div></dl></section>
      </aside>
    </main>
  {/if}
</div>
