#!/usr/bin/env node
import { readFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const MAX_GZIP_BYTES = 150 * 1024;
const __dirname = dirname(fileURLToPath(import.meta.url));
const root = resolve(__dirname, '..');
const pkg = JSON.parse(readFileSync(resolve(root, 'packages/embed/package.json'), 'utf-8'));
const version = pkg.version;
const embedPath = resolve(root, 'dist/embed', `v${version}`, 'embed.js');

if (!existsSync(embedPath)) {
  console.error(`embed.js not found at ${embedPath}. Run npm run build first.`);
  process.exit(1);
}

const content = readFileSync(embedPath);
const gzipped = gzipSync(content);
const sizeKb = (gzipped.length / 1024).toFixed(1);

console.log(`Gzipped embed.js size: ${sizeKb} KB (limit: ${MAX_GZIP_BYTES / 1024} KB)`);

if (gzipped.length > MAX_GZIP_BYTES) {
  console.error('Bundle exceeds 150 KB gzipped limit.');
  process.exit(1);
}
