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
IDs. The live client waits for committed results and applies only matching-base deltas.

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
arguments and disallows unmapped fields; the server additionally validates command kinds, counters and input at its boundary.
Complete save validation remains Stage 6 work. TypeScript
compile-time types alone do not validate messages from a server.

Stage 4 implements bootstrap, operator-cookie exchange and an exclusive WebSocket
with compatibility query checks. Deltas now include paused/cash values, track
upserts and track-position removals. Resync requests obtain a fresh snapshot;
heartbeats bound idle connections; input-reset messages clear held input and can
request the single-player pause policy. All lifecycle mutations are drained by
the host. Runtime browser validation uses this schema; server DTO validation and
domain checks reject malformed messages and invalid/stale commands.

The [network report](../../tmp/web-prototype-network.md) specifies endpoints,
origin/access policy, message/queue limits, two-minute bounded in-memory receipts,
resend restrictions, input expiry, disconnect grace and idle world retention.
Request IDs are unique per participant/server incarnation. Reusing an ID with a
different command is rejected; capacity exhaustion preserves existing receipts.
No exactly-once guarantee survives restart or receipt expiry.

Stage 2 snapshots now contain complete `tracks` (position, shape, revision) and
train direction, ordered `carPositions`, and state (`moving`, `station`, `buffer`,
`blocked`). Initial snapshot/join/numeric-boundary fixtures and generated types
were updated together; the headless initial snapshot equals the shared fixture.
Track order is stable by z/y/x. Runtime scheduler/held-input diagnostics are
immutable domain read data, not a save format; complete save conversion is Stage 6.
The draft save fixture still lacks full train bodies, stop state, input sequence
and next scheduler-order counter and cannot support continuation as it stands.
