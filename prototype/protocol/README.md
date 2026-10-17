# Initial protocol and save contracts

`schema.json` is the canonical draft-07 schema for initial wire messages and
save v1. `$defs` are referenced explicitly; custom formats `int64` and `uint63`
mean canonical decimal strings within signed Int64 or nonnegative Int64 bounds.
Validators must register these formats (see `web/tests/contracts.test.mjs`).
Strings forbid leading zeros, plus signs, whitespace, and negative zero. World
coordinates and counts are bounded JSON integers; money, ticks, epochs,
revisions, input order, and event order are decimal strings. Consumers compare
those using `BigInt` or `long`, never lexicographic or floating-point ordering.

Versions are independent: protocol `1`, content `prototype-content-1`, save `1`.
Join conveys server incarnation, participant/session identity, epoch, and initial
snapshot. Identity on a command will come from the trusted server context;
commands cannot choose their own participant. A join fixture is not an access
mechanism.

Commands carry request ID and expected epoch; rename/controller/removal carry
expected revision. `accepted` means committed and includes application sequence,
tick, and revision. `rejected` carries a stable code and string arguments, to be
localized at presentation. Deltas include base/new revision and explicit removal
IDs. The fixture client refuses submissions rather than simulating acknowledgements.

The initial save contains explicit tracks, train/controller/next-move data,
station/dwell data, buildings, clock, cash, random state, and ordered event
records. It excludes credentials, connections, UI, delegates, and assembly names.
It is a draft contract fixture; no current game can load it yet.

Run from the repo root:

```sh
deno task --cwd web generate:contracts
deno task --cwd web test
dotnet test FreeTrain.slnx -c Release
```

Commit generated TypeScript alongside schema changes. C# records in Contracts
and Persistence must round-trip the same fixtures without dropping/adding fields
or changing numeric values. The C# JSON reader enforces required constructor
arguments and disallows unmapped fields; domain invariants and complete
schema/enum/range validation are future server/load boundary work. TypeScript
compile-time types alone do not validate messages from a server.

Before networking, extend the schema with full track/scalar changes, input expiry,
resync/reset/version-mismatch messages, and the concrete bootstrap/save HTTP
contract. Specify dedup retention, disconnect grace, request/queue/output bounds,
and compatibility identifiers while implementing Stage 4. No retry guarantee is
implemented by the headless session API.

Stage 2 snapshots now contain complete `tracks` (position, shape, revision) and
train direction, ordered `carPositions`, and state (`moving`, `station`, `buffer`,
`blocked`). Initial snapshot/join/numeric-boundary fixtures and generated types
were updated together; the headless initial snapshot equals the shared fixture.
Track order is stable by z/y/x. Runtime scheduler/held-input diagnostics are
immutable domain read data, not a save format; complete save conversion is Stage 6.
The draft save fixture still lacks full train bodies, stop state, input sequence
and next scheduler-order counter and cannot support continuation as it stands.
