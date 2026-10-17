import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
const proxy = { '/api': { target: 'http://127.0.0.1:5080', ws: true } };
export default defineConfig({ plugins: [svelte()], server: { proxy }, preview: { proxy } });
