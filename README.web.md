# FreeTrain Web

The prototype now runs the deterministic reference game headlessly with an owning
session queue, rail/train/station rules, construction accounting, and explicit
manual-controller input. The bilingual browser remains a **static fixture**;
HTTP/WebSocket gameplay, map rendering, and working save/load arrive in later stages.
See [the Stage 2 implementation and evidence](doc/web-prototype-headless.md).

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

These tests run Chromium against the release static files and capture English
and Japanese screenshots in `web/test-results/`. They do not establish the final
cross-platform browser matrix or native IME behavior.

## Local development

In separate terminals:

```sh
dotnet run --project src/FreeTrain.Server --no-launch-profile
```

```sh
deno task --cwd web dev
```

Open the Vite loopback URL printed in the second terminal. The server
binds to `http://127.0.0.1:5080` by default; `GET /health` returns headless
status. The fixture client does not contact it. `web/dist/` is the static
deployment artifact; production serves it alongside the C# backend without a
JavaScript server runtime.

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
[the headless extraction notes](doc/web-prototype-headless.md). The .NET test
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
