<script lang="ts">
  import { onMount } from 'svelte';
  import { initialLocale, messages, persistLocale, type Locale } from './lib/localization';
  import { createFixtureClient, type ReadState } from './lib/session-client';

  const client = createFixtureClient();
  let readState = $state<ReadState>();
  let locale = $state<Locale>(initialLocale());
  let draft = $state('');
  const text = $derived(messages[locale]);
  $effect(() => persistLocale(locale));
  onMount(() => {
    const unsubscribe = client.subscribe(next => { readState = next; draft = next.stations[0].name; });
    return () => { unsubscribe(); client.dispose(); };
  });
</script>

<svelte:head><title>{text.title} · {text.fixture}</title></svelte:head>

<div class="app-shell">
  <header>
    <div class="brand"><span class="brand-mark" aria-hidden="true">FT</span><div><h1>{text.title}</h1><p>{text.subtitle}</p></div></div>
    <label class="language">{text.language}<select bind:value={locale}><option value="en">English</option><option value="ja">日本語</option></select></label>
  </header>
  <div class="fixture-notice" role="status"><strong>{text.fixture}</strong><span>{text.notice}</span></div>
  <nav class="toolbar" aria-label={text.controller}>
    <div class="transport"><button disabled>▶ {text.run}</button><button disabled>Ⅱ {text.pause}</button><button disabled>▸| {text.step}</button></div>
    <span class="status-dot">{text.paused}</span>
  </nav>
  {#if readState}
    <main>
      <section class="world-panel" aria-labelledby="world-title">
        <div class="panel-heading"><h2 id="world-title">{text.mapTitle}</h2><span>{text.mapSize}</span></div>
        <div class="map-placeholder"><div class="map-label"><span aria-hidden="true">◇</span><h3>{text.mapPending}</h3><p>{text.scenario}</p></div></div>
        <dl class="world-stats"><div><dt>{text.cash}</dt><dd>{new Intl.NumberFormat(locale).format(BigInt(readState.cash))}</dd></div><div><dt>{text.tick}</dt><dd>{new Intl.NumberFormat(locale).format(BigInt(readState.tick))}</dd></div></dl>
      </section>
      <aside>
        <section class="card"><h2>{text.inspector}</h2><dl class="details"><div><dt>{text.train}</dt><dd>{readState.trains[0].name}</dd></div><div><dt>{text.cars}</dt><dd>{readState.trains[0].cars}</dd></div><div><dt>{text.controller}</dt><dd>{text[readState.trains[0].controller]}</dd></div></dl></section>
        <section class="card"><h2>{text.station}</h2><label class="field">{text.stationName}<input bind:value={draft} maxlength="120" /></label><p class="hint">{text.draft}</p><button class="secondary" onclick={() => draft = readState!.stations[0].name}>{text.resetDraft}</button></section>
        <section class="card"><h2>{text.buildings}</h2><ul class="catalog"><li><span class="building-icon" aria-hidden="true">▥</span><div><strong>{text.pier}</strong><small>{text.fixed} · 1</small></div></li><li><span class="building-icon" aria-hidden="true">▤</span><div><strong>{text.condo}</strong><small>{text.variable} · 2–5</small></div></li></ul></section>
      </aside>
    </main>
  {/if}
</div>
