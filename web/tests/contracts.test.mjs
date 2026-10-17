import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import Ajv from 'ajv';
const root = new URL('../../prototype/protocol/', import.meta.url);
const schema = JSON.parse(await readFile(new URL('schema.json', root), 'utf8'));
const ajv = new Ajv({ strict: true });
function withinInt64(value, unsigned) {
  if (!/^(0|-?[1-9][0-9]*)$/.test(value)) return false;
  const number = BigInt(value);
  return number >= (unsigned ? 0n : -9223372036854775808n) && number <= 9223372036854775807n;
}
ajv.addFormat('int64', value => withinInt64(value, false));
ajv.addFormat('uint63', value => withinInt64(value, true));
const validate = ajv.compile(schema);
for (const name of await readdir(new URL('fixtures/', root))) {
  if (!name.endsWith('.json')) continue;
  Deno.test(`shared contract: ${name}`, async () => {
    const fixture = JSON.parse(await readFile(new URL(`fixtures/${name}`, root), 'utf8'));
    assert.ok(validate(fixture), JSON.stringify(validate.errors));
    if (name === 'snapshot-numeric-boundary.json') {
      assert.equal(BigInt(fixture.revision), 9007199254740993n);
      assert.equal(BigInt(fixture.cash), -9223372036854775808n);
    }
  });
}
const snapshot = JSON.parse(await readFile(new URL('fixtures/snapshot.json', root), 'utf8'));
for (const [field, value] of [['revision', 9007199254740992], ['cash', '9223372036854775808'], ['tick', '-1'], ['revision', '01']]) {
  Deno.test(`reject invalid numeric encoding ${field}=${value}`, () => assert.equal(validate({ ...snapshot, [field]: value }), false));
}
Deno.test('reject an unsupported command kind', () => {
  assert.equal(validate({ type: 'command', requestId: 'bad', expectedWorldEpoch: '1', payload: { kind: 'arbitrary' } }), false);
});
