import { compile } from 'json-schema-to-typescript';
import { readFile, writeFile } from 'node:fs/promises';
const schema = JSON.parse(await readFile(new URL('../../prototype/protocol/schema.json', import.meta.url), 'utf8'));
const output = new URL('../src/generated/contracts.ts', import.meta.url);
const generated = await compile(schema, 'PrototypeDocument', {
  bannerComment: '/* Generated from prototype/protocol/schema.json. Run deno task generate:contracts. */',
  unreachableDefinitions: true,
});
if (process.argv.includes('--check')) {
  if (await readFile(output, 'utf8') !== generated) throw new Error('Generated contracts are stale. Run deno task generate:contracts.');
} else await writeFile(output, generated);
