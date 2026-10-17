#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
deno eval --no-config 'const expected = Deno.readTextFileSync(".deno-version").trim(); if (Deno.version.deno !== expected) throw new Error(`Expected Deno ${expected}, found ${Deno.version.deno}`);'
python3 scripts/check-project-boundaries.py
dotnet restore FreeTrain.slnx --locked-mode
dotnet build FreeTrain.slnx -c Release --no-restore
dotnet test FreeTrain.slnx -c Release --no-build --no-restore
deno install --config web/deno.json --frozen
deno task --cwd web test
deno task --cwd web build
