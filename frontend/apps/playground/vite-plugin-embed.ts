import type { Plugin } from 'vite';
import { existsSync, readFileSync } from 'node:fs';
import { extname, resolve } from 'node:path';

const embedRoot = resolve(__dirname, '../../dist/embed');

export function serveEmbedDist(): Plugin {
  return {
    name: 'serve-embed-dist',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const url = req.url?.split('?')[0] ?? '';
        if (!url.startsWith('/embed/')) {
          next();
          return;
        }
        const relative = url.replace(/^\/embed\//, '');
        const filePath = resolve(embedRoot, relative);
        if (!filePath.startsWith(embedRoot) || !existsSync(filePath)) {
          next();
          return;
        }
        const ext = extname(filePath);
        const contentType =
          ext === '.js' ? 'application/javascript' :
          ext === '.map' ? 'application/json' :
          'text/plain';
        res.statusCode = 200;
        res.setHeader('Content-Type', contentType);
        res.setHeader('Cache-Control', 'no-store');
        res.end(readFileSync(filePath));
      });
    },
  };
}
