using System.Collections.Immutable;
using FreeTrain.Simulation;

namespace FreeTrain.Content;

// Typed, curated metadata only. Legacy XML/image importing belongs to Stage 5.
public static class ReferenceScenario
{
    public const string PierId = "{B32B765C-C74C-4BD9-84C6-E75A3E6DFC27}";
    public const string CondoId = "{C7536844-7CD0-49e0-8982-2819F5792B32}";
    public static ImmutableArray<BuildingFamily> BuildingFamilies =>
    [new(PierId, "Railroad pier", "鉄道桟橋", 1, 1), new(CondoId, "Condominium", "マンション", 2, 5)];

    public static ScenarioDefinition Create()
    {
        var tracks = ImmutableArray.CreateBuilder<TrackDefinition>();
        for (var x = 9; x < 28; x++)
        {
            tracks.Add(new(new(x, 8, 1), TrackShape.EastWest));
            tracks.Add(new(new(x, 24, 1), TrackShape.EastWest));
        }
        for (var y = 9; y < 24; y++)
        {
            tracks.Add(new(new(8, y, 1), TrackShape.NorthSouth));
            tracks.Add(new(new(28, y, 1), y == 16 ? TrackShape.JunctionE : TrackShape.NorthSouth));
        }
        tracks.Add(new(new(8, 8, 1), TrackShape.CurveSE));
        tracks.Add(new(new(28, 8, 1), TrackShape.CurveSW));
        tracks.Add(new(new(28, 24, 1), TrackShape.CurveNW));
        tracks.Add(new(new(8, 24, 1), TrackShape.CurveNE));
        for (var x = 29; x <= 36; x++) tracks.Add(new(new(x, 16, 1), x == 36 ? TrackShape.Buffer : TrackShape.EastWest));
        return new("reference-v1", "prototype-content-1", new(64, 64, 16),
            new DateTime(2000, 4, 1, 8, 0, 0, DateTimeKind.Unspecified), 477, 1_000_000, 100,
            tracks.ToImmutable(),
            [new("train-1", "Local 1", "prototype.train.three-car", [new(12, 8, 1), new(11, 8, 1), new(10, 8, 1)], Direction.East, "automatic", 1)],
            [new("station-1", "中央駅", new(20, 8, 1), 5)],
            [new("building-1", PierId, new(16, 16, 0), 1), new("building-2", CondoId, new(34, 22, 0), 3)]);
    }
}
