import { defineConfig, loadEnv } from 'vite';
import { resolve } from 'node:path';
import { serveEmbedDist } from './vite-plugin-embed.js';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, resolve(__dirname, '../..'), '');
  return {
    root: resolve(__dirname),
    envDir: resolve(__dirname, '../..'),
    plugins: [serveEmbedDist()],
    server: {
      host: true,
      port: 5173,
      strictPort: true,
      fs: {
        allow: [resolve(__dirname, '../..')],
      },
    },
    define: {
      __API_BASE__: JSON.stringify(env.VITE_API_BASE ?? 'http://localhost:8080'),
      __EMBED_CDN__: JSON.stringify(env.VITE_EMBED_CDN ?? ''),
      __EMBED_VERSION__: JSON.stringify('1.0.0'),
    },
  };
});
