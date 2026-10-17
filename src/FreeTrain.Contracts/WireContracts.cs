using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeTrain.Contracts;

public static class WireJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowOutOfOrderMetadataProperties = true,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true
        };
        options.Converters.Add(new DecimalInt64Converter());
        return options;
    }
}

// JSON long values are canonical decimal strings, never JS floating-point numbers.
public sealed class DecimalInt64Converter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected decimal string.");
        var text = reader.GetString();
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || text != value.ToString(CultureInfo.InvariantCulture))
            throw new JsonException("Expected canonical Int64 decimal string.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CommandMessage), "command")]
[JsonDerivedType(typeof(SnapshotMessage), "snapshot")]
[JsonDerivedType(typeof(DeltaMessage), "delta")]
[JsonDerivedType(typeof(AcceptedMessage), "accepted")]
[JsonDerivedType(typeof(RejectedMessage), "rejected")]
[JsonDerivedType(typeof(JoinMessage), "join")]
[JsonDerivedType(typeof(ResyncMessage), "resync")]
[JsonDerivedType(typeof(HeartbeatMessage), "heartbeat")]
[JsonDerivedType(typeof(InputResetMessage), "inputReset")]
public abstract record WireMessage;

public sealed record ResyncMessage : WireMessage;
public sealed record HeartbeatMessage : WireMessage;
public sealed record InputResetMessage(bool Pause) : WireMessage;

public sealed record JoinMessage(int ProtocolVersion, string ContentVersion, string ServerIncarnation,
    string SessionId, string ParticipantId, long WorldEpoch, [property: JsonConverter(typeof(NestedSnapshotConverter))] SnapshotMessage Snapshot) : WireMessage;
public sealed record CommandMessage(string RequestId, long ExpectedWorldEpoch, CommandPayload Payload) : WireMessage;
public sealed record AcceptedMessage(string RequestId, long WorldEpoch, long Sequence, long Tick, long Revision) : WireMessage;
public sealed record RejectedMessage(string RequestId, string Code, Dictionary<string, string> Arguments) : WireMessage;
public sealed record SnapshotMessage(long WorldEpoch, long Revision, long Tick, bool Paused, long Cash,
    MapRead Map, TrainRead[] Trains, StationRead[] Stations, BuildingRead[] Buildings, TrackRead[] Tracks) : WireMessage;
public sealed record DeltaMessage(long WorldEpoch, long BaseRevision, long Revision, long Tick,
    bool Paused, long Cash, TrainRead[] Trains, StationRead[] Stations, BuildingRead[] Buildings,
    TrackRead[] Tracks, RemovedRead Removed) : WireMessage;
public sealed record RemovedRead(string[] TrainIds, string[] StationIds, string[] BuildingIds, PositionRead[] TrackPositions);
public sealed record MapRead(int Width, int Height, int Levels);
public sealed record PositionRead(int X, int Y, int Z);
public sealed record TrackRead(PositionRead Position, string Shape, long Revision);
public sealed record TrainRead(string Id, long Revision, string Name, PositionRead Position, int Cars, string Controller,
    string Direction, PositionRead[] CarPositions, string State);
public sealed record StationRead(string Id, long Revision, string Name, PositionRead Position);
public sealed record BuildingRead(string Id, string DefinitionId, PositionRead Position, int Height);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RunCommand), "run")]
[JsonDerivedType(typeof(PauseCommand), "pause")]
[JsonDerivedType(typeof(StepCommand), "step")]
[JsonDerivedType(typeof(RenameStationCommand), "renameStation")]
[JsonDerivedType(typeof(BuildTrackCommand), "buildTrack")]
[JsonDerivedType(typeof(RemoveTrackCommand), "removeTrack")]
[JsonDerivedType(typeof(SetControllerCommand), "setController")]
[JsonDerivedType(typeof(ControllerInputCommand), "controllerInput")]
public abstract record CommandPayload;
public sealed record RunCommand : CommandPayload;
public sealed record PauseCommand : CommandPayload;
public sealed record StepCommand : CommandPayload;
public sealed record RenameStationCommand(string StationId, long ExpectedRevision, string Name) : CommandPayload;
public sealed record BuildTrackCommand(PositionRead Position, string Shape) : CommandPayload;
public sealed record RemoveTrackCommand(PositionRead Position, long ExpectedRevision) : CommandPayload;
public sealed record SetControllerCommand(string TrainId, long ExpectedRevision, string Controller) : CommandPayload;
public sealed record ControllerInputCommand(string TrainId, long InputSequence, bool Pass, bool Depart,
    bool Reverse, bool Left, bool Right, bool Reset) : CommandPayload;

// Nested snapshots retain the same discriminator as standalone snapshots.
public sealed class NestedSnapshotConverter : JsonConverter<SnapshotMessage>
{
    public override SnapshotMessage Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<WireMessage>(ref reader, options) as SnapshotMessage
            ?? throw new JsonException("Expected a snapshot message.");
    public override void Write(Utf8JsonWriter writer, SnapshotMessage value, JsonSerializerOptions options)
        => JsonSerializer.Serialize<WireMessage>(writer, value, options);
}
