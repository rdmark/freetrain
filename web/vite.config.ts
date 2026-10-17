import { defineConfig, loadEnv } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'FREETRAIN_');
  const proxy = { '/api': { target: env.FREETRAIN_API_TARGET ?? 'http://127.0.0.1:5080', ws: true } };
  return { plugins: [svelte()], server: { proxy }, preview: { proxy } };
});
