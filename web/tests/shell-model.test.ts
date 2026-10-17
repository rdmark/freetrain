import { createFixtureService, type ReadState, type SessionClient } from '../src/lib/session-client.ts';
import { createShellModel } from '../src/lib/shell-model.ts';
import type { Accepted, Snapshot } from '../src/generated/contracts.ts';

function assert(condition: unknown, message = 'Assertion failed'): asserts condition {
  if (!condition) throw new Error(message);
}
async function until(predicate: () => boolean) {
  const deadline = Date.now() + 2000;
  while (!predicate()) {
    if (Date.now() > deadline) throw new Error('Timed out');
    await new Promise(resolve => setTimeout(resolve, 2));
  }
}
function setup() {
  const service = createFixtureService({ normal: 2, delayed: 40 });
  const model = createShellModel(() => service.connect(), 'en', 1);
  return { service, model };
}

Deno.test('delayed confirmation leaves confirmed state unchanged and blocks duplicate submission', async () => {
  const { service, model } = setup();
  try {
    await until(() => model.state.connection === 'ready');
    service.setBehavior('delayed');
    const pending = model.send({ kind: 'step' });
    assert(model.state.pending && model.state.read?.tick === '0');
    await model.send({ kind: 'step' });
    await pending;
    assert(model.state.read?.tick === '1' && !model.state.pending);
  } finally { model.dispose(); }
});

Deno.test('rejection retains Japanese draft, confirmed name, and train controller', async () => {
  const { service, model } = setup();
  try {
    await until(() => model.state.connection === 'ready');
    service.setBehavior('rejected');
    model.setDraft('新宿駅');
    await model.rename();
    await model.controller('manual');
    assert(model.state.draft === '新宿駅' && model.state.read?.stations[0].name === '中央駅');
    assert(model.state.read?.trains[0].controller === 'automatic' && model.state.feedback === 'invalidInput');
  } finally { model.dispose(); }
});

Deno.test('incoming snapshots preserve dirty draft, IME composition and catalog selection', async () => {
  const { service, model } = setup();
  try {
    await until(() => model.state.connection === 'ready');
    model.setDraft('編集中'); model.setComposing(true); model.selectBuilding('catalog-5');
    const next = structuredClone(model.state.read) as Snapshot;
    next.stations[0].name = '別の駅'; next.stations[0].revision = '1';
    next.trains[0].name = 'Updated train'; service.publish(next);
    await model.rename();
    assert(model.state.draft === '編集中' && model.state.composing && !model.state.pending);
    assert(model.state.selectedBuilding === 'catalog-5' && model.state.read?.trains[0].name === 'Updated train');
    model.setComposing(false); model.resetDraft();
    assert(model.state.draft === '別の駅');
  } finally { model.dispose(); }
});

Deno.test('editing again during pending rename retains the newer draft', async () => {
  const { service, model } = setup();
  try {
    await until(() => model.state.connection === 'ready');
    service.setBehavior('delayed'); model.setDraft('確定する駅');
    const pending = model.rename(); model.setDraft('次の下書き'); await pending;
    assert(model.state.read?.stations[0].name === '確定する駅' && model.state.draft === '次の下書き');
  } finally { model.dispose(); }
});

Deno.test('reconnect cancels pending fixture changes and keeps exactly one subscription', async () => {
  const { service, model } = setup();
  try {
    await until(() => model.state.connection === 'ready');
    model.setDraft('保存した駅'); await model.rename();
    service.setBehavior('delayed');
    const pending = model.send({ kind: 'step' });
    model.reconnect(); await pending;
    for (let i = 0; i < 20; i++) { model.reconnect(); await until(() => model.state.connection === 'ready'); }
    assert(service.diagnostics.connections === 1 && service.diagnostics.subscriptions === 1);
    assert(model.state.read?.tick === '0' && model.state.read.stations[0].name === '保存した駅');
  } finally { model.dispose(); }
  assert(service.diagnostics.connections === 0 && service.diagnostics.subscriptions === 0);
});

Deno.test('late result from a disposed connection cannot change the new connection feedback', async () => {
  const service = createFixtureService({ normal: 1, delayed: 1 });
  let complete: (value: Accepted) => void = () => {};
  const model = createShellModel((): SessionClient => {
    const client = service.connect();
    return { ...client, send: () => new Promise(resolve => { complete = resolve; }) };
  }, 'en', 1);
  try {
    await until(() => model.state.connection === 'ready');
    const pending = model.send({ kind: 'step' }); model.reconnect();
    await until(() => model.state.connection === 'ready');
    complete({ type: 'accepted', requestId: 'old', worldEpoch: '1', sequence: '1', revision: '1', tick: '1' });
    await pending;
    assert(model.state.feedback === 'idle' && !model.state.pending && model.state.read?.tick === '0');
  } finally { model.dispose(); }
});

Deno.test('failed connection can recover and disposal cancels scheduled reconnect', async () => {
  const service = createFixtureService(); let fail = true;
  const model = createShellModel(() => { if (fail) throw new Error('offline'); return service.connect(); }, 'ja', 1);
  await until(() => model.state.connection === 'disconnected');
  assert(model.state.feedback === 'unavailable');
  fail = false; model.reconnect(); await until(() => model.state.connection === 'ready');
  model.reconnect(); model.dispose();
  await new Promise(resolve => setTimeout(resolve, 10));
  assert(service.diagnostics.connections === 0);
});

Deno.test('fixture snapshots are immutable; stale revisions reject without publishing', async () => {
  const service = createFixtureService({ normal: 1, delayed: 1 });
  const client = service.connect(); let read: ReadState | undefined; let updates = 0;
  const unsubscribe = client.subscribe(next => { read = next; updates++; });
  try {
    assert(Object.isFrozen(read) && Object.isFrozen(read!.stations[0]));
    const result = await client.send({ type: 'command', requestId: 'stale', expectedWorldEpoch: '1', payload: { kind: 'renameStation', stationId: 'station-1', expectedRevision: '42', name: '駅' } });
    assert(result.type === 'rejected' && result.code === 'staleRevision' && updates === 1);
    unsubscribe(); assert(service.diagnostics.subscriptions === 0);
  } finally { client.dispose(); }
});
