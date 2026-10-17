import assert from 'node:assert/strict';
import { createBrowserAudio } from '../src/lib/browser-audio.ts';
const event = (id: string, epoch = '1') => ({ type: 'soundEvent' as const, eventId: id, worldEpoch: epoch, kind: 'stationArrived' as const });
function fixture() {
  let plays = 0, paused = 0, fail = false, hidden = false;
  const media = { src: '', currentTime: 0, preload: '', play() { plays++; return fail ? Promise.reject(new Error('Blocked')) : Promise.resolve(); }, pause() { paused++; } };
  const audio = createBrowserAudio(() => media as unknown as HTMLAudioElement, () => hidden);
  return { audio, get plays() { return plays; }, get paused() { return paused; }, set fail(value: boolean) { fail = value; }, set hidden(value: boolean) { hidden = value; } };
}
Deno.test('audio waits for user enable and plays each current live event once', async () => {
  const f = fixture(); await f.audio.event(event('old'), '1'); assert.equal(f.plays, 0);
  assert.equal(await f.audio.enable(), true); assert.equal(f.plays, 1);
  await f.audio.event(event('old'), '1'); assert.equal(f.plays, 1);
  await f.audio.event(event('live'), '1'); await f.audio.event(event('live'), '1'); assert.equal(f.plays, 2);
  await f.audio.event(event('wrong', '2'), '1'); assert.equal(f.plays, 2);
  f.audio.dispose();
});
Deno.test('hidden, disabled and disposed audio never queues a replay backlog', async () => {
  const f = fixture(); await f.audio.enable(); f.hidden = true;
  await f.audio.event(event('hidden'), '1'); f.hidden = false; await f.audio.event(event('hidden'), '1'); assert.equal(f.plays, 1);
  f.audio.disable(); await f.audio.event(event('disabled'), '1'); await f.audio.enable();
  await f.audio.event(event('disabled'), '1'); assert.equal(f.plays, 2);
  f.audio.dispose(); await f.audio.event(event('disposed'), '1'); assert.equal(f.plays, 2); assert.equal(f.paused, 2);
});
Deno.test('playback rejection disables audio and permits a later user retry', async () => {
  const f = fixture(); f.fail = true; assert.equal(await f.audio.enable(), false); assert.equal(f.audio.enabled, false);
  f.fail = false; assert.equal(await f.audio.enable(), true);
  f.fail = true; await f.audio.event(event('fails'), '1'); assert.equal(f.audio.enabled, false);
  f.audio.dispose();
});
