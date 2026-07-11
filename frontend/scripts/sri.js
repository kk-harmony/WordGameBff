#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

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
const hash = createHash('sha384').update(content).digest('base64');
const sri = `sha384-${hash}`;
const outPath = resolve(root, 'dist/embed', `v${version}`, 'sri.txt');
writeFileSync(outPath, sri + '\n');
console.log(`SRI hash written to ${outPath}: ${sri}`);
