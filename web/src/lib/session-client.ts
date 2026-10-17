import fixture from '../../../prototype/protocol/fixtures/snapshot.json';
import type { Accepted, Command, Rejected, Snapshot } from '../generated/contracts';

type DeepReadonly<T> = T extends object ? { readonly [K in keyof T]: DeepReadonly<T[K]> } : T;
export type ReadState = DeepReadonly<Snapshot>;
export interface SessionClient {
  readonly mode: 'fixture' | 'remote';
  subscribe(listener: (state: ReadState) => void): () => void;
  send(command: Command): Promise<Accepted | Rejected>;
  dispose(): void;
}
function freeze<T extends object>(value: T): DeepReadonly<T> {
  Object.values(value).forEach(child => { if (child && typeof child === 'object') freeze(child); });
  return Object.freeze(value) as DeepReadonly<T>;
}
// This adapter is read-only. It cannot imply a committed simulation command.
export function createFixtureClient(): SessionClient {
  const state = freeze(structuredClone(fixture) as Snapshot);
  return {
    mode: 'fixture',
    subscribe(listener) { listener(state); return () => {}; },
    async send(command) {
      return { type: 'rejected', requestId: command.requestId, code: 'notAuthorized', arguments: {} };
    },
    dispose() {},
  };
}
