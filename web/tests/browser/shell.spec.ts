import { expect, test, type Page } from '@playwright/test';

const ready = (page: Page) => expect(page.getByTestId('connection')).toHaveText('Connected to game');
const step = (page: Page) => page.getByRole('button', { name: 'Single step', exact: false });

test('real game runs, steps, renames Japanese station and recovers on refresh', async ({ page }) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto('/'); await ready(page);
  await expect(page.getByRole('heading', { name: 'Reference world' })).toBeVisible();
  const initial = await page.getByTestId('tick').textContent();
  const initialPosition = await page.getByTestId('train-position').textContent();
  await step(page).click();
  await expect(page.getByRole('status')).toHaveText('Confirmed by server');
  await expect(page.getByTestId('tick')).not.toHaveText(initial!);
  await page.getByRole('button', { name: 'Run', exact: false }).click();
  await expect(page.getByTestId('clock-state')).toHaveText('Running');
  await expect(page.getByTestId('train-position')).not.toHaveText(initialPosition!);
  await page.getByRole('button', { name: 'Pause', exact: false }).click();
  await expect(page.getByTestId('clock-state')).toHaveText('Paused');
  await page.getByRole('combobox', { name: 'Controller', exact: true }).selectOption('manual');
  await expect(page.getByRole('combobox', { name: 'Controller', exact: true })).toHaveValue('manual');
  await page.getByLabel('Station name', { exact: true }).fill('新宿駅');
  await page.getByRole('button', { name: 'Apply name', exact: true }).click();
  await expect(page.getByTestId('confirmed-name')).toHaveText('新宿駅');
  const tick = await page.getByTestId('tick').textContent();
  await page.getByRole('button', { name: 'Reconnect', exact: true }).click();
  await ready(page); await expect(page.getByTestId('tick')).toHaveText(tick!);
  await expect(page.getByTestId('confirmed-name')).toHaveText('新宿駅');
  await page.getByLabel('Language').selectOption('ja');
  await expect(page.getByRole('heading', { name: '列車情報' })).toBeVisible();
  await page.screenshot({ path: 'test-results/network-ja.png', fullPage: true });
  await page.reload();
  await expect(page.getByTestId('connection')).toHaveText('ゲームに接続済み');
  await expect(page.getByLabel('駅名', { exact: true })).toHaveValue('新宿駅');
  await page.getByLabel('言語').selectOption('en');
  await page.screenshot({ path: 'test-results/network-en.png', fullPage: true });
  expect(errors).toEqual([]);
});

test('delayed real result preserves drafts, composition, focus and catalog scroll', async ({ page }) => {
  const timers: ReturnType<typeof setTimeout>[] = [];
  await page.routeWebSocket('**/api/session?*', route => {
    const server = route.connectToServer();
    server.onMessage(message => {
      const data = JSON.parse(String(message));
      if (data.type === 'delta' || data.type === 'accepted') timers.push(setTimeout(() => route.send(message), 700));
      else route.send(message);
    });
  });
  try {
    await page.goto('/'); await ready(page);
    const before = await page.getByTestId('tick').textContent();
    const catalog = page.getByRole('list', { name: 'Building catalog' });
    await expect(catalog.getByRole('button')).toHaveCount(5);
    await catalog.getByRole('button').last().focus(); await page.keyboard.press('Enter');
    const scroll = await catalog.evaluate(el => el.scrollTop);
    await step(page).click();
    await expect(page.getByRole('status')).toHaveText('Waiting for server confirmation…');
    await expect(page.getByTestId('tick')).toHaveText(before!);
    const draft = page.getByLabel('Station name', { exact: true }); await draft.fill('東京駅'); await draft.focus();
    await draft.evaluate((el: HTMLInputElement) => { el.setSelectionRange(1, 2); el.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true })); });
    const prevented = await draft.evaluate(el => !el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true, cancelable: true })));
    expect(prevented).toBe(true);
    await expect(page.getByTestId('tick')).not.toHaveText(before!);
    await expect(page.getByRole('status')).toHaveText('Confirmed by server');
    await expect(draft).toBeFocused(); await expect(draft).toHaveValue('東京駅');
    expect(await draft.evaluate((el: HTMLInputElement) => [el.selectionStart, el.selectionEnd])).toEqual([1, 2]);
    expect(await catalog.evaluate(el => el.scrollTop)).toBe(scroll);
    await expect(page.getByRole('button', { name: 'Apply name', exact: true })).toBeDisabled();
    await draft.dispatchEvent('compositionend');
    await page.keyboard.press('Enter'); await expect(page.getByTestId('confirmed-name')).toHaveText('東京駅');
  } finally { for (const timer of timers) clearTimeout(timer); }
});

test('lost acknowledgement retries same request once and a missing delta base resyncs', async ({ page }) => {
  let dropped = false; let resynced = false; let corrupted = false;
  const ids: string[] = [];
  await page.routeWebSocket('**/api/session?*', route => {
    const server = route.connectToServer();
    route.onMessage(message => {
      const data = JSON.parse(String(message));
      if (data.type === 'command') ids.push(data.requestId);
      if (data.type === 'resync') resynced = true;
      server.send(message);
    });
    server.onMessage(message => {
      const data = JSON.parse(String(message));
      if (data.type === 'accepted' && !dropped) { dropped = true; return; }
      if (data.type === 'delta' && !corrupted) { corrupted = true; route.send(JSON.stringify({ ...data, baseRevision: '9223372036854775807' })); return; }
      route.send(message);
    });
  });
  await page.goto('/'); await ready(page);
  const before = BigInt((await page.getByTestId('tick').textContent())!.replaceAll(',', ''));
  await step(page).click();
  await expect(page.getByRole('status')).toHaveText('Confirmed by server', { timeout: 7000 });
  expect(dropped && corrupted && resynced).toBe(true);
  expect(ids).toHaveLength(2); expect(ids[0]).toBe(ids[1]);
  await expect(page.getByTestId('tick')).toHaveText(new Intl.NumberFormat('en').format(before + 1n));
});

test('dropped connection recovers confirmed state and reports ambiguous command without replay', async ({ page }) => {
  let dropped = false; let commandCount = 0;
  await page.routeWebSocket('**/api/session?*', route => {
    const server = route.connectToServer();
    route.onMessage(message => { if (JSON.parse(String(message)).type === 'command') commandCount++; server.send(message); });
    server.onMessage(message => {
      if (JSON.parse(String(message)).type === 'accepted' && !dropped) {
        dropped = true; server.close(); route.close(); return;
      }
      route.send(message);
    });
  });
  await page.goto('/'); await ready(page);
  const before = BigInt((await page.getByTestId('tick').textContent())!.replaceAll(',', ''));
  await step(page).click();
  await expect(page.getByRole('status')).toContainText('unresolved');
  await ready(page);
  await expect(page.getByTestId('tick')).toHaveText(new Intl.NumberFormat('en').format(before + 1n));
  expect(commandCount).toBe(1);
});

test('competing browser cannot join and recovers after the owner disconnects', async ({ page, context }) => {
  await page.goto('/'); await ready(page);
  const competing = await context.newPage(); await competing.goto('/');
  await expect(competing.getByTestId('connection')).not.toHaveText('Connected to game');
  await expect(step(competing)).toBeDisabled();
  await page.close();
  await ready(competing); await expect(step(competing)).toBeEnabled();
});

test('Japanese labels fit narrow doubled-density layouts and hidden tab pauses game', async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 640, height: 450 }, deviceScaleFactor: 2, locale: 'ja-JP' });
  const page = await context.newPage(); await page.goto('/');
  await expect(page.getByTestId('connection')).toHaveText('ゲームに接続済み');
  for (const width of [720, 640, 360]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true); }
  await page.getByRole('navigation').getByRole('button', { name: '運転', exact: false }).click();
  await expect(page.getByTestId('clock-state')).toHaveText('運転中');
  await page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, value: true }); document.dispatchEvent(new Event('visibilitychange')); });
  await expect(page.getByTestId('clock-state')).toHaveText('一時停止中');
  const input = page.getByLabel('駅名', { exact: true }); await input.focus(); await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: '駅名を適用', exact: true })).toBeFocused();
  await page.screenshot({ path: 'test-results/network-ja-narrow-2x.png', fullPage: true });
  await context.close();
});

test('real stale station edit is rejected without losing the Japanese draft', async ({ page }) => {
  let stale = true;
  await page.routeWebSocket('**/api/session?*', route => {
    const server = route.connectToServer();
    route.onMessage(message => {
      const data = JSON.parse(String(message));
      if (stale && data.type === 'command' && data.payload.kind === 'renameStation') {
        stale = false; data.payload.expectedRevision = '9223372036854775807'; server.send(JSON.stringify(data));
      } else server.send(message);
    });
  });
  await page.goto('/'); await ready(page);
  const confirmed = await page.getByTestId('confirmed-name').textContent();
  const draft = page.getByLabel('Station name', { exact: true }); await draft.fill('下書きの駅');
  await page.getByRole('button', { name: 'Apply name', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('The item changed');
  await expect(page.getByTestId('confirmed-name')).toHaveText(confirmed!);
  await expect(draft).toHaveValue('下書きの駅');
  await page.getByRole('button', { name: 'Apply name', exact: true }).click();
  await expect(page.getByTestId('confirmed-name')).toHaveText('下書きの駅');
});

test('measure live running publication rate and JSON payload volume', async ({ page, browser }, testInfo) => {
  const frames: { at: number; bytes: number }[] = []; let sampling = false;
  page.on('websocket', socket => socket.on('framereceived', frame => {
    const text = String(frame.payload);
    if (sampling && JSON.parse(text).type === 'delta') frames.push({ at: performance.now(), bytes: new TextEncoder().encode(text).length });
  }));
  await page.goto('/'); await ready(page);
  await page.getByRole('button', { name: 'Run', exact: false }).click();
  await expect(page.getByTestId('clock-state')).toHaveText('Running');
  const start = performance.now(); sampling = true;
  await page.waitForTimeout(2000); // Defined measurement interval, not a synchronization delay.
  sampling = false; const durationMs = performance.now() - start;
  await page.getByRole('button', { name: 'Pause', exact: false }).click();
  await expect(page.getByTestId('clock-state')).toHaveText('Paused');
  expect(frames.length).toBeGreaterThanOrEqual(8);
  const intervals = frames.slice(1).map((frame, index) => frame.at - frames[index].at).sort((a, b) => a - b);
  const measurement = { browser: browser.version(), durationMs, deltas: frames.length,
    publicationsPerSecond: frames.length * 1000 / durationMs,
    jsonBytes: frames.reduce((n, frame) => n + frame.bytes, 0),
    jsonBytesPerSecond: frames.reduce((n, frame) => n + frame.bytes, 0) * 1000 / durationMs,
    p95IntervalMs: intervals[Math.ceil(intervals.length * 0.95) - 1],
    scope: '64x64 reference scene; loopback HTTP/WS through Vite release preview; payload bytes exclude transport overhead; not a capacity benchmark',
  };
  const body = JSON.stringify(measurement, null, 2);
  await testInfo.attach('network-publication-measurement', { body, contentType: 'application/json' });
  const { writeFile } = await import('node:fs/promises');
  const { resolve } = await import('node:path');
  await writeFile(resolve(testInfo.config.rootDir, '../../../tmp/web-prototype-network-measurements.json'), body + '\n');
});
