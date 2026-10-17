# Content boundary

`ReferenceScenario.Create()` supplies immutable typed definitions for the 64×64
reference world: 80 rail tiles (72 loop + 8 siding), three explicit train-car
positions, the station, and the two building families. Simulation consumes these
values without referencing Content. Tests compare the initial track definitions
and snapshot against the shared fixtures to prevent independent drift.

The pier and condominium family metadata retain stable historical contribution
IDs; the train remains an authored three-car placeholder. See
`prototype/assets/PROVENANCE.md`. No legacy XML or images are loaded at runtime.
Manifest decoding/validation and browser image conversion remain Stage 5 work.
