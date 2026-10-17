# FreeTrain web deployment and recovery

Deploy one authoritative world with one operator and one save-directory writer.
The [Compose configuration](../deploy/compose.yaml) runs a Linux x64 .NET service
serving the release static client, Caddy for one HTTPS/WSS origin, and named
persistent save and TLS volumes. Production runs neither Deno nor Node.

Run all commands from the repository root. Building the image requires Docker
with Compose; local validation also needs the .NET SDK, Deno and Python versions
described in [the web README](../README.web.md#prerequisites-and-checks).

## Build and transfer

From the repository root, with Docker Desktop or Linux Docker Engine/Compose:

```sh
./scripts/build-web-release.sh
cat tmp/release/SHA256SUMS
```

The build pins base-image digests, .NET SDK 10.0.402, Deno 2.9.7 and frozen
frontend/backend dependency locks. Native build stages produce portable managed
assemblies; the final image targets `linux/amd64`. On an ARM Mac, Docker's x64
emulation must be enabled. The final runtime is .NET/ASP.NET Core 10.0.13,
unprivileged UID/GID 1654, HTTP port 8080, static root `/app/wwwroot`, saves
`/data/saves`. The image contains asset notices and the GPL license copy.

Outputs under `tmp/release/`:

| Artifact | Use |
| --- | --- |
| `freetrain-linux-x64.tar.gz` | `docker load` image archive, tagged `freetrain:prototype` |
| `freetrain-static.tar.gz` | Exact `/app/wwwroot` from that same image; no frontend runtime needed |
| `image.json`, `SHA256SUMS` | Image metadata and transfer checksums |
| `static/` | Extracted matching static files for inspection |

The SDK/runtime/frontend image digests are fixed in
[`deploy/Dockerfile`](../deploy/Dockerfile); the proxy digest is fixed in Compose.
Debian's build-only Python installation uses the configured package repositories;
byte-identical image rebuilds are not
claimed. Rebuild deliberately when updating security patches. Keep the prior
image and save backup for rollback. The archives are local artifacts, not a
published registry release.

On another host, copy the archive/checksums and the `deploy/` directory, verify
the hashes in their archive directory, then load:

```sh
sha256sum -c SHA256SUMS
gzip -dc freetrain-linux-x64.tar.gz | docker load
```

On macOS, use `shasum -a 256 -c SHA256SUMS` for verification.

Use a release-specific tag or registry digest for a hosted rollout rather than
overwriting the only retained image. Set `FREETRAIN_IMAGE` in `deploy/.env`.

## Local HTTPS/WSS

```sh
cp deploy/env.example deploy/.env
```

Replace the example key with a long random key, e.g. output from
`openssl rand -hex 32`. Keep `deploy/.env` private (it is ignored). Run:

```sh
docker compose --env-file deploy/.env -p freetrain -f deploy/compose.yaml up -d --no-build
docker compose --env-file deploy/.env -p freetrain -f deploy/compose.yaml logs --tail 50
```

Open `https://localhost:8443`, enter the operator key, and play. Local HTTP port
8080 redirects to that HTTPS port. Caddy uses a private CA for this local setup;
the browser must trust its root certificate to avoid a trust prompt. Export the
public certificate if needed (do not export the CA private key):

```sh
mkdir -p tmp
docker cp freetrain-proxy-1:/data/caddy/pki/authorities/local/root.crt tmp/freetrain-local-root.crt
curl --cacert tmp/freetrain-local-root.crt https://localhost:8443/health
```

The automated deployment check verifies CA trust and the `localhost` hostname
with an explicit Deno trust store. Its Chromium context bypasses browser trust
for this disposable private CA; this is not public certificate validation.

Compose reserves `172.30.70.0/28`; backend `.2`, proxy `.3`. If it conflicts with
another local network, update the subnet, both addresses and
`FreeTrain__TrustedProxy` together. Only one stack using that subnet can run at
a time. The game has no host-published port. Forwarded scheme/client headers
are accepted only from the configured adjacent proxy; host/origin checks and
secure cookies operate on the HTTPS origin. Keep the internal backend port
inaccessible to public clients.

## Controlled public Linux host

On a single persistent Linux x64 machine, install Docker/Compose, transfer/load
the image and deploy configuration, point the intended DNS hostname at the
machine, and set these values in `deploy/.env`:

```dotenv
FREETRAIN_SITE=game.example.org
FREETRAIN_TLS=operator@example.org
FREETRAIN_BIND=0.0.0.0
FREETRAIN_HTTP_PORT=80
FREETRAIN_HTTPS_PORT=443
```

Replace the example hostname/email/key with actual values. Allow inbound TCP
80/443 for HTTP redirect, ACME and HTTPS/WSS. Caddy uses the email form of `tls`
for a publicly issued certificate; preserve its volumes through deployment.
Verify public DNS, certificate issuance, browser trust, firewall access,
persistent storage, HTTPS/WSS from an external client, host restart and an
independent backup restore on the target host. Local emulation checks cover
the container workflow; record host-specific validation separately.

## Configuration and readiness

| Setting | Meaning |
| --- | --- |
| `FREETRAIN_OPERATOR_SECRET` | Compose injects server-only operator key; never put it in a bundle or URL |
| `FREETRAIN_IMAGE` | Matched server/static image; default `freetrain:prototype` |
| `FREETRAIN_SAVE_VOLUME` | Optional existing/new named save volume; default `<project>_saves` |
| `FREETRAIN_CHECKPOINT_INTERVAL` | Default `00:00:30`; supported range one second to five minutes |
| `FREETRAIN_SITE`, `FREETRAIN_TLS` | Hostname and private-CA `internal` or public-certificate email |
| `FREETRAIN_BIND`, `FREETRAIN_HTTP_PORT`, `FREETRAIN_HTTPS_PORT` | Host binding and public ports |
| `FreeTrain__Network__<Property>` | Optional backend lifecycle/queue settings defined in [NetworkOptions](../src/FreeTrain.Server/NetworkSession.cs) |

`GET /health` is unauthenticated: 200 with `status: ok`, stage and protocol
version after the service has initialized its checkpoint; 503 with
`status: degraded` and `checkpointError: checkpointFailed` after an automatic
write failure. A corrupt checkpoint or occupied save-directory lease prevents
startup/listening and is left intact. The proxy can return 502 while the game
starts; readiness means the proxied health endpoint returns 200. Compose starts
the dependencies but does not itself gate traffic on a healthcheck; monitor the
endpoint and logs externally. A healthy process is not a substitute for a
recent external backup.

The game is limited to 2 CPUs/512 MiB; proxy 128 MiB. These are prototype caps,
not capacity guarantees. The game root filesystem is read-only, `/tmp` is
temporary, and the save directory is the writable persistent mount. Only one
process may own a save directory. Never run replicas against that directory.

Protocol 1, content `prototype-content-1`, and save version 1 are independent
compatibility identifiers. Update the service and its static client together;
incompatible bootstraps/sockets return 426. HTML and assets without content
hashes revalidate; hashed Vite JS/CSS can be immutable. No service worker caches
an obsolete client. Reload after an update. Restart invalidates credentials and
command receipts; sign in again and review state before retrying an uncertain
operation. Camera/layout preferences remain browser-local.

## Back up and stop/redeploy

Prefer the browser's **Download save** for an independent, consistent JSON
backup while playing. It captures through the host queue, without credentials.
Copy it outside this repository/container/host and retain dated backups. An
authenticated CLI can exchange the key with `/api/access`, retain its cookie,
and GET `/api/saves/download`; protect cookie/key files and avoid putting the
key in command history or access logs.

To back up checkpoint plus all manual slots, stop the sole writer cleanly,
then archive the named volume using the release image:

```sh
docker compose --env-file deploy/.env -p freetrain -f deploy/compose.yaml stop game
mkdir -p tmp/backups
docker run --rm --user 0 --entrypoint sh \
  -v freetrain_saves:/saves:ro \
  --mount "type=bind,src=$PWD/tmp/backups,dst=/backup" \
  freetrain:prototype -c 'tar -czf /backup/freetrain-volume.tar.gz -C /saves .'
```

Replace `freetrain_saves` if using a custom project/volume. Copy the archive to
independent storage, verify it, and start the game again or load the new image
and run Compose `up -d --no-build`. Keep the same save volume and allow the
20-second shutdown grace. A normal stop pauses and writes a final checkpoint,
including when an operator WebSocket remains open. Startup restores paused,
with a fresh server incarnation/epoch; credentials must be exchanged again.

Automatic checkpoints run every 30 seconds by default, after load/disconnect,
and at graceful shutdown. An abrupt crash can lose all changes since the last
successful checkpoint; repeated disk-write failures can make that window
longer. HTTP/WS acknowledgement means committed in memory, not crash-durable.
Do not use `docker compose down --volumes` for a real deployment.

## Restore and rollback

Stop the writer before touching its files. Preserve a corrupt checkpoint for
diagnosis; never overwrite the only backup. Restore into a fresh volume first:

```sh
docker volume create freetrain_restored
docker run --rm --user 0 --entrypoint sh \
  -v freetrain_restored:/data/saves \
  --mount "type=bind,src=$PWD/tmp/backups/backup.json,dst=/backup.json,readonly" \
  freetrain:prototype -c 'cp /backup.json /data/saves/checkpoint.json && chown -R 1654:1654 /data/saves'
```

Here `backup.json` is an independent downloaded Save v1 JSON file. For a full
volume archive, extract it into the empty volume instead, then set ownership.
Set `FREETRAIN_SAVE_VOLUME=freetrain_restored` in `deploy/.env`, start the
stack, require healthy startup, sign in, and confirm paused state, station name,
cash, train and manual slots before retiring the old volume. If it fails, stop
and retain both copies; inspect logs/version compatibility. A second writer
must never start on the original directory during this exercise.

For code rollback, stop the game, select the retained prior image together with
a compatible backup, then start and verify. Arbitrary backwards save migration
is not implemented; older images must not overwrite a newer unsupported save.

## Repeat validation

```sh
./scripts/check-web-prototype.sh
deno task --cwd web install:browser
FREETRAIN_TEST_SERVER_PORT=5182 FREETRAIN_TEST_WEB_PORT=4182 \
  deno task --cwd web test:browser
./scripts/build-web-release.sh
deno task --cwd web test:deployment
deno task --cwd web profile
dotnet src/FreeTrain.Server/bin/Release/net10.0/FreeTrain.Server.dll --headless-profile
```

These commands use Playwright's default browser cache. If you use a custom
cache, set `PLAYWRIGHT_BROWSERS_PATH` consistently for installation and checks.
`test:deployment` exclusively owns the `freetrain-stage7-check` Compose
project/volumes, uses ports 8080/8443 and the
documented subnet, and removes those test resources afterward. Never attach
production data to that project. Its temporary access key is random and stays
out of saved evidence. `profile` chooses unused loopback ports and temporary save
data; the renderer stress artifact is built only into `tmp/web-profile`, not the
shipping `web/dist`. Deployment/profiling evidence goes in `tmp/`; browser
regression captures go in `web/test-results/`.

The browser commands above exercise Chromium. Validate other supported browser
and OS combinations separately before claiming coverage for those targets.
