using System.Collections.Immutable;
using FreeTrain.Contracts;
using FreeTrain.Simulation;

namespace FreeTrain.Session;

// Constructed by a trusted adapter, never deserialized from a command's payload.
public sealed record ParticipantContext(string ParticipantId);
public sealed record PumpResult(int AdvancedTicks, int AppliedCommands, ImmutableArray<SimulationEvent> Events);

public sealed class SessionHost
{
    private sealed record Queued(ParticipantContext Participant, CommandMessage Command, TaskCompletionSource<WireMessage> Completion);
    private readonly object gate = new();
    private readonly Queue<Queued> commands = new();
    private readonly World world;
    private readonly string ownerId;
    private readonly int queueCapacity;
    private long sequence;
    public const int MaximumTicksPerPump = 256;
    public const int MaximumCommandsPerBoundary = 64;
    public long WorldEpoch { get; } = 1;

    public SessionHost(ScenarioDefinition scenario, string ownerId, IEnumerable<Func<ITrainController>> controllerFactories, int queueCapacity = 256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        this.ownerId = ownerId;
        this.queueCapacity = queueCapacity;
        world = new(scenario, controllerFactories.Select(factory => factory()));
    }

    // Completion means committed/rejected by Pump, never merely queued.
    public Task<WireMessage> Enqueue(ParticipantContext participant, CommandMessage command)
    {
        lock (gate)
        {
            if (participant.ParticipantId != ownerId) return Task.FromResult<WireMessage>(Reject(command, "notAuthorized"));
            if (string.IsNullOrWhiteSpace(command.RequestId)) return Task.FromResult<WireMessage>(Reject(command, "invalidInput"));
            if (commands.Count >= queueCapacity) return Task.FromResult<WireMessage>(Reject(command, "queueFull"));
            var completion = new TaskCompletionSource<WireMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            commands.Enqueue(new(participant, command, completion));
            return completion.Task;
        }
    }

    // The adapter supplies elapsed tick allowance. Even Pump(0) drains a bounded
    // FIFO prefix while paused. Locking serializes all callers; no timer callback reentry.
    public PumpResult Pump(int tickBudget = 0)
    {
        if (tickBudget < 0 || tickBudget > MaximumTicksPerPump) throw new ArgumentOutOfRangeException(nameof(tickBudget));
        lock (gate)
        {
            var events = ImmutableArray.CreateBuilder<SimulationEvent>();
            var advanced = 0;
            var applied = 0;
            do
            {
                var count = Math.Min(commands.Count, MaximumCommandsPerBoundary);
                for (var i = 0; i < count && advanced < MaximumTicksPerPump; i++)
                {
                    var queued = commands.Dequeue();
                    var before = world.Tick;
                    var result = Apply(queued, events);
                    advanced += (int)(world.Tick - before); // Explicit single-step commands also consume this pump's tick bound.
                    queued.Completion.SetResult(result);
                    applied++;
                }
                if (advanced >= tickBudget || world.Paused) break;
                events.AddRange(world.AdvanceOneTick());
                advanced++;
            } while (advanced < tickBudget);
            return new(advanced, applied, events.ToImmutable());
        }
    }

    private WireMessage Apply(Queued queued, ImmutableArray<SimulationEvent>.Builder events)
    {
        var command = queued.Command;
        if (queued.Participant.ParticipantId != ownerId) return Reject(command, "notAuthorized");
        if (command.ExpectedWorldEpoch != WorldEpoch) return Reject(command, "staleEpoch");
        string? error;
        switch (command.Payload)
        {
            case RunCommand: world.SetPaused(false); error = null; break;
            case PauseCommand: world.SetPaused(true); error = null; break;
            case StepCommand:
                if (!world.Paused) { error = "invalidInput"; break; }
                events.AddRange(world.AdvanceOneTick()); error = null; break;
            case RenameStationCommand rename: error = world.RenameStation(rename.StationId, rename.ExpectedRevision, rename.Name); break;
            case BuildTrackCommand build:
                error = TryShape(build.Shape, out var shape) ? world.BuildTrack(Position(build.Position), shape) : "invalidPlacement"; break;
            case RemoveTrackCommand remove: error = world.RemoveTrack(Position(remove.Position), remove.ExpectedRevision); break;
            case SetControllerCommand controller: error = world.SetController(controller.TrainId, controller.ExpectedRevision, controller.Controller); break;
            case ControllerInputCommand input:
                error = world.SetInput(input.TrainId, input.InputSequence,
                    new(input.Pass, input.Depart, input.Reverse, input.Left, input.Right), input.Reset); break;
            default: error = "invalidInput"; break;
        }
        return error == null ? new AcceptedMessage(command.RequestId, WorldEpoch, ++sequence, world.Tick, world.Revision) : Reject(command, error);
    }
    private static bool TryShape(string value, out TrackShape shape)
    {
        foreach (var candidate in Enum.GetValues<TrackShape>())
            if (Camel(candidate.ToString()) == value) { shape = candidate; return true; }
        shape = default;
        return false;
    }
    private static RejectedMessage Reject(CommandMessage command, string code) => new(command.RequestId, code, new());
    private static GridPosition Position(PositionRead p) => new(p.X, p.Y, p.Z);
    private static PositionRead Position(GridPosition p) => new(p.X, p.Y, p.Z);
    private static string Camel(string value) => char.ToLowerInvariant(value[0]) + value[1..];
    public WorldState ReadState() { lock (gate) return world.Read(); }
    public SnapshotMessage Snapshot()
    {
        lock (gate)
        {
            var state = world.Read();
            return new(WorldEpoch, state.Revision, state.Tick, state.Paused, state.Cash,
                new(state.Map.Width, state.Map.Height, state.Map.Levels),
                state.Trains.Select(x => new TrainRead(x.Id, x.Revision, x.Name, Position(x.Cars[0]), x.Cars.Length,
                    x.Controller, Camel(x.Direction.ToString()), x.Cars.Select(Position).ToArray(), Camel(x.Status.ToString()))).ToArray(),
                state.Stations.Select(x => new StationRead(x.Id, x.Revision, x.Name, Position(x.Position))).ToArray(),
                state.Buildings.Select(x => new BuildingRead(x.Id, x.DefinitionId, Position(x.Position), x.Height)).ToArray(),
                state.Tracks.Select(x => new TrackRead(Position(x.Position), Camel(x.Shape.ToString()), x.Revision)).ToArray());
        }
    }
}
