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
