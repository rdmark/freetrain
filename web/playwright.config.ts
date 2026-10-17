import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests/browser',
  use: { baseURL: 'http://127.0.0.1:4173', viewport: { width: 1280, height: 900 } },
  workers: 1,
  webServer: [{ command: 'dotnet run --project ../src/FreeTrain.Server -c Release --no-build --no-restore --no-launch-profile', url: 'http://127.0.0.1:5080/health', reuseExistingServer: false }, { command: 'deno task preview --port 4173 --strictPort', url: 'http://127.0.0.1:4173', reuseExistingServer: false }],
});
