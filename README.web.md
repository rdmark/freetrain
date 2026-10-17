# FreeTrain Web

The bilingual Svelte browser now drives the deterministic reference game through
real HTTP/WebSocket transport and an authoritative C# session. Run/pause/step,
controller changes and Japanese station rename use committed server state.
Reconnect/refresh recover the current game. The Canvas map supports pan/zoom,
height cuts, object picking, track previews and confirmed construction/removal,
with focus-scoped manual keyboard control. Versioned saves, server checkpoints, browser
download/upload and an optional station sound are available.

See the [persistence/audio report and backup/restore instructions](tmp/web-prototype-persistence.md),
[playable map report and asset provenance](tmp/web-prototype-map.md),
[network slice report and protocol/limits](tmp/web-prototype-network.md),
[Stage 2 implementation](tmp/web-prototype-headless.md), and
[UI comparison decision](tmp/web-prototype-ui-comparison.md).

## Prerequisites and checks

Install the exact .NET SDK in `global.json` (10.0.402) and Deno in
`.deno-version` (2.9.7). See the official
[.NET download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and
[Deno installation guide](https://docs.deno.com/runtime/getting_started/installation/).

Frontend dependencies and tasks are defined in `web/deno.json`; `web/deno.lock`
pins transitive versions and integrity hashes. Python 3 (standard library only)
is also required to reproduce/check the curated legacy sprite conversion.

The solution to build is `FreeTrain.slnx`.

From the repository root:

```sh
./scripts/check-web-prototype.sh
```

This checks project boundaries, restores .NET packages in locked mode, builds
Release, runs headless tests, installs frontend packages with `deno install --frozen`, validates
shared fixtures, checks generated types and Svelte/TypeScript, and builds static
files into `web/dist/`.

For browser interaction checks after the build:

```sh
cd web
deno task install:browser
deno task test:browser
```

These tests build the Svelte shell into `web/dist/`, run
Chromium against those release static files, and capture English/Japanese
screenshots in `web/test-results/`. They do not establish the final
cross-platform browser matrix or native IME behavior.

## Local development

In separate terminals:

```sh
dotnet run --project src/FreeTrain.Server --no-launch-profile
```

```sh
deno task --cwd web dev
```

Open the Vite loopback URL printed in the second terminal. The server binds to
`http://127.0.0.1:5080`; Vite proxies `/api` HTTP and WebSocket traffic to it.
`GET /health` reports persistence-stage health and any automatic-checkpoint error. Commands and train positions now come
from the real headless game. The map renders imported pier/condo/FT train images
and the catalog contains the fixed pier and condo heights 2–5.
Only one operator socket can control the game at a time.

For a remote binding, configure `FreeTrain__OperatorSecret` on the server. The
browser exchanges the access key for an HttpOnly, SameSite cookie. Keep the static
client and service behind one HTTPS origin; deployment and HTTPS/WSS verification
remain Stage 7. No secret belongs in frontend configuration. Optional explicit
origins use `FreeTrain__AllowedOrigins__0`; lifecycle and queue settings use
`FreeTrain__Network__<Property>`. See the network report for defaults and limits.

The game clears held input on disconnect and pauses/checkpoints after one second.
Idle games retain their state. Restart restores the latest checkpoint, starts paused,
and changes the server incarnation. Automatic checkpoints run every 30 seconds;
an abrupt crash can lose changes since the most recent successful checkpoint.
Uncertain command outcomes are shown after reconnect and are never replayed across
connections. `web/dist/` contains the Svelte static artifact; production needs no
Deno/Node frontend process.

If using the workspace-local SDK installed during the first implementation,
prepend `$PWD/.tools/dotnet` to `PATH`. `.tools/` is ignored and not required by
the source. In a restricted execution environment, set `DOTNET_CLI_HOME` to a
writable temporary directory; .NET build/test and local web servers also need
permission to create local sockets.

## Playing the map

Click an object to inspect, drag the map to pan, and use the wheel or zoom buttons.
Height cut hides raised rails and replaces cut buildings with selectable footprints.
Night uses the source palette transformation; winter ground and recoloring are
local display options. The water/fence/slope sample by the pier is decorative.

Choose Build track to pause, select a tile on level 1, then confirm. Extend the
siding with a buffer at `(37,16,1)`; the host converts the old buffer and charges
200 total. Preview/cancel costs nothing. Removal has no refund and rejects occupied
or station track. Leaving a construction tool keeps the game paused.

Select Manual in the train inspector, run, and focus the map. Shift passes the
station, Up departs, Down reverses at a station, and Left/Right choose a junction.
Focus loss, hidden tabs, disconnect and server expiry clear held input. Text fields
and IME composition do not capture controller keys.

Reproduce/check bundled sprites:

```sh
deno task --cwd web import:content
deno task --cwd web check:content
```

These tasks use the Python standard library. Generated PNGs, night/recolor variants,
alpha masks, offsets and source hashes are included in source; browser image decode stays
outside the paint loop. Asset notices ship in `web/public/assets/NOTICE.txt` and
`COPYING.txt`. Build checks reject generated drift.

## Saves and sound

Use **Save slot** / **Load slot** for three server slots, or **Download save** /
**Upload save** for an independent JSON backup. Loading asks for confirmation,
validates the complete file, replaces the game under a new epoch, clears held input,
and starts paused. Invalid/incompatible files preserve the current world. Uploads
are limited to 1 MiB. This format does not import historical FreeTrain saves.

The automatic checkpoint is separate from the three manual slots. The server
writes it periodically, after disconnect/load, and on graceful shutdown. Saves
use `data/saves` relative to the service working directory by default. Set an
absolute directory for predictable local or persistent-volume storage:

```sh
FreeTrain__Saves__Directory="$PWD/data/saves" dotnet run --project src/FreeTrain.Server --no-launch-profile
```

`FreeTrain__Saves__Interval=00:00:30` configures the checkpoint interval
(allowed range: one second to five minutes). Only one server can hold a save
directory at a time. A corrupt checkpoint prevents startup and is left intact;
restore an independent download/backup using the linked report's procedure.
Health reports `checkpointError: "checkpointFailed"` if an automatic write fails.

Click **Enable sound** to preview the bundled station WAV and enable sound for
new station-arrival events. Reconnects, refreshes and loaded snapshots do not replay
old events. **Disable sound** mutes it; refresh starts muted.

After a Release build, run the real crash/restart/independent-backup check:

```sh
python3 scripts/check-save-recovery.py
```

This launches isolated local servers with temporary save directories and writes
measurements into `tmp/web-prototype-save-measurements.json`. Linux persistent
volumes, hosted HTTPS/WSS and the full browser matrix remain Stage 7.

## Headless scenario

Run the 10,000-tick reference scenario without starting an HTTP listener:

```sh
dotnet run --project src/FreeTrain.Server -c Release --no-build --no-restore -- --headless-trace 10000
```

Append an output path to write full per-tick state/event NDJSON. The command
prints counts and a SHA-256; the expected result and rule decisions are in
[the headless extraction notes](tmp/web-prototype-headless.md). The .NET test
suite compares two complete 10,000-tick traces and checks movement legality,
station timing, manual control, accounting atomicity and host isolation.

## Contract changes

Edit `prototype/protocol/schema.json`, run
`deno task --cwd web generate:contracts`, and update the C# DTOs and shared
fixtures together. See [the protocol notes](prototype/protocol/README.md) and
[scenario behavior cases](prototype/scenarios/README.md). Keep protocol, content,
and save versions independent.

To change frontend dependency versions, edit `web/deno.json`, then run:

```sh
deno install --config web/deno.json --frozen=false
```

Commit the updated `web/deno.lock` together with the dependency changes. Ordinary
installs and tasks use the frozen lockfile and fail if it is out of date.
