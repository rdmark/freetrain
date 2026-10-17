# Session boundary

`SessionHost` privately creates and owns one `World`. Network adapters must submit
commands with a trusted `ParticipantContext`; commands do not select authority.
`Enqueue` is bounded (256 by default) and returns a task completed only after
`Pump` commits or rejects it. Unauthorized submissions cannot fill the queue.

The adapter calls `Pump(tickBudget)`; there is no background timer or wall-clock
catch-up inside this library. All callers serialize through one lock. A pump
drains up to 64 FIFO commands before each automatic tick; `Pump(0)` also drains
one prefix while paused. Arrivals beyond that prefix wait for the next boundary.
A paused single-step is an explicit command that advances one tick, and therefore
can advance time even with a zero automatic tick budget. No pump advances more
than 256 ticks, including steps. Step while running is rejected. Completions run
asynchronously, outside simulation callback execution.

Commands validate epoch, participant and relevant entity/track revisions before
mutation. Successful commands receive an application sequence; rejected commands
leave world state and revisions unchanged. Request receipt deduplication and
transport retry guarantees remain Stage 4 work. Do not automatically retry based
only on a request ID in this headless API.

`ReadState` returns immutable domain values including train bodies, held input,
clock/scheduler counters and pending value actions. `Snapshot` returns detached
wire DTO arrays, including complete track state; editing them cannot edit the
world. Events are returned per pump and are not retained in a growing log.
Per-host controller factories instantiate the explicitly registered strategies;
mutable input, random, clock, money and entity state belong to the world.

Input reset clears held keys. Simulation-tick expiry is five ticks, processed
before events at the expiry tick. Stage 4 must add monotonic wall-time expiry and
immediate disconnect/focus lifecycle reset so paused time cannot retain lost
connection input indefinitely. Persistence orchestration belongs to Stage 6.
