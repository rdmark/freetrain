using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreeTrain.Content;
using FreeTrain.Contracts;
using FreeTrain.Persistence;
using FreeTrain.Plugins.ManualController;
using FreeTrain.Server;
using FreeTrain.Session;
using FreeTrain.Simulation;
using Xunit;

public class PersistenceTests
{
    private static readonly ScenarioDefinition Scenario = ReferenceScenario.Create();
    private static ITrainController[] Controllers() => [new AutomaticController(), new ManualTrainController()];
    private static SessionHost Host(int capacity = 256) => new(Scenario, "operator-1", [() => new AutomaticController(), () => new ManualTrainController()], capacity);
    private static SaveDocument Capture(SessionHost host)
    {
        var pending = host.CaptureSave(); host.Pump(); return pending.GetAwaiter().GetResult();
    }
    private static WireMessage Apply(SessionHost host, CommandPayload payload, long? epoch = null)
    {
        var pending = host.Enqueue(new("operator-1"), new(Guid.NewGuid().ToString(), epoch ?? host.WorldEpoch, payload));
        host.Pump(); return pending.GetAwaiter().GetResult();
    }
    private static string State(WorldState state) => JsonSerializer.Serialize(SaveCodec.Capture(Scenario, state), SaveCodec.Json);

    [Fact]
    public void Shared_initial_save_fixture_is_loadable()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/save-reference.json"));
        Assert.Equal(0, SaveCodec.Restore(SaveCodec.Decode(bytes), Scenario, Controllers()).Tick);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)] // arrival, with a future station dwell action
    [InlineData(11)] // midway through dwell
    [InlineData(29)] // curved car body, not inferred from head/direction
    [InlineData(1000)]
    public void Save_continuation_matches_complete_state_and_events_for_10000_ticks(int tick)
    {
        var host = Host(); Apply(host, new RunCommand());
        for (var i = 0; i < tick; i++) host.Pump(1);
        Apply(host, new RenameStationCommand("station-1", 0, "保存した中央駅"));
        Apply(host, new BuildTrackCommand(new(37, 16, 1), "buffer"));
        var save = Capture(host);
        var restored = SaveCodec.Restore(SaveCodec.Decode(SaveCodec.Encode(save)), Scenario, Controllers());
        Assert.Equal(State(host.ReadState()), State(restored.Read()));
        for (var i = 0; i < 10000; i++)
        {
            var expected = host.Pump(1).Events;
            var actual = restored.AdvanceOneTick();
            Assert.Equal(expected.ToArray(), actual.ToArray());
            Assert.Equal(State(host.ReadState()), State(restored.Read()));
        }
    }

    [Fact]
    public async Task Load_is_fifo_atomic_paused_changes_epoch_and_invalidates_queued_commands()
    {
        var host = Host(); var save = Capture(host);
        var first = host.Enqueue(new("operator-1"), new("before", 1, new BuildTrackCommand(new(37, 16, 1), "buffer")));
        var captured = host.CaptureSave();
        var load = host.LoadSave(save, 1);
        var stale = host.Enqueue(new("operator-1"), new("after", 1, new StepCommand()));
        host.Pump();
        Assert.IsType<AcceptedMessage>(await first);
        Assert.Equal(999800, (await captured).World.Cash);
        Assert.Equal(2, (await load).WorldEpoch);
        Assert.Equal("staleEpoch", Assert.IsType<RejectedMessage>(await stale).Code);
        Assert.True(host.ReadState().Paused); Assert.Equal(0, host.ReadState().Tick); Assert.Equal(1000000, host.ReadState().Cash);
        var wrongEpoch = host.LoadSave(save, 1); host.Pump();
        Assert.Equal("staleEpoch", (await Assert.ThrowsAsync<InvalidOperationException>(() => wrongEpoch)).Message);
        Assert.Equal(2, host.WorldEpoch);
    }

    public static IEnumerable<object[]> InvalidEdits() => new[] {
        "version", "content", "scenario", "dimensions", "terrain", "clock", "negativeTick", "tickOverflow", "negativeCash", "random", "revision",
        "trackBounds", "duplicateTrack", "trackShape", "trackRevision", "missingRail", "disconnectedCars", "duplicateCar", "unknownTrain", "contribution", "direction",
        "controller", "status", "stationReference", "pastAction", "missingAction", "unknownAction", "unknownSubject", "eventOrder", "duplicateAction",
        "nullTrain", "nullTracks", "nullMap", "stationName", "stationPosition", "building", "unknownProperty", "numericMoney", "overflowMoney"
    }.Select(name => new object[] { name });

    [Theory, MemberData(nameof(InvalidEdits))]
    public async Task Malformed_or_inconsistent_save_preserves_world_and_epoch(string edit)
    {
        var host = Host(); Apply(host, new StepCommand());
        var before = State(host.ReadState()); var node = JsonSerializer.SerializeToNode(Capture(host), SaveCodec.Json)!;
        var w = node["world"]!; var train = w["trains"]![0]!;
        switch (edit)
        {
            case "version": node["saveVersion"] = 2; break;
            case "content": node["contentVersion"] = "unknown"; break;
            case "scenario": node["scenarioId"] = "unknown"; break;
            case "dimensions": w["map"]!["width"] = 1000000; break;
            case "terrain": w["terrain"] = "ocean"; break;
            case "clock": w["clockStart"] = "9999-01-01T00:00:00"; break;
            case "negativeTick": w["tick"] = "-1"; break;
            case "tickOverflow": w["tick"] = "9223372036854775807"; break;
            case "negativeCash": w["cash"] = "-1"; break;
            case "random": w["randomState"] = "2147483647"; break;
            case "revision": w["revision"] = "-1"; break;
            case "trackBounds": w["tracks"]![0]!["position"]!["x"] = 64; break;
            case "duplicateTrack": w["tracks"]!.AsArray().Add(w["tracks"]![0]!.DeepClone()); break;
            case "trackShape": w["tracks"]![0]!["shape"] = "magic"; break;
            case "trackRevision": w["tracks"]![0]!["revision"] = "99"; break;
            case "missingRail": w["tracks"]!.AsArray().Clear(); break;
            case "disconnectedCars": train["cars"]![1]!["x"] = 8; break;
            case "duplicateCar": train["cars"]![1] = train["cars"]![0]!.DeepClone(); break;
            case "unknownTrain": train["id"] = "unknown"; break;
            case "contribution": train["definitionId"] = "unknown.dll"; break;
            case "direction": train["direction"] = "north"; break;
            case "controller": train["controller"] = "unknown"; break;
            case "status": train["status"] = "station"; break;
            case "stationReference": train["stationId"] = "missing"; break;
            case "pastAction": train["nextMoveTick"] = "1"; break;
            case "missingAction": w["events"]!.AsArray().Clear(); break;
            case "unknownAction": w["events"]![0]!["kind"] = "releaseStation"; break;
            case "unknownSubject": w["events"]![0]!["subjectId"] = "unknown"; break;
            case "eventOrder": w["nextEventOrder"] = "0"; break;
            case "duplicateAction": w["events"]!.AsArray().Add(w["events"]![0]!.DeepClone()); break;
            case "nullTrain": w["trains"]![0] = null; break;
            case "nullTracks": w["tracks"] = null; break;
            case "nullMap": w["map"] = null; break;
            case "stationName": w["stations"]![0]!["name"] = "\n"; break;
            case "stationPosition": w["stations"]![0]!["position"]!["x"] = 0; break;
            case "building": w["buildings"]![0]!["definitionId"] = "unknown"; break;
            case "unknownProperty": w["credential"] = "secret"; break;
            case "numericMoney": w["cash"] = 9007199254740993L; break;
            case "overflowMoney": w["cash"] = "9223372036854775808"; break;
        }
        try
        {
            var save = SaveCodec.Decode(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString()));
            var load = host.LoadSave(save); host.Pump();
            await Assert.ThrowsAsync<InvalidDataException>(() => load);
        }
        catch (InvalidDataException) { /* Malformed JSON contracts reject before enqueuing. */ }
        Assert.Equal(before, State(host.ReadState())); Assert.Equal(1, host.WorldEpoch);
    }

    [Fact]
    public async Task Load_discards_old_world_events_from_the_same_boundary()
    {
        var host = Host(); var initial = Capture(host);
        for (var i = 0; i < 7; i++) Apply(host, new StepCommand());
        var arrival = host.Enqueue(new("operator-1"), new("arrival", 1, new StepCommand()));
        var load = host.LoadSave(initial);
        var pump = host.Pump();
        Assert.IsType<AcceptedMessage>(await arrival); await load;
        Assert.Empty(pump.Events); Assert.Equal(0, host.ReadState().Tick);
    }

    [Fact]
    public async Task Controller_state_survives_load_while_held_input_is_cleared()
    {
        var host = Host(); Apply(host, new SetControllerCommand("train-1", 0, "manual"));
        Apply(host, new ControllerInputCommand("train-1", 1, true, true, true, true, true, false));
        var save = Capture(host); var load = host.LoadSave(save); host.Pump(); await load;
        var train = Assert.Single(host.ReadState().Trains);
        Assert.Equal("manual", train.Controller); Assert.Equal(default, train.Input); Assert.Null(train.InputExpiresAt);
        Assert.True(host.ReadState().Paused);
    }

    [Fact]
    public async Task Persistence_operations_share_bounded_command_capacity()
    {
        var host = Host(1); var capture = host.CaptureSave();
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.CaptureSave());
        host.Pump(); Assert.Equal(0, (await capture).World.Tick);
    }

    [Fact]
    public async Task Atomic_slots_restart_and_independent_backup_restore_use_a_single_writer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "freetrain-saves-" + Guid.NewGuid());
        var backup = Path.Combine(Path.GetTempPath(), "freetrain-backup-" + Guid.NewGuid());
        Directory.CreateDirectory(backup);
        try
        {
            string incarnation;
            using (var storage = new SaveStorage(directory))
            {
                Assert.Throws<IOException>(() => new SaveStorage(directory));
                Assert.Throws<InvalidDataException>(() => storage.Exists("../escape"));
                var session = new NetworkSession(new(), storage); incarnation = session.Incarnation;
                var peer = session.Connect(DateTimeOffset.UtcNow)!;
                var rename = session.Submit(peer, new("rename", 1, new RenameStationCommand("station-1", 0, "独立バックアップ駅")), DateTimeOffset.UtcNow);
                session.Tick(DateTimeOffset.UtcNow); Assert.IsType<AcceptedMessage>(await rename);
                var checkpoint = session.Checkpoint(); session.Tick(DateTimeOffset.UtcNow); await checkpoint;
                File.Copy(Path.Combine(directory, "checkpoint.json"), Path.Combine(backup, "checkpoint.json"));
                // Changes after a checkpoint are deliberately lost after a crash.
                var step = session.Submit(peer, new("step", 1, new StepCommand()), DateTimeOffset.UtcNow);
                session.Tick(DateTimeOffset.UtcNow); await step;
                Assert.Equal(1, session.Bootstrap().Snapshot.Tick);
                Assert.Equal(0, storage.Read("checkpoint").World.Tick);
                Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            }
            using (var storage = new SaveStorage(directory))
            {
                var restarted = new NetworkSession(new(), storage);
                Assert.NotEqual(incarnation, restarted.Incarnation);
                Assert.Equal(0, restarted.Bootstrap().Snapshot.Tick);
                Assert.True(restarted.Bootstrap().Snapshot.Paused);
                Assert.Equal("独立バックアップ駅", restarted.Bootstrap().Snapshot.Stations[0].Name);
            }
            using (var storage = new SaveStorage(backup))
                Assert.Equal("独立バックアップ駅", new NetworkSession(new(), storage).Bootstrap().Snapshot.Stations[0].Name);
            File.WriteAllText(Path.Combine(directory, "checkpoint.json"), "{}");
            using (var storage = new SaveStorage(directory))
                Assert.Throws<InvalidDataException>(() => new NetworkSession(new(), storage));
        }
        finally { Directory.Delete(directory, true); Directory.Delete(backup, true); }
    }

    [Fact]
    public async Task Station_sound_has_a_live_event_id_and_is_absent_from_reconnect_snapshots()
    {
        var session = new NetworkSession(new()); var peer = session.Connect(DateTimeOffset.UtcNow)!;
        while (peer.Output.Reader.TryRead(out _)) { }
        for (var i = 0; i < 8; i++)
        {
            var step = session.Submit(peer, new("step-" + i, 1, new StepCommand()), DateTimeOffset.UtcNow);
            session.Tick(DateTimeOffset.UtcNow); await step;
        }
        var sounds = new List<SoundEventMessage>();
        while (peer.Output.Reader.TryRead(out var message)) if (message is SoundEventMessage sound) sounds.Add(sound);
        Assert.Contains(":1:8:train-1:stationArrived", Assert.Single(sounds).EventId);
        session.Disconnect(peer, DateTimeOffset.UtcNow);
        var next = session.Connect(DateTimeOffset.UtcNow)!;
        Assert.IsType<JoinMessage>(await next.Output.Reader.ReadAsync());
        Assert.False(next.Output.Reader.TryRead(out _));
    }
}
