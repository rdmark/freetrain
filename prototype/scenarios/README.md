# Deterministic reference and load scenarios

`reference-v1.json` describes a proposed modern scenario, not proven historical
parity. Its initial world is `../protocol/fixtures/save-reference.json`.
Coordinates are zero-based; x increases east, y south, z upwards. Terrain is
flat at z=0 with the prebuilt loop at z=1 and 16 available height levels. The
clock begins at 2000-04-01 08:00 game time (not a local timezone). One tick is
one game minute; seed/state starts at 477. The PRNG algorithm/state advancement
must be fixed and tested during extraction before adding random behavior.

The rectangular loop spans (8,8)–(28,24), has 72 connected tiles, and a junction
at (28,16) leads east to eight siding tiles ending in a buffer at (36,16). The
three-car train starts facing east at (12,8), with trailing cars at (11,8) and
(10,8). The station lies on the northern straight at (20,8), dwell five ticks.
Automatic operation stays on the loop. One fixed pier at (16,16,0) and one
three-layer member of the 2–5-layer condominium family at (34,22,0) supply
representative inspectors. Construction prices below are deliberately proposed
prototype rules, not imported legacy accounting.

Stage 2 behavior cases:

- Start paused with cash 1,000,000, tick/revision zero and a move event at tick 1.
  Run moves one connected tile per tick; first arrival at the station is tick 8.
  No departure before tick 13; inspect stop/resume events, positions and direction.
- Pausing retains tick/time. Single-step advances exactly one tick while paused.
  Process queued commands FIFO before each tick, then due events ordered by
  `(dueTick, order)`; commands still drain while paused. Fix tie-breaking and
  fresh event order counters in the implementation.
- Preview and cancel at (37,16,1) do not change cash. Extending the siding requires
  replacing its existing buffer with an eastWest segment and adding a new buffer
  at (37,16,1) as one validated operation (extend the draft command when ported).
  A committed simple track placement costs 100 once. Removal refund is zero.
  Out-of-map, occupied, or otherwise illegal placement is atomic and unchanged.
- Manual source: Shift on initial station call passes; otherwise wait five minutes.
  Later calls with Up depart, Down reverse, neither waits another five minutes.
  Up has precedence over Down; Left is checked before Right at a junction, with
  valid connections required. Reverse's legacy negative time sentinel must become
  explicit behavior. Reset/expiry clears all held inputs.
- Enter a construction tool while running: request pause. Leaving resumes only
  if the tool caused the pause. Enter/leave while already paused keeps it paused.
  The tool/previous-pause bookkeeping is client-local, not world save data.
- Rename station to `新中央駅`, save in either UI language and reopen in the other.
  Continue scheduled movements exactly as an uninterrupted run; keep camera and
  language out of the save.
- Run two fresh isolated hosts for 10,000 ticks and compare traces, but also assert
  legality, route/dwell events, cash, and expected intermediate positions. No
  shared scheduler/random/world/accounting services are permitted.

Owner review cases: discrete speed versus source train timing; arrival/departure
boundary conventions; support/foundation legality for raised rails; automatic
junction policy; siding buffer behavior and reversal; construction/removal
pricing. Keep accepted corrections here and in fixtures/tests together.

`load-v1.json` fixes a larger 256×256 scene with 78,332 sprite instances before
culling (including layers and effects). Its generation, camera cuts, and actual
visible counts are Stage 5/7 work. The rendering target is provisional, measured
at 1920×1080; scene specification is not benchmark evidence.
