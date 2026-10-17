import { expect, test, type Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';
const ready = (page: Page) => expect(page.getByTestId('connection')).toHaveText('Connected to game');
const bootstrap = async (page: Page) => (await (await page.request.get('/api/bootstrap?protocolVersion=1&contentVersion=prototype-content-1')).json()).snapshot;
const step = (page: Page) => page.getByRole('button', { name: 'Single step', exact: false });

test('save slot, download, upload and epoch reset restore confirmed Japanese state after refresh', async ({ page }) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto('/'); await ready(page);
  await page.getByLabel('Station name', { exact: true }).fill('音と保存の駅');
  await page.getByRole('button', { name: 'Apply name', exact: true }).click();
  await expect(page.getByTestId('confirmed-name')).toHaveText('音と保存の駅');
  const before = await bootstrap(page);
  await page.getByRole('button', { name: 'Save slot', exact: true }).click();
  await expect(page.getByTestId('save-feedback')).toHaveText('Save completed');
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download save', exact: true }).click();
  const file = await download; const bytes = await readFile((await file.path())!); const saved = JSON.parse(bytes.toString());
  expect(saved.world.tick).toBe(before.tick); expect(saved.world.stations[0].name).toBe('音と保存の駅');
  expect(saved.world.map.width).toBe(64); expect(saved.world.trains[0].cars).toHaveLength(3);
  expect(saved).not.toHaveProperty('credentials');
  await step(page).click(); await expect(page.getByTestId('tick')).not.toHaveText(Number(before.tick).toLocaleString('en'));
  await page.getByLabel('Station name', { exact: true }).fill('置換される駅');
  await page.getByRole('button', { name: 'Apply name', exact: true }).click();
  await expect(page.getByTestId('confirmed-name')).toHaveText('置換される駅');
  await page.getByRole('button', { name: 'Load slot', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm load', exact: true }).click();
  await expect(page.getByTestId('save-feedback')).toHaveText('Game restored and paused');
  await expect(page.getByTestId('confirmed-name')).toHaveText('音と保存の駅');
  const loaded = await bootstrap(page); expect(loaded.worldEpoch).not.toBe(before.worldEpoch); expect(loaded.tick).toBe(before.tick); expect(loaded.paused).toBe(true);
  await step(page).click(); await expect(page.getByRole('status')).toHaveText('Confirmed by server');
  await page.getByLabel('Upload save', { exact: true }).setInputFiles({ name: 'backup.json', mimeType: 'application/json', buffer: bytes });
  await page.getByRole('button', { name: 'Confirm load', exact: true }).click();
  await expect(page.getByTestId('save-feedback')).toHaveText('Game restored and paused');
  expect((await bootstrap(page)).tick).toBe(before.tick);
  await page.reload(); await ready(page); await expect(page.getByTestId('confirmed-name')).toHaveText('音と保存の駅');
  await page.getByRole('button', { name: 'Reconnect', exact: true }).click(); await ready(page);
  await expect(page.getByTestId('confirmed-name')).toHaveText('音と保存の駅');
  await page.screenshot({ path: 'test-results/persistence-en.png', fullPage: true });
  await page.getByLabel('Language').selectOption('ja');
  await expect(page.getByRole('heading', { name: '保存と復元' })).toBeVisible();
  await page.setViewportSize({ width: 360, height: 900 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/persistence-ja.png', fullPage: true });
  expect(errors).toEqual([]);
});

test('malformed, unsupported and oversized uploads preserve world and epoch; cancel never loads', async ({ page }) => {
  await page.goto('/'); await ready(page); const before = await bootstrap(page);
  for (const buffer of [Buffer.from('{broken'), Buffer.from('{"saveVersion":999}'), Buffer.alloc(1024 * 1024 + 1, 'x')]) {
    await page.getByLabel('Upload save', { exact: true }).setInputFiles({ name: 'bad.json', mimeType: 'application/json', buffer });
    await page.getByRole('button', { name: 'Confirm load', exact: true }).click();
    await expect(page.getByTestId('save-feedback')).toContainText(buffer.length > 1024 * 1024 ? '1 MiB' : 'current game was preserved');
    expect(await bootstrap(page)).toEqual(before);
  }
  const download = await page.request.get('/api/saves/download'); const bytes = await download.body();
  await page.getByLabel('Upload save', { exact: true }).setInputFiles({ name: 'valid.json', mimeType: 'application/json', buffer: bytes });
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(await bootstrap(page)).toEqual(before);
});

test('bundled WAV decodes and plays after a click; snapshots and reconnect never replay it', async ({ page }) => {
  let liveSounds = 0;
  page.on('websocket', socket => socket.on('framereceived', frame => { if (JSON.parse(String(frame.payload)).type === 'soundEvent') liveSounds++; }));
  await page.addInitScript(() => {
    (window as unknown as { soundPlays: number }).soundPlays = 0;
    const original = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function () {
      (window as unknown as { soundPlays: number }).soundPlays++;
      this.addEventListener('loadedmetadata', () => { (window as unknown as { wavDuration: number }).wavDuration = this.duration; }, { once: true });
      return original.call(this);
    };
  });
  await page.goto('/'); await ready(page);
  expect(await page.evaluate(() => (window as unknown as { soundPlays: number }).soundPlays)).toBe(0);
  await page.getByRole('button', { name: 'Enable sound', exact: true }).click();
  await expect(page.getByTestId('sound-feedback')).toHaveText('Station sound on');
  await expect.poll(() => page.evaluate(() => (window as unknown as { wavDuration: number }).wavDuration)).toBeGreaterThan(0);
  const count = await page.evaluate(() => (window as unknown as { soundPlays: number }).soundPlays);
  await page.getByRole('button', { name: 'Reconnect', exact: true }).click(); await ready(page);
  expect(await page.evaluate(() => (window as unknown as { soundPlays: number }).soundPlays)).toBe(count);
  const save = await (await page.request.get('/api/saves/download')).json();
  // Start at the deterministic pre-arrival frame regardless of earlier journeys.
  const fixture = JSON.parse(await readFile('../prototype/protocol/fixtures/save-reference.json', 'utf8'));
  const epoch = (await bootstrap(page)).worldEpoch;
  const uploaded = await page.request.post(`/api/saves/upload?expectedWorldEpoch=${epoch}`, { data: fixture });
  expect(uploaded.ok(), await uploaded.text()).toBe(true);
  await expect.poll(async () => (await bootstrap(page)).worldEpoch).not.toBe(epoch);
  for (let i = 0; i < 8; i++) { await step(page).click(); await expect(page.getByTestId('tick')).toHaveText(String(i + 1)); }
  await expect.poll(() => liveSounds).toBe(1);
  await expect.poll(() => page.evaluate(() => (window as unknown as { soundPlays: number }).soundPlays)).toBe(count + 1);
  await page.getByRole('button', { name: 'Reconnect', exact: true }).click(); await ready(page);
  expect(await page.evaluate(() => (window as unknown as { soundPlays: number }).soundPlays)).toBe(count + 1);
  await page.getByRole('button', { name: 'Disable sound', exact: true }).click();
  await expect(page.getByTestId('sound-feedback')).toHaveText('Sound off');
  expect(save.saveVersion).toBe(1);
});
