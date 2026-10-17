#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
mkdir -p tmp/release
docker build --platform linux/amd64 -f deploy/Dockerfile -t freetrain:prototype .
container="freetrain-artifact-$$"
trap 'docker rm "$container" >/dev/null 2>&1 || true' EXIT
docker create --platform linux/amd64 --name "$container" freetrain:prototype >/dev/null
# Generated output only: prevent obsolete hashed bundles surviving a rebuild.
rm -rf tmp/release/static
mkdir -p tmp/release/static
docker cp "$container:/app/wwwroot/." tmp/release/static/
tar -czf tmp/release/freetrain-static.tar.gz -C tmp/release/static .
docker save -o tmp/release/freetrain-linux-x64.tar freetrain:prototype
gzip -f tmp/release/freetrain-linux-x64.tar
docker image inspect freetrain:prototype > tmp/release/image.json
(cd tmp/release && shasum -a 256 freetrain-static.tar.gz freetrain-linux-x64.tar.gz > SHA256SUMS)
