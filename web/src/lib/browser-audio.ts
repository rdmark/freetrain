import type { SoundEvent } from '../generated/contracts.ts';

// Only live, explicit event IDs play. Snapshots, joins and reconnects carry no sound backlog.
export function createBrowserAudio(create = () => new Audio('/assets/station.wav'), hidden = () => document.hidden) {
  let audio: HTMLAudioElement | undefined;
  let enabled = false;
  let disposed = false;
  const seen = new Set<string>();
  const play = async () => {
    if (disposed) return false;
    try {
      audio ??= create(); audio.preload = 'auto'; audio.currentTime = 0;
      await audio.play(); return true;
    } catch { enabled = false; return false; }
  };
  return {
    // Called directly from the sound button's trusted user gesture; also previews the WAV.
    async enable() { enabled = true; enabled = await play(); return enabled; },
    disable() { enabled = false; audio?.pause(); },
    get enabled() { return enabled; },
    async event(event: SoundEvent, worldEpoch: string) {
      if (event.worldEpoch !== worldEpoch || seen.has(event.eventId)) return;
      seen.add(event.eventId);
      if (seen.size > 128) seen.delete(seen.values().next().value!);
      if (enabled && !hidden()) await play();
    },
    dispose() { disposed = true; enabled = false; audio?.pause(); if (audio) audio.src = ''; seen.clear(); },
  };
}
