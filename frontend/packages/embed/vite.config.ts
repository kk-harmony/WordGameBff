import { defineConfig, type Plugin } from 'vite';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const pkg = JSON.parse(
  readFileSync(resolve(__dirname, 'package.json'), 'utf-8'),
) as { version: string };

// Rollup cannot parse pure annotations on function declarations in SignalR Utils.js.
function stripSignalRPureAnnotations(): Plugin {
  return {
    name: 'strip-signalr-pure-annotations',
    transform(code, id) {
      if (!id.includes('@microsoft/signalr') || !id.endsWith('Utils.js')) {
        return null;
      }
      return {
        code: code.replace(/\/\*#__PURE__\*\//g, ''),
        map: null,
      };
    },
  };
}

export default defineConfig({
  plugins: [stripSignalRPureAnnotations()],
  resolve: {
    alias: {
      '@wordgame/sdk': resolve(__dirname, '../sdk/src/index.ts'),
      '@wordgame/ui': resolve(__dirname, '../ui/src/index.ts'),
    },
  },
  define: {
    __EMBED_VERSION__: JSON.stringify(pkg.version),
  },
  build: {
    outDir: resolve(__dirname, '../../dist/embed', `v${pkg.version}`),
    emptyOutDir: true,
    target: 'es2020',
    minify: 'esbuild',
    sourcemap: true,
    lib: {
      entry: resolve(__dirname, 'src/index.ts'),
      name: 'WordGame',
      formats: ['iife'],
      fileName: () => 'embed.js',
    },
    rollupOptions: {
      output: {
        inlineDynamicImports: true,
      },
    },
  },
  worker: {
    format: 'es',
  },
});
