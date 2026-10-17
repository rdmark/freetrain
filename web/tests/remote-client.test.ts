import { applyDelta, parseMessage } from '../src/lib/protocol.ts';
import fixture from '../../prototype/protocol/fixtures/snapshot.json' with { type: 'json' };
import removal from '../../prototype/protocol/fixtures/delta-removal.json' with { type: 'json' };
import type { Delta, Snapshot } from '../src/generated/contracts.ts';
function assert(condition: unknown): asserts condition { if (!condition) throw new Error('Assertion failed'); }
Deno.test('delta applies complete scalar/entity/track changes immutably with explicit removals', () => {
  const state = structuredClone(fixture) as Snapshot;
  const changed = { ...removal, cash: '9007199254740993', paused: false,
    tracks: [{ position: { x: 23, y: 16, z: 1 }, revision: '1', shape: 'buffer' }],
    removed: { ...removal.removed, trackPositions: [state.tracks[0].position], trainIds: [state.trains[0].id] },
  } as Delta;
  const result = applyDelta(state, changed)!;
  assert(result.revision === '1' && result.cash === '9007199254740993' && !result.paused);
  assert(result.trains.length === 0 && result.buildings.length === state.buildings.length - 1);
  assert(!result.tracks.some(t => JSON.stringify(t.position) === JSON.stringify(state.tracks[0].position)));
  assert(result.tracks.some(t => t.position.x === 23 && t.shape === 'buffer'));
  assert(Object.isFrozen(result) && Object.isFrozen(result.tracks));
  assert(state.revision === '0' && state.trains.length === 1);
});
Deno.test('missing base, wrong epoch, and nonadvancing delta require snapshot resync', () => {
  const state = structuredClone(fixture) as Snapshot;
  for (const patch of [{ baseRevision: '99' }, { worldEpoch: '2' }, { revision: '0' }]) {
    assert(applyDelta(state, { ...removal, ...patch } as Delta) === undefined);
  }
});
Deno.test('runtime wire validation rejects numeric loss, unknown fields, and unsupported messages', () => {
  for (const value of [{ ...fixture, cash: 9007199254740992 }, { ...fixture, extra: true }, { type: 'join', protocolVersion: 2 }, { ...removal, cash: '9223372036854775808' }]) {
    let rejected = false; try { parseMessage(JSON.stringify(value)); } catch { rejected = true; }
    assert(rejected);
  }
  assert(parseMessage(JSON.stringify(fixture)).type === 'snapshot');
  assert(parseMessage(JSON.stringify(removal)).type === 'delta');
});
