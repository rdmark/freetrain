/* Generated from prototype/protocol/schema.json. Run deno task generate:contracts. */

export type PrototypeDocument = WireMessage | SaveDocument;
export type WireMessage =
  Join | Command | Accepted | Rejected | Snapshot | Delta | Resync | Heartbeat | InputReset | SoundEvent;
export type Counter = string;
export type Money = string;
export type CommandPayload =
  | {
      kind: "run";
    }
  | {
      kind: "pause";
    }
  | {
      kind: "step";
    }
  | {
      kind: "renameStation";
      stationId: string;
      expectedRevision: Counter;
      name: string;
    }
  | {
      kind: "buildTrack";
      position: Position;
      shape: "eastWest" | "northSouth" | "curveSE" | "curveSW" | "curveNE" | "curveNW" | "junctionE" | "buffer";
    }
  | {
      kind: "removeTrack";
      position: Position;
      expectedRevision: Counter;
    }
  | {
      kind: "setController";
      trainId: string;
      expectedRevision: Counter;
      controller: "automatic" | "manual";
    }
  | {
      kind: "controllerInput";
      trainId: string;
      inputSequence: Counter;
      pass: boolean;
      depart: boolean;
      reverse: boolean;
      left: boolean;
      right: boolean;
      reset: boolean;
    };

export interface Join {
  type: "join";
  protocolVersion: 1;
  contentVersion: string;
  serverIncarnation: string;
  sessionId: string;
  participantId: string;
  worldEpoch: Counter;
  snapshot: Snapshot;
}
export interface Snapshot {
  type: "snapshot";
  worldEpoch: Counter;
  revision: Counter;
  tick: Counter;
  paused: boolean;
  cash: Money;
  map: Map;
  trains: Train[];
  stations: Station[];
  buildings: Building[];
  tracks: Track[];
}
export interface Map {
  width: number;
  height: number;
  levels: number;
}
export interface Train {
  id: string;
  revision: Counter;
  name: string;
  position: Position;
  cars: number;
  controller: "automatic" | "manual";
  direction: "north" | "east" | "south" | "west";
  /**
   * @minItems 1
   */
  carPositions: [Position, ...Position[]];
  state: "moving" | "station" | "buffer" | "blocked";
}
export interface Position {
  x: number;
  y: number;
  z: number;
}
export interface Station {
  id: string;
  revision: Counter;
  name: string;
  position: Position;
}
export interface Building {
  id: string;
  definitionId: string;
  position: Position;
  height: number;
}
export interface Track {
  position: Position;
  shape: "eastWest" | "northSouth" | "curveSE" | "curveSW" | "curveNE" | "curveNW" | "junctionE" | "buffer";
  revision: Counter;
}
export interface Command {
  type: "command";
  requestId: string;
  expectedWorldEpoch: Counter;
  payload: CommandPayload;
}
export interface Accepted {
  type: "accepted";
  requestId: string;
  worldEpoch: Counter;
  sequence: Counter;
  tick: Counter;
  revision: Counter;
}
export interface Rejected {
  type: "rejected";
  requestId: string;
  code:
    | "invalidPlacement"
    | "staleEpoch"
    | "staleRevision"
    | "insufficientFunds"
    | "notAuthorized"
    | "queueFull"
    | "invalidInput"
    | "requestConflict"
    | "retryWindowFull";
  arguments: {
    [k: string]: string;
  };
}
export interface Delta {
  type: "delta";
  worldEpoch: Counter;
  baseRevision: Counter;
  revision: Counter;
  tick: Counter;
  trains: Train[];
  stations: Station[];
  buildings: Building[];
  removed: Removed;
  paused: boolean;
  cash: Money;
  tracks: Track[];
}
export interface Removed {
  trainIds: string[];
  stationIds: string[];
  buildingIds: string[];
  trackPositions: Position[];
}
export interface Resync {
  type: "resync";
}
export interface Heartbeat {
  type: "heartbeat";
}
export interface InputReset {
  type: "inputReset";
  pause: boolean;
}
export interface SoundEvent {
  type: "soundEvent";
  eventId: string;
  worldEpoch: Counter;
  kind: "stationArrived";
}
export interface SaveDocument {
  saveVersion: 1;
  contentVersion: string;
  scenarioId: string;
  world: SavedWorld;
}
export interface SavedWorld {
  tick: Counter;
  paused: boolean;
  cash: Money;
  randomState: Counter;
  tracks: SavedTrack[];
  trains: SavedTrain[];
  stations: SavedStation[];
  buildings: SavedBuilding[];
  events: ScheduledEvent[];
  map: Map;
  terrain: "flat";
  clockStart: string;
  revision: Counter;
  nextEventOrder: Counter;
}
export interface SavedTrack {
  position: Position;
  shape: "eastWest" | "northSouth" | "curveSE" | "curveSW" | "curveNE" | "curveNW" | "junctionE" | "buffer";
  revision: Counter;
}
export interface SavedTrain {
  id: string;
  definitionId: string;
  direction: "north" | "south" | "east" | "west";
  /**
   * @minItems 1
   */
  cars: [Position, ...Position[]];
  controller: "automatic" | "manual";
  nextMoveTick: Counter;
  name: string;
  revision: Counter;
  status: "moving" | "station" | "buffer" | "blocked";
  stationId: string | null;
}
export interface SavedStation {
  id: string;
  name: string;
  position: Position;
  dwellTicks: number;
  revision: Counter;
}
export interface SavedBuilding {
  id: string;
  definitionId: string;
  position: Position;
  height: number;
}
export interface ScheduledEvent {
  dueTick: Counter;
  order: Counter;
  kind: "moveTrain";
  subjectId: string;
}
