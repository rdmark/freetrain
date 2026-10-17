import { expect, test } from '@playwright/test';

test('static release shell supports both languages, local drafts, and persisted locale', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Reference world' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Run', exact: false })).toBeDisabled();
  await page.getByLabel('Language').selectOption('ja');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ja');
  await expect(page.getByRole('heading', { name: '列車情報' })).toBeVisible();
  await page.getByLabel('駅名').fill('新宿駅');
  await expect(page.getByLabel('駅名')).toHaveValue('新宿駅');
  await page.getByRole('button', { name: '下書きを戻す' }).click();
  await expect(page.getByLabel('駅名')).toHaveValue('中央駅');
  await page.reload();
  await expect(page.getByRole('heading', { name: '列車情報' })).toBeVisible();
  await page.screenshot({ path: 'test-results/shell-ja.png', fullPage: true });
  await page.getByLabel('言語').selectOption('en');
  await expect(page.getByRole('heading', { name: 'Train inspector' })).toBeVisible();
  await page.screenshot({ path: 'test-results/shell-en.png', fullPage: true });
  await page.setViewportSize({ width: 720, height: 900 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});
