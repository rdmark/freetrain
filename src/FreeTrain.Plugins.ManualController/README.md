# Manual controller

`ManualTrainController` ports the defaults from
`plugins/org.kohsuke.freetrain.tc.manual/src/ManualTrainController.cs` at `1f755ca`
using explicit `Simulation.ManualInput`. Original copyright/licensing and source
mapping are recorded in `prototype/assets/PROVENANCE.md` and `COPYING.txt`.

The server composition root registers the contribution explicitly with a per-host
factory. It has no native keyboard polling, UI, assembly scanning or mutable
controller singleton. The source contribution ID remains a constant on the class;
`manual` is the prototype command/controller key.

Shift passes on the initial station call. Otherwise it waits five scenario ticks;
later calls prefer Up/depart over Down/reverse, otherwise wait again. Junction
choice checks valid left before valid right, then straight/mandatory turn. The
negative legacy stop-time sentinel becomes `StopAction.Reverse`; the world
reverses the car list and moves away at that scheduled decision tick. Browser key
capture/help and wall-time disconnect expiry arrive in later stages.
