# FreeTrain Web

The bilingual Svelte browser now drives the deterministic reference game through
real HTTP/WebSocket transport and an authoritative C# session. Run/pause/step,
controller changes and Japanese station rename use committed server state.
Reconnect/refresh recover the current game. Map rendering and construction tools
arrive in Stage 5; durable saves/checkpoints arrive in Stage 6.

See the [network slice report and protocol/limits](tmp/web-prototype-network.md),
[Stage 2 implementation](tmp/web-prototype-headless.md), and
[UI comparison decision](tmp/web-prototype-ui-comparison.md).

## Prerequisites and checks

Install the exact .NET SDK in `global.json` (10.0.402) and Deno in
`.deno-version` (2.9.7). See the official
[.NET download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and
[Deno installation guide](https://docs.deno.com/runtime/getting_started/installation/).

Frontend dependencies and tasks are defined in `web/deno.json`; `web/deno.lock`
pins transitive versions and integrity hashes.

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
`GET /health` returns network-stage status. Commands and train positions now come
from the real headless game. The map and building catalog remain placeholders.
Only one operator socket can control the game at a time.

For a remote binding, configure `FreeTrain__OperatorSecret` on the server. The
browser exchanges the access key for an HttpOnly, SameSite cookie. Keep the static
client and service behind one HTTPS origin; deployment and HTTPS/WSS verification
remain Stage 7. No secret belongs in frontend configuration. Optional explicit
origins use `FreeTrain__AllowedOrigins__0`; lifecycle and queue settings use
`FreeTrain__Network__<Property>`. See the network report for defaults and limits.

The game clears held input on disconnect, pauses after one second, and retains an
idle game in memory for five minutes before starting a fresh paused world with a
new epoch. Restart also starts a fresh game; there are no durable saves yet.
Uncertain command outcomes are shown after reconnect and are never replayed across
connections. `web/dist/` contains the Svelte static artifact; production needs no
Deno/Node frontend process.

If using the workspace-local SDK installed during the first implementation,
prepend `$PWD/.tools/dotnet` to `PATH`. `.tools/` is ignored and not required by
the source. In a restricted execution environment, set `DOTNET_CLI_HOME` to a
writable temporary directory; .NET build/test and local web servers also need
permission to create local sockets.

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
