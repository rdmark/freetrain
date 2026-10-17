namespace FreeTrain.Persistence;

// Save v1 is an initial data contract, not a working legacy-save importer.
// The world converter and atomic validation/loading are Stage 6 work.
public sealed record SaveDocument(int SaveVersion, string ContentVersion, string ScenarioId,
    SavedWorld World);
public sealed record SavedWorld(long Tick, bool Paused, long Cash, long RandomState,
    SavedTrack[] Tracks, SavedTrain[] Trains, SavedStation[] Stations, SavedBuilding[] Buildings,
    ScheduledEvent[] Events);
public sealed record SavedPosition(int X, int Y, int Z);
public sealed record SavedTrack(SavedPosition Position, string Shape);
public sealed record SavedTrain(string Id, string DefinitionId, SavedPosition Position,
    string Direction, int Cars, string Controller, long NextMoveTick);
public sealed record SavedStation(string Id, string Name, SavedPosition Position, int DwellTicks);
public sealed record SavedBuilding(string Id, string DefinitionId, SavedPosition Position, int Height);
public sealed record ScheduledEvent(long DueTick, long Order, string Kind, string SubjectId);
