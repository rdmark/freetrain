// Behavioral extraction from FreeTrain at 1f755ca; see prototype/assets/PROVENANCE.md.
// Original: Copyright (c) 2002 Kohsuke Kawaguchi et al.; LGPL-2.1-or-later (COPYING.txt).
using System.Collections.Frozen;
using System.Collections.Immutable;

namespace FreeTrain.Simulation;

public sealed record TrackState(GridPosition Position, TrackShape Shape, long Revision);
public sealed record TrainState(string Id, string Name, string DefinitionId, long Revision,
    ImmutableArray<GridPosition> Cars, Direction Direction, string Controller, TrainStatus Status,
    string? StationId, long NextMoveTick, ManualInput Input, long InputSequence, long? InputExpiresAt);
public sealed record StationState(string Id, string Name, GridPosition Position, int DwellTicks, long Revision);
public sealed record SimulationEvent(long Tick, string Kind, string SubjectId, GridPosition Position);
public sealed record WorldState(long Tick, DateTime Time, bool Paused, long Revision, long Cash, long RandomState,
    long NextEventOrder, MapDefinition Map, ImmutableArray<TrackState> Tracks, ImmutableArray<TrainState> Trains,
    ImmutableArray<StationState> Stations, ImmutableArray<BuildingDefinition> Buildings, ImmutableArray<PendingAction> Pending);

// Created and retained privately by a SessionHost. No callbacks, renderer or network code.
public sealed class World
{
    private sealed class Train(TrainDefinition definition)
    {
        public TrainDefinition Definition { get; } = definition;
        public List<GridPosition> Cars { get; } = definition.Cars.ToList();
        public Direction Direction = definition.Direction;
        public string Controller = definition.Controller;
        public long Revision;
        public TrainStatus Status;
        public string? StationId;
        public long NextMoveTick = definition.FirstMoveTick;
        public ManualInput Input;
        public long InputSequence = -1;
        public long? InputExpiresAt;
    }
    private readonly ScenarioDefinition definition;
    private readonly Dictionary<GridPosition, TrackState> tracks;
    private readonly Dictionary<string, Train> trains;
    private readonly Dictionary<string, StationState> stations;
    private readonly FrozenDictionary<string, ITrainController> controllers;
    private readonly SimulationClock clock;
    private readonly DeterministicRandom random;
    private long cash;
    public bool Paused { get; private set; } = true;
    public long Revision { get; private set; }
    public long Tick => clock.Tick;
    public const int InputLifetimeTicks = 5;

    public World(ScenarioDefinition definition, IEnumerable<ITrainController> controllers)
    {
        this.definition = definition;
        this.controllers = controllers.ToFrozenDictionary(x => x.Id, StringComparer.Ordinal);
        clock = new(definition.ClockStart);
        random = new(definition.Seed);
        cash = definition.InitialCash;
        tracks = definition.Tracks.ToDictionary(x => x.Position, x => new TrackState(x.Position, x.Shape, 0));
        trains = definition.Trains.ToDictionary(x => x.Id, x => new Train(x), StringComparer.Ordinal);
        stations = definition.Stations.ToDictionary(x => x.Id, x => new StationState(x.Id, x.Name, x.Position, x.DwellTicks, 0), StringComparer.Ordinal);
        if (definition.Map.Width <= 0 || definition.Map.Height <= 0 || definition.Map.Levels <= 0 || cash < 0 || definition.ConstructionCost <= 0)
            throw new ArgumentException("Invalid scenario bounds/economics.", nameof(definition));
        foreach (var track in tracks.Values)
            if (!definition.Map.Contains(track.Position) || !Enum.IsDefined(track.Shape)) throw new ArgumentException("Invalid rail.");
        foreach (var station in stations.Values)
            if (station.DwellTicks <= 0 || !tracks.ContainsKey(station.Position)) throw new ArgumentException("Invalid station.");
        var occupied = new HashSet<GridPosition>();
        foreach (var train in trains.Values)
        {
            if (train.Cars.Count == 0 || !this.controllers.ContainsKey(train.Controller)) throw new ArgumentException("Invalid train/controller.");
            for (var i = 0; i < train.Cars.Count; i++)
            {
                if (!tracks.ContainsKey(train.Cars[i]) || !occupied.Add(train.Cars[i])) throw new ArgumentException("Invalid car position.");
                if (i > 0 && !Connected(train.Cars[i - 1], train.Cars[i])) throw new ArgumentException("Disconnected train body.");
            }
            clock.Schedule(train.NextMoveTick, "moveTrain", train.Definition.Id);
        }
    }

    public void SetPaused(bool value) { Paused = value; Revision++; }
    public void ClearInputs()
    {
        foreach (var train in trains.Values)
        {
            if (train.InputExpiresAt == null) continue;
            train.Input = default;
            train.InputExpiresAt = null;
            train.Revision++;
            Revision++;
        }
    }
    public string? RenameStation(string id, long expectedRevision, string name)
    {
        if (!stations.TryGetValue(id, out var station) || string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl)) return "invalidInput";
        if (station.Revision != expectedRevision) return "staleRevision";
        stations[id] = station with { Name = name, Revision = station.Revision + 1 };
        Revision++;
        return null;
    }
    public string? SetController(string id, long expectedRevision, string controller)
    {
        if (!trains.TryGetValue(id, out var train) || !controllers.ContainsKey(controller)) return "invalidInput";
        if (train.Revision != expectedRevision) return "staleRevision";
        train.Controller = controller;
        train.Input = default;
        train.InputExpiresAt = null;
        train.Revision++;
        Revision++;
        return null;
    }
    public string? SetInput(string id, long sequence, ManualInput input, bool reset)
    {
        if (!trains.TryGetValue(id, out var train) || train.Controller != "manual" || sequence < 0 || sequence <= train.InputSequence) return "invalidInput";
        train.InputSequence = sequence;
        train.Input = reset ? default : input;
        train.InputExpiresAt = reset ? null : Tick + InputLifetimeTicks;
        train.Revision++;
        Revision++;
        return null;
    }

    // Includes an existing west buffer replacement when extending the siding.
    // Both tiles validate before either changes; cost is per changed tile.
    public string? BuildTrack(GridPosition position, TrackShape shape)
    {
        if (!Enum.IsDefined(shape) || !definition.Map.Contains(position) || position.Z > 1 || Occupied(position) || tracks.ContainsKey(position)) return "invalidPlacement";
        var west = position.Move(Direction.West);
        TrackState? replace = null;
        if (shape.Ports().Contains(Direction.West) && tracks.TryGetValue(west, out var previous) && previous.Shape == TrackShape.Buffer)
        {
            if (Occupied(west)) return "invalidPlacement";
            replace = previous;
        }
        var connected = shape.Ports().Any(d =>
        {
            var neighbor = position.Move(d);
            return neighbor == west && replace != null || tracks.TryGetValue(neighbor, out var rail) && rail.Shape.Ports().Contains(d.Opposite());
        });
        if (!connected) return "invalidPlacement";
        var changedTiles = replace == null ? 1 : 2;
        if (cash / changedTiles < definition.ConstructionCost) return "insufficientFunds";
        var cost = definition.ConstructionCost * changedTiles;
        // Mutation starts here; no validation may fail beyond this point.
        Revision++;
        if (replace != null) tracks[west] = replace with { Shape = TrackShape.EastWest, Revision = Revision };
        tracks.Add(position, new(position, shape, Revision));
        cash -= cost;
        return null;
    }
    public string? RemoveTrack(GridPosition position, long expectedRevision)
    {
        if (!tracks.TryGetValue(position, out var track) || Occupied(position)) return "invalidPlacement";
        if (track.Revision != expectedRevision) return "staleRevision";
        tracks.Remove(position);
        Revision++;
        return null; // Prototype refund is zero.
    }
    private bool Occupied(GridPosition position) => trains.Values.Any(x => x.Cars.Contains(position))
        || stations.Values.Any(x => x.Position == position)
        || definition.Buildings.Any(x => x.Position.X == position.X && x.Position.Y == position.Y
            && position.Z >= x.Position.Z && position.Z < x.Position.Z + x.Height);
    private bool Connected(GridPosition from, GridPosition to) => tracks.TryGetValue(from, out var a)
        && tracks.TryGetValue(to, out var b) && a.Shape.Ports().Any(d => from.Move(d) == to && b.Shape.Ports().Contains(d.Opposite()));

    public ImmutableArray<SimulationEvent> AdvanceOneTick()
    {
        var due = clock.Advance();
        var events = ImmutableArray.CreateBuilder<SimulationEvent>();
        foreach (var train in trains.Values)
            if (train.InputExpiresAt is long expiry && Tick >= expiry)
            {
                train.Input = default;
                train.InputExpiresAt = null;
                train.Revision++;
                events.Add(new(Tick, "inputExpired", train.Definition.Id, train.Cars[0]));
            }
        foreach (var action in due)
        {
            if (action.Kind != "moveTrain") throw new InvalidOperationException("Unknown scheduled action.");
            var train = trains[action.SubjectId];
            MoveTrain(train, events);
            train.Revision++;
            clock.Schedule(train.NextMoveTick, "moveTrain", action.SubjectId);
        }
        Revision++;
        return events.ToImmutable();
    }
    private void MoveTrain(Train train, ImmutableArray<SimulationEvent>.Builder events)
    {
        var controller = controllers[train.Controller];
        train.NextMoveTick = Tick + 1;
        void Emit(string kind) => events.Add(new(Tick, kind, train.Definition.Id, train.Cars[0]));
        if (train.StationId is string stationId)
        {
            var action = controller.AtStation(train.Input, false);
            if (action == StopAction.Wait)
            {
                train.NextMoveTick = Tick + stations[stationId].DwellTicks;
                Emit("stationWait");
                return;
            }
            if (action == StopAction.Reverse)
            {
                train.Cars.Reverse();
                train.Direction = train.Cars.Count == 1 ? train.Direction.Opposite()
                    : Enum.GetValues<Direction>().Single(d => train.Cars[1].Move(d) == train.Cars[0]);
                Emit("reversed");
            }
            Emit("stationDeparted");
            train.StationId = null;
        }
        var head = train.Cars[0];
        var rail = tracks[head];
        var exits = rail.Shape.Ports().Where(d => d != train.Direction.Opposite() && Connected(head, head.Move(d))).ToImmutableArray();
        if (exits.Length == 0)
        {
            var status = rail.Shape == TrackShape.Buffer ? TrainStatus.Buffer : TrainStatus.Blocked;
            if (train.Status != status) Emit(status == TrainStatus.Buffer ? "bufferStopped" : "blocked");
            train.Status = status;
            return;
        }
        var direction = controller.AtJunction(train.Input, train.Direction, exits);
        if (!exits.Contains(direction)) throw new InvalidOperationException("Controller selected an invalid exit.");
        var next = head.Move(direction);
        if (trains.Values.Any(other => (other == train ? other.Cars.SkipLast(1) : other.Cars).Contains(next)))
        {
            train.Status = TrainStatus.Blocked;
            Emit("blocked");
            return;
        }
        if (rail.Shape == TrackShape.JunctionE) Emit(direction == train.Direction ? "junctionStraight" : "junctionTurned");
        train.Cars.Insert(0, next);
        train.Cars.RemoveAt(train.Cars.Count - 1);
        train.Direction = direction;
        train.Status = TrainStatus.Moving;
        Emit("moved");
        var station = stations.Values.FirstOrDefault(x => x.Position == next);
        if (station == null) return;
        Emit("stationArrived");
        if (controller.AtStation(train.Input, true) == StopAction.Depart) Emit("stationPassed");
        else
        {
            train.Status = TrainStatus.Station;
            train.StationId = station.Id;
            train.NextMoveTick = Tick + station.DwellTicks;
        }
    }

    public WorldState Read() => new(Tick, clock.Time, Paused, Revision, cash, random.State, clock.NextOrder, definition.Map,
        tracks.Values.OrderBy(x => x.Position.Z).ThenBy(x => x.Position.Y).ThenBy(x => x.Position.X).ToImmutableArray(),
        trains.Values.OrderBy(x => x.Definition.Id, StringComparer.Ordinal).Select(x => new TrainState(x.Definition.Id,
            x.Definition.Name, x.Definition.DefinitionId, x.Revision, x.Cars.ToImmutableArray(), x.Direction, x.Controller,
            x.Status, x.StationId, x.NextMoveTick, x.Input, x.InputSequence, x.InputExpiresAt)).ToImmutableArray(),
        stations.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToImmutableArray(), definition.Buildings, clock.Pending);
}
