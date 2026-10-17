using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeTrain.Simulation;

namespace FreeTrain.Persistence;

// Values only: no credentials, sockets, delegates, CLR type names or held keyboard input.
public sealed record SaveDocument(int SaveVersion, string ContentVersion, string ScenarioId, SavedWorld World);
public sealed record SavedWorld(MapDefinition Map, string Terrain, DateTime ClockStart, long Tick, bool Paused,
    long Revision, long Cash, long RandomState, long NextEventOrder, ImmutableArray<TrackState> Tracks,
    ImmutableArray<SavedTrain> Trains, ImmutableArray<StationState> Stations,
    ImmutableArray<BuildingDefinition> Buildings, ImmutableArray<PendingAction> Events);
public sealed record SavedTrain(string Id, string Name, string DefinitionId, long Revision,
    ImmutableArray<GridPosition> Cars, Direction Direction, string Controller, TrainStatus Status,
    string? StationId, long NextMoveTick);

public static class SaveCodec
{
    public const int Version = 1;
    public const int MaximumBytes = 1024 * 1024;
    public static readonly JsonSerializerOptions Json = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, MaxDepth = 32 };
        options.Converters.Add(new Int64StringConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
    public static SaveDocument Capture(ScenarioDefinition scenario, WorldState state) => new(Version,
        scenario.ContentVersion, scenario.Id, new(state.Map, "flat", scenario.ClockStart, state.Tick, state.Paused,
            state.Revision, state.Cash, state.RandomState, state.NextEventOrder, state.Tracks,
            state.Trains.Select(t => new SavedTrain(t.Id, t.Name, t.DefinitionId, t.Revision, t.Cars, t.Direction,
                t.Controller, t.Status, t.StationId, t.NextMoveTick)).ToImmutableArray(),
            state.Stations, state.Buildings, state.Pending));
    public static byte[] Encode(SaveDocument document) => JsonSerializer.SerializeToUtf8Bytes(document, Json);
    public static SaveDocument Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("saveTooLarge");
        try { return JsonSerializer.Deserialize<SaveDocument>(bytes, Json) ?? throw new InvalidDataException("invalidSave"); }
        catch (JsonException ex) { throw new InvalidDataException("invalidSave", ex); }
    }
    public static World Restore(SaveDocument save, ScenarioDefinition scenario, IEnumerable<ITrainController> controllers)
    {
        void Require(bool valid) { if (!valid) throw new InvalidDataException("invalidSave"); }
        Require(save.SaveVersion == Version && save.ContentVersion == scenario.ContentVersion && save.ScenarioId == scenario.Id);
        var s = save.World;
        Require(s != null);
        if (s == null) throw new InvalidDataException("invalidSave");
        // The prototype has one registered rules/content set, bounded flat terrain and fixed entities.
        Require(s.Map == scenario.Map && s.Terrain == "flat" && s.ClockStart == scenario.ClockStart);
        const long maximumTick = 1_000_000_000; // DateTime and tick arithmetic remain bounded.
        Require(s.Tick >= 0 && s.Tick < maximumTick && s.Revision >= 0 && s.Revision < long.MaxValue - maximumTick
            && s.Cash >= 0 && s.RandomState is > 0 and < 2147483647 && s.NextEventOrder >= 0 && s.NextEventOrder < long.MaxValue - maximumTick);
        Require(!s.Tracks.IsDefault && !s.Trains.IsDefault && !s.Stations.IsDefault && !s.Buildings.IsDefault && !s.Events.IsDefault);
        Require(s.Tracks.Length <= s.Map.Width * s.Map.Height * 2 && s.Tracks.All(t => t != null && s.Map.Contains(t.Position)
            && t.Position.Z <= 1 && !scenario.Buildings.Any(b => b.Position.X == t.Position.X && b.Position.Y == t.Position.Y
                && t.Position.Z >= b.Position.Z && t.Position.Z < b.Position.Z + b.Height) && Enum.IsDefined(t.Shape) && t.Revision >= 0 && t.Revision <= s.Revision));
        Require(s.Tracks.Select(t => t.Position).Distinct().Count() == s.Tracks.Length);
        Require(s.Trains.Length == scenario.Trains.Length && s.Trains.All(t => t != null)
            && s.Trains.Select(t => t.Id).Distinct().Count() == s.Trains.Length);
        Require(s.Stations.Length == scenario.Stations.Length && s.Stations.All(t => t != null)
            && s.Stations.Select(t => t.Id).Distinct().Count() == s.Stations.Length);
        Require(s.Buildings.SequenceEqual(scenario.Buildings));
        foreach (var station in s.Stations)
        {
            var known = scenario.Stations.FirstOrDefault(t => t.Id == station.Id);
            Require(known != null && station.Position == known.Position && station.DwellTicks == known.DwellTicks
                && !string.IsNullOrWhiteSpace(station.Name) && station.Name.Length <= 120 && !station.Name.Any(char.IsControl)
                && station.Revision >= 0 && station.Revision <= s.Revision);
        }
        foreach (var train in s.Trains)
        {
            var known = scenario.Trains.FirstOrDefault(t => t.Id == train.Id);
            Require(known != null && train.Name == known.Name && train.DefinitionId == known.DefinitionId
                && !train.Cars.IsDefault && train.Cars.Length == known.Cars.Length
                && Enum.IsDefined(train.Direction) && Enum.IsDefined(train.Status)
                && train.Revision >= 0 && train.Revision <= s.Revision
                && train.NextMoveTick > s.Tick && train.NextMoveTick <= s.Tick + 5);
            Require((train.Status == TrainStatus.Station) == (train.StationId != null));
            if (train.StationId != null) Require(s.Stations.Any(t => t.Id == train.StationId && t.Position == train.Cars[0]));
            if (train.Cars.Length > 1) Require(train.Cars[1].Move(train.Direction) == train.Cars[0]);
        }
        Require(s.Events.Length == s.Trains.Length && s.Events.All(e => e != null && e.Kind == "moveTrain"
            && e.Order >= 0 && e.Order < s.NextEventOrder
            && s.Trains.Any(t => t.Id == e.SubjectId && t.NextMoveTick == e.DueTick))
            && s.Events.Select(e => e.Order).Distinct().Count() == s.Events.Length
            && s.Events.Select(e => e.SubjectId).Distinct().Count() == s.Events.Length);
        var definition = scenario with { Seed = s.RandomState, InitialCash = s.Cash, Tracks = s.Tracks.Select(t => new TrackDefinition(t.Position, t.Shape)).ToImmutableArray(),
            Trains = s.Trains.Select(t => new TrainDefinition(t.Id, t.Name, t.DefinitionId, t.Cars, t.Direction, t.Controller, t.NextMoveTick)).ToImmutableArray(),
            Stations = s.Stations.Select(t => new StationDefinition(t.Id, t.Name, t.Position, t.DwellTicks)).ToImmutableArray() };
        try
        {
            var state = new WorldState(s.Tick, s.ClockStart.AddMinutes(s.Tick), s.Paused, s.Revision, s.Cash, s.RandomState,
                s.NextEventOrder, s.Map, s.Tracks, s.Trains.Select(t => new TrainState(t.Id, t.Name, t.DefinitionId,
                    t.Revision, t.Cars, t.Direction, t.Controller, t.Status, t.StationId, t.NextMoveTick, default,
                    -1, null)).ToImmutableArray(), s.Stations, s.Buildings, s.Events);
            return World.Restore(definition, controllers, state);
        }
        catch (ArgumentException ex) { throw new InvalidDataException("invalidSave", ex); }
    }
    private sealed class Int64StringConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected decimal string");
            var text = reader.GetString();
            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
                || text != value.ToString(CultureInfo.InvariantCulture)) throw new JsonException("Invalid Int64");
            return value;
        }
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
