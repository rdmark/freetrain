# Prototype source and asset provenance

Baseline: `main` at `1f755ca`. Optional reference branches: SDL `e8a7e0f`, c477
`498eebd`. These revisions exist locally. Stage 1 copied **no** historical code
or binary assets; its schemas, fixture data, shell, and CSS are newly authored.
The source was reviewed to identify the following extraction candidates.

| Candidate / stable ID | Source at `1f755ca` | Use / status |
|---|---|---|
| Clock and scheduled behavior | `core/world/Clock.cs` | Review only; UI/singleton callbacks require extraction |
| Rail/train/station rules | `core/world/rail/{Train,Station,RailRoad,JunctionRailRoad}.cs` | Review candidates for Stage 2 |
| Manual controller `{5567FE11-FC2C-4a1d-836F-722EF9DC957C}` | `plugins/org.kohsuke.freetrain.tc.manual/plugin.xml` and `src/ManualTrainController.cs` | Defaults reviewed; no native polling copied |
| Fixed railroad pier `{B32B765C-C74C-4BD9-84C6-E75A3E6DFC27}` | `plugins/org.kohsuke.freetrain.structure.ayase/plugin.xml`; `minato2.bmp` | 1×1, height 1; metadata only in fixture; author Hiro Ayase |
| Variable condo `{C7536844-7CD0-49e0-8982-2819F5792B32}` | `plugins/org.kohsuke.freetrain.structure.miha/plugin.xml`; `mansyon.bmp` | 1×1, heights 2–5; metadata only in fixture; author Miha |
| Three-car train `prototype.train.three-car` | Authored scenario identifier | Placeholder definition; choose/provenance a bundled train during extraction |

The original root `readme.txt` states FreeTrain is copyright 2002 Kohsuke
Kawaguchi et al. under LGPL 2.1 or later. See `../../COPYING.txt`. Individual
plugins may have different terms; preserve and inspect their notices before
copying. Do not copy MagicLibrary code or external A-Train music.

When extracting assets/code, add exact source revision/path, original author and
license/notice location, output path, and transformations here. Normalize case
carefully (`makushisu.bmp` in the Miha manifest differs from `Makushisu.bmp` on
disk); decode legacy text explicitly and disable external XML entity resolution.
The variable family's internal entity expansion must be bounded. c477 explicit
variants are later comparison fixtures, not assets already imported.

## Stage 2 behavioral extraction — 2026-10-17

All historical references below use baseline `1f755ca`. These are portable
behavioral rewrites with explicit scenario simplifications; native/UI code and
binary assets were not copied. The root notice is copyright 2002 Kohsuke
Kawaguchi et al., LGPL-2.1-or-later; retain `COPYING.txt` and the source-attribution
headers on behavioral ports. The current source remains available in this tree.

| Source path / notice | Output | Transformation |
|---|---|---|
| `core/world/Clock.cs`; root `readme.txt`, `COPYING.txt` | `src/FreeTrain.Simulation/SimulationClock.cs` | Replace singleton/delegate/window callbacks with ordered value actions; explicit due-tick/insertion tie-breaking, future-only scheduling |
| `core/world/rail/{Train,JunctionRailRoad,RailRoad}.cs`; root notices | `src/FreeTrain.Simulation/{World,Definitions}.cs` | Discrete connected-tile movement, follower-car positions and reversal; no surfaces/audio/voxel globals; scenario dwell, buffer stop, and construction cash rules |
| `plugins/org.kohsuke.freetrain.tc.manual/src/ManualTrainController.cs` and `plugin.xml`; root notices | `src/FreeTrain.Plugins.ManualController/ManualTrainController.cs` | Preserve default input precedence/initial and later station calls, retain contribution ID; explicit inputs and stop action replace native polling, dialogs and negative-time sentinel |
| `core/world/accounting/AccountManager.cs` and `core/world/rail/Train.cs`; root notices | `src/FreeTrain.Simulation/World.cs` | Reviewed only for spend/movement concerns; authored prototype construction debit/refund rules, no old account manager or operating-cost formula copied |
| Ayase/Miha `plugin.xml` paths listed above; plugin creator metadata | `src/FreeTrain.Content/ReferenceScenario.cs` | Curated IDs/height ranges only; author Hiro Ayase (pier), Miha (condo); no XML parser, factories, image conversion or bitmap redistribution |
| Authored `prototype/scenarios/reference-v1.json` and initial save fixture | `src/FreeTrain.Content/ReferenceScenario.cs` | Typed immutable 80-tile rail topology, three-car placeholder and entity definitions; initial snapshot/track drift checks |

The `prototype.train.three-car` definition remains newly authored. Selecting and
converting an original train asset is deferred to Stage 5. No SDL/c477 code or
assets were needed in this extraction. Park-Miller/Schrage random arithmetic is
newly implemented from the public algorithm with fixed test vectors; the scenario
retains its seed without consuming random values. Session queue/participant
ownership and diagnostic trace tools are newly authored.
