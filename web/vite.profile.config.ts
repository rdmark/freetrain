import { defineConfig } from 'vite';
// Separate measurement artifact; the profiling page never ships in web/dist.
export default defineConfig({
  build: { outDir: '../tmp/web-profile', emptyOutDir: true, rollupOptions: { input: 'profiling/renderer.html' } },
});
