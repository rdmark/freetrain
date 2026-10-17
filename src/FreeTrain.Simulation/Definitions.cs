// Behavioral extraction from FreeTrain at 1f755ca; see prototype/assets/PROVENANCE.md.
// Original: Copyright (c) 2002 Kohsuke Kawaguchi et al.; LGPL-2.1-or-later (COPYING.txt).
using System.Collections.Immutable;

namespace FreeTrain.Simulation;

public enum Direction { North, East, South, West }
public enum TrackShape { EastWest, NorthSouth, CurveSE, CurveSW, CurveNE, CurveNW, JunctionE, Buffer }
public enum StopAction { Wait, Depart, Reverse }
public enum TrainStatus { Moving, Station, Buffer, Blocked }

public static class RailGeometry
{
    public static Direction Opposite(this Direction d) => (Direction)(((int)d + 2) % 4);
    public static Direction Left(this Direction d) => (Direction)(((int)d + 3) % 4);
    public static Direction Right(this Direction d) => (Direction)(((int)d + 1) % 4);
    public static GridPosition Move(this GridPosition p, Direction d) => d switch
    {
        Direction.North => p with { Y = p.Y - 1 }, Direction.East => p with { X = p.X + 1 },
        Direction.South => p with { Y = p.Y + 1 }, _ => p with { X = p.X - 1 }
    };
    public static ImmutableArray<Direction> Ports(this TrackShape shape) => shape switch
    {
        TrackShape.EastWest => [Direction.East, Direction.West],
        TrackShape.NorthSouth => [Direction.North, Direction.South],
        TrackShape.CurveSE => [Direction.South, Direction.East],
        TrackShape.CurveSW => [Direction.South, Direction.West],
        TrackShape.CurveNE => [Direction.North, Direction.East],
        TrackShape.CurveNW => [Direction.North, Direction.West],
        TrackShape.JunctionE => [Direction.North, Direction.South, Direction.East],
        TrackShape.Buffer => [Direction.West],
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };
}

public sealed record MapDefinition(int Width, int Height, int Levels)
{
    public bool Contains(GridPosition p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height && p.Z >= 0 && p.Z < Levels;
}
public sealed record TrackDefinition(GridPosition Position, TrackShape Shape);
public sealed record TrainDefinition(string Id, string Name, string DefinitionId, ImmutableArray<GridPosition> Cars,
    Direction Direction, string Controller, long FirstMoveTick);
public sealed record StationDefinition(string Id, string Name, GridPosition Position, int DwellTicks);
public sealed record BuildingDefinition(string Id, string DefinitionId, GridPosition Position, int Height);
public sealed record BuildingFamily(string Id, string EnglishName, string JapaneseName, int MinimumHeight, int MaximumHeight);
public sealed record ScenarioDefinition(string Id, string ContentVersion, MapDefinition Map, DateTime ClockStart,
    long Seed, long InitialCash, long ConstructionCost, ImmutableArray<TrackDefinition> Tracks,
    ImmutableArray<TrainDefinition> Trains, ImmutableArray<StationDefinition> Stations, ImmutableArray<BuildingDefinition> Buildings);

// Strategies are stateless; every mutable input/stop counter belongs to a train in its world.
public interface ITrainController
{
    string Id { get; }
    StopAction AtStation(ManualInput input, bool firstCall);
    Direction AtJunction(ManualInput input, Direction heading, ImmutableArray<Direction> exits);
}
public sealed class AutomaticController : ITrainController
{
    public string Id => "automatic";
    public StopAction AtStation(ManualInput input, bool firstCall) => firstCall ? StopAction.Wait : StopAction.Depart;
    public Direction AtJunction(ManualInput input, Direction heading, ImmutableArray<Direction> exits)
        => exits.Contains(heading) ? heading : exits[0];
}

// Park-Miller minimal standard with Schrage arithmetic. No use of runtime-dependent Random.
public sealed class DeterministicRandom
{
    public long State { get; private set; }
    public DeterministicRandom(long seed)
    {
        if (seed <= 0 || seed >= 2147483647) throw new ArgumentOutOfRangeException(nameof(seed));
        State = seed;
    }
    public int Next()
    {
        var next = 16807 * (State % 127773) - 2836 * (State / 127773);
        State = next > 0 ? next : next + 2147483647;
        return (int)State;
    }
}
