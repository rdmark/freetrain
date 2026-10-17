# Session boundary

Stage 2 implements the sole world owner here: trusted participant context, ordered
commands, bounded stepping, paused command draining, immutable read models, and
persistence orchestration. Network handlers must never hold a mutable world.
No runtime session is implemented in Stage 1.
