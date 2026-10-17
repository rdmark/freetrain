import fixture from '../../../prototype/protocol/fixtures/snapshot.json' with { type: 'json' };
import type { Accepted, Command, Rejected, Snapshot } from '../generated/contracts.ts';

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
export type FixtureBehavior = 'normal' | 'delayed' | 'rejected';

// UI fixture only: no clock, movement, network, persistence or simulation rules.
// One service survives connection replacement so reconnect recovers confirmed edits.
export function createFixtureService(delays = { normal: 180, delayed: 900 }) {
  let state = freeze(structuredClone(fixture) as Snapshot);
  let behavior: FixtureBehavior = 'normal';
  const connections = new Set<Set<(state: ReadState) => void>>();
  const publish = (next: Snapshot) => {
    state = freeze(structuredClone(next));
    for (const listeners of connections) for (const listener of listeners) listener(state);
  };
  return {
    setBehavior(next: FixtureBehavior) { behavior = next; },
    publish,
    get diagnostics() {
      return { connections: connections.size, subscriptions: [...connections].reduce((n, listeners) => n + listeners.size, 0) };
    },
    connect(): SessionClient {
      const listeners = new Set<(state: ReadState) => void>();
      const pending = new Map<ReturnType<typeof setTimeout>, () => void>();
      let disposed = false;
      connections.add(listeners);
      return {
        mode: 'fixture',
        subscribe(listener) {
          if (disposed) throw new Error('Connection disposed');
          listeners.add(listener);
          listener(state);
          return () => { listeners.delete(listener); };
        },
        send(command) {
          const reject = (code: Rejected['code']): Rejected => ({ type: 'rejected', requestId: command.requestId, code, arguments: {} });
          if (disposed) return Promise.resolve(reject('notAuthorized'));
          const outcome = behavior;
          return new Promise(resolve => {
            const timer = setTimeout(() => {
              pending.delete(timer);
              if (outcome === 'rejected') return resolve(reject('invalidInput'));
              if (command.expectedWorldEpoch !== state.worldEpoch) return resolve(reject('staleEpoch'));
              const next = structuredClone(state) as Snapshot;
              const payload = command.payload;
              switch (payload.kind) {
                case 'run': next.paused = false; break;
                case 'pause': next.paused = true; break;
                case 'step': next.tick = String(BigInt(next.tick) + 1n); break;
                case 'setController': {
                  const train = next.trains.find(t => t.id === payload.trainId);
                  if (!train) return resolve(reject('invalidInput'));
                  if (train.revision !== payload.expectedRevision) return resolve(reject('staleRevision'));
                  train.controller = payload.controller;
                  train.revision = String(BigInt(train.revision) + 1n);
                  break;
                }
                case 'renameStation': {
                  const station = next.stations.find(s => s.id === payload.stationId);
                  if (!station || !payload.name.trim() || payload.name.length > 120) return resolve(reject('invalidInput'));
                  if (station.revision !== payload.expectedRevision) return resolve(reject('staleRevision'));
                  station.name = payload.name.trim();
                  station.revision = String(BigInt(station.revision) + 1n);
                  break;
                }
                default: return resolve(reject('invalidInput'));
              }
              next.revision = String(BigInt(next.revision) + 1n);
              publish(next);
              resolve({ type: 'accepted', requestId: command.requestId, worldEpoch: next.worldEpoch, sequence: next.revision, revision: next.revision, tick: next.tick });
            }, delays[outcome === 'delayed' ? 'delayed' : 'normal']);
            pending.set(timer, () => resolve(reject('notAuthorized')));
          });
        },
        dispose() {
          disposed = true;
          listeners.clear();
          connections.delete(listeners);
          for (const [timer, cancel] of pending) { clearTimeout(timer); cancel(); }
          pending.clear();
        },
      };
    },
  };
}
