using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreeTrain.Content;
using FreeTrain.Contracts;
using FreeTrain.Persistence;
using FreeTrain.Plugins.ManualController;
using FreeTrain.Session;
using FreeTrain.Simulation;
using Xunit;

public class SimulationTests
{
    private static readonly ParticipantContext Owner = new("operator-1");
    private static SessionHost Host(ScenarioDefinition? scenario = null, int capacity = 256) => new(
        scenario ?? ReferenceScenario.Create(), Owner.ParticipantId,
        [() => new AutomaticController(), () => new ManualTrainController()], capacity);
    private static WireMessage Send(SessionHost host, CommandPayload payload, long epoch = 1)
    {
        var task = host.Enqueue(Owner, new("test-command", epoch, payload));
        host.Pump();
        return task.GetAwaiter().GetResult();
    }
    private static void Accept(SessionHost host, CommandPayload payload) => Assert.IsType<AcceptedMessage>(Send(host, payload));
    private static void Run(SessionHost host) => Accept(host, new RunCommand());
    private static ImmutableArray<SimulationEvent> Advance(SessionHost host, int ticks)
    {
        var events = ImmutableArray.CreateBuilder<SimulationEvent>();
        while (ticks > 0)
        {
            var count = Math.Min(ticks, SessionHost.MaximumTicksPerPump);
            var batch = host.Pump(count);
            Assert.Equal(count, batch.AdvancedTicks);
            events.AddRange(batch.Events);
            ticks -= count;
        }
        return events.ToImmutable();
    }
    private static TrainState Train(SessionHost host) => Assert.Single(host.ReadState().Trains);
    private static void Manual(SessionHost host) => Accept(host, new SetControllerCommand("train-1", Train(host).Revision, "manual"));
    private static void Input(SessionHost host, long sequence = 0, bool pass = false, bool depart = false, bool reverse = false,
        bool left = false, bool right = false, bool reset = false) => Accept(host,
            new ControllerInputCommand("train-1", sequence, pass, depart, reverse, left, right, reset));
    private static string Frame(SessionHost host, ImmutableArray<SimulationEvent> events) => JsonSerializer.Serialize(
        new { state = host.ReadState(), events }, WireJson.CreateOptions());

    [Fact]
    public void Fresh_typed_scenario_matches_shared_snapshot_and_initial_save_data()
    {
        var host = Host();
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/snapshot.json")));
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode<WireMessage>(host.Snapshot(), WireJson.CreateOptions())));
        var save = JsonSerializer.Deserialize<SaveDocument>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/save-reference.json")), WireJson.CreateOptions())!;
        var state = host.ReadState();
        Assert.Equal(80, state.Tracks.Length);
        Assert.Equal(save.World.Tracks.Length, state.Tracks.Length);
        foreach (var track in save.World.Tracks)
        {
            var actual = Assert.Single(state.Tracks.Where(x => x.Position == new GridPosition(track.Position.X, track.Position.Y, track.Position.Z)));
            Assert.Equal(track.Shape, actual.Shape.ToString(), ignoreCase: true);
        }
        Assert.Equal(new DateTime(2000, 4, 1, 8, 0, 0), state.Time);
        Assert.True(state.Paused);
        Assert.Equal(0, state.Tick);
        Assert.Equal(0, state.Revision);
        Assert.Equal(1_000_000, state.Cash);
        Assert.Equal(477, state.RandomState);
        Assert.Equal(new PendingAction(1, 0, "moveTrain", "train-1"), Assert.Single(state.Pending));
        Assert.Equal(new GridPosition(10, 8, 1), Train(host).Cars[2]);
        Assert.Equal(2, ReferenceScenario.BuildingFamilies.Length);
        Assert.Equal(5, ReferenceScenario.BuildingFamilies[1].MaximumHeight);
    }

    [Fact]
    public void Clock_orders_due_tick_then_insertion_order_and_forbids_same_tick_rescheduling()
    {
        var clock = new SimulationClock(new DateTime(2000, 4, 1));
        clock.Schedule(2, "moveTrain", "second-tick-first");
        clock.Schedule(1, "moveTrain", "first-tick");
        clock.Schedule(2, "moveTrain", "second-tick-second");
        Assert.Equal("first-tick", Assert.Single(clock.Advance()).SubjectId);
        Assert.Equal(["second-tick-first", "second-tick-second"], clock.Advance().Select(x => x.SubjectId));
        Assert.Empty(clock.Pending);
        Assert.Equal(3, clock.NextOrder);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Schedule(2, "moveTrain", "invalid"));
    }

    [Fact]
    public void Random_algorithm_has_fixed_vectors_and_independent_state()
    {
        var first = new DeterministicRandom(1);
        var second = new DeterministicRandom(1);
        Assert.Equal(16807, first.Next());
        Assert.Equal(282475249, first.Next());
        Assert.Equal(1622650073, first.Next());
        Assert.Equal(1, second.State);
        Assert.Equal(16807, second.Next());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandom(0));
    }

    [Fact]
    public void Automatic_train_arrives_at_eight_dwells_until_thirteen_and_stays_on_loop()
    {
        var host = Host(); Run(host);
        Advance(host, 7);
        Assert.Equal(new GridPosition(19, 8, 1), Train(host).Cars[0]);
        var arrival = Advance(host, 1);
        Assert.Contains(arrival, x => x.Kind == "stationArrived" && x.Tick == 8);
        Assert.Equal(TrainStatus.Station, Train(host).Status);
        Assert.Equal(13, Train(host).NextMoveTick);
        Assert.Empty(Advance(host, 4));
        Assert.Equal(new GridPosition(20, 8, 1), Train(host).Cars[0]);
        var departure = Advance(host, 1);
        Assert.Contains(departure, x => x.Kind == "stationDeparted" && x.Tick == 13);
        Assert.Equal(new GridPosition(21, 8, 1), Train(host).Cars[0]);
        var junction = Advance(host, 16);
        Assert.Contains(junction, x => x.Kind == "junctionStraight" && x.Tick == 29);
        Assert.DoesNotContain(junction, x => x.Kind == "junctionTurned");
        Assert.Equal(new GridPosition(28, 17, 1), Train(host).Cars[0]);
    }

    [Fact]
    public async Task Paused_host_drains_fifo_commands_and_single_steps_exactly_once()
    {
        var host = Host();
        var run = host.Enqueue(Owner, new("run", 1, new RunCommand()));
        var pause = host.Enqueue(Owner, new("pause", 1, new PauseCommand()));
        var step = host.Enqueue(Owner, new("step", 1, new StepCommand()));
        Assert.False(run.IsCompleted);
        var batch = host.Pump(20);
        Assert.Equal(1, batch.AdvancedTicks);
        Assert.Equal(3, batch.AppliedCommands);
        Assert.True(host.ReadState().Paused);
        Assert.Equal(1, host.ReadState().Tick);
        Assert.Equal(1, Assert.IsType<AcceptedMessage>(await run).Sequence);
        Assert.Equal(2, Assert.IsType<AcceptedMessage>(await pause).Sequence);
        Assert.Equal(3, Assert.IsType<AcceptedMessage>(await step).Sequence);
        Assert.Equal(0, host.Pump(20).AdvancedTicks);
        Accept(host, new RenameStationCommand("station-1", 0, "新中央駅"));
        Assert.Equal("新中央駅", Assert.Single(host.ReadState().Stations).Name);
        Assert.Equal(1, host.ReadState().Tick);
        Run(host);
        Assert.Equal("invalidInput", Assert.IsType<RejectedMessage>(Send(host, new StepCommand())).Code);
    }

    [Fact]
    public async Task Queue_and_tick_batches_are_bounded_and_unauthorized_commands_cannot_fill_queue()
    {
        var host = Host(capacity: 1);
        var unauthorized = host.Enqueue(new("stranger"), new("bad", 1, new RunCommand()));
        Assert.Equal("notAuthorized", Assert.IsType<RejectedMessage>(await unauthorized).Code);
        var pending = host.Enqueue(Owner, new("first", 1, new RunCommand()));
        var overflow = host.Enqueue(Owner, new("second", 1, new PauseCommand()));
        Assert.Equal("queueFull", Assert.IsType<RejectedMessage>(await overflow).Code);
        host.Pump();
        Assert.IsType<AcceptedMessage>(await pending);
        Assert.Throws<ArgumentOutOfRangeException>(() => host.Pump(257));
        Assert.Equal(256, host.Pump(256).AdvancedTicks);
        var bounded = Host();
        var tasks = Enumerable.Range(0, 100).Select(i => bounded.Enqueue(Owner, new($"step-{i}", 1, new StepCommand()))).ToArray();
        Assert.Equal(64, bounded.Pump().AppliedCommands);
        Assert.Equal(64, tasks.Count(x => x.IsCompleted));
        Assert.Equal(36, bounded.Pump().AppliedCommands);
        Assert.Equal(100, bounded.ReadState().Tick);
    }

    [Fact]
    public async Task Concurrent_producers_and_pumps_serialize_each_commit()
    {
        var host = Host();
        var submissions = await Task.WhenAll(Enumerable.Range(0, 80).Select(i => Task.Run(() =>
            new { Completion = host.Enqueue(Owner, new($"step-{i}", 1, new StepCommand())) })));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => host.Pump())));
        var results = await Task.WhenAll(submissions.Select(x => x.Completion));
        Assert.Equal(80, results.OfType<AcceptedMessage>().Select(x => x.Sequence).Distinct().Count());
        Assert.Equal(80, host.ReadState().Tick);
        Assert.True(host.ReadState().Paused);
    }

    [Fact]
    public void Invalid_and_stale_commands_leave_world_completely_unchanged()
    {
        var host = Host();
        CommandPayload[] invalid = [
            new BuildTrackCommand(new(-1, 0, 1), "eastWest"), new BuildTrackCommand(new(64, 16, 1), "eastWest"),
            new BuildTrackCommand(new(37, 16, 2), "eastWest"), new BuildTrackCommand(new(37, 16, 1), "0"),
            new BuildTrackCommand(new(12, 8, 1), "eastWest"), new BuildTrackCommand(new(37, 16, 1), "northSouth"),
            new BuildTrackCommand(new(2, 2, 1), "eastWest"), new RemoveTrackCommand(new(10, 8, 1), 0),
            new RemoveTrackCommand(new(20, 8, 1), 0), new RemoveTrackCommand(new(36, 16, 1), 1),
            new RenameStationCommand("station-1", 1, "new"), new RenameStationCommand("station-1", 0, " \n"),
            new SetControllerCommand("train-1", 1, "manual"), new SetControllerCommand("train-1", 0, "dll"),
            new ControllerInputCommand("train-1", 0, true, false, false, false, false, false)
        ];
        var before = Frame(host, []);
        foreach (var command in invalid)
        {
            Assert.IsType<RejectedMessage>(Send(host, command));
            Assert.Equal(before, Frame(host, []));
        }
        Assert.Equal("staleEpoch", Assert.IsType<RejectedMessage>(Send(host, new RunCommand(), epoch: 2)).Code);
        Assert.Equal(before, Frame(host, []));
    }

    [Fact]
    public void Siding_extension_validates_and_charges_both_tiles_atomically_then_removes_without_refund()
    {
        var host = Host();
        var before = Frame(host, []); // No command for preview/cancel: no authority-side effect.
        Assert.Equal(before, Frame(host, []));
        Accept(host, new BuildTrackCommand(new(37, 16, 1), "buffer"));
        var state = host.ReadState();
        Assert.Equal(999800, state.Cash);
        Assert.Equal(81, state.Tracks.Length);
        var replaced = Assert.Single(state.Tracks.Where(x => x.Position == new GridPosition(36, 16, 1)));
        Assert.Equal(TrackShape.EastWest, replaced.Shape);
        Assert.Equal(1, replaced.Revision);
        var built = Assert.Single(state.Tracks.Where(x => x.Position == new GridPosition(37, 16, 1)));
        Assert.Equal(TrackShape.Buffer, built.Shape);
        var committed = Frame(host, []);
        Assert.IsType<RejectedMessage>(Send(host, new BuildTrackCommand(new(37, 16, 1), "buffer")));
        Assert.Equal(committed, Frame(host, []));
        Accept(host, new RemoveTrackCommand(new(37, 16, 1), built.Revision));
        Assert.Equal(999800, host.ReadState().Cash);
        Accept(host, new BuildTrackCommand(new(37, 16, 1), "eastWest"));
        Assert.Equal(999700, host.ReadState().Cash); // Now only one tile changes.
    }

    [Fact]
    public void Insufficient_funds_or_occupied_replacement_preserves_all_state()
    {
        var poor = Host(ReferenceScenario.Create() with { InitialCash = 199 });
        var before = Frame(poor, []);
        Assert.Equal("insufficientFunds", Assert.IsType<RejectedMessage>(Send(poor, new BuildTrackCommand(new(37, 16, 1), "buffer"))).Code);
        Assert.Equal(before, Frame(poor, []));
        var source = ReferenceScenario.Create();
        var occupied = Host(source with { Buildings = source.Buildings.Add(new("block", "test", new(36, 16, 0), 2)) });
        before = Frame(occupied, []);
        Assert.IsType<RejectedMessage>(Send(occupied, new BuildTrackCommand(new(37, 16, 1), "buffer")));
        Assert.Equal(before, Frame(occupied, []));
    }

    [Fact]
    public void Broken_connection_blocks_train_and_controller_switch_clears_input()
    {
        var host = Host();
        Accept(host, new RemoveTrackCommand(new(13, 8, 1), 0));
        Run(host);
        Assert.Contains(Advance(host, 1), x => x.Kind == "blocked");
        Assert.Equal(new GridPosition(12, 8, 1), Train(host).Cars[0]);
        Assert.Equal(TrainStatus.Blocked, Train(host).Status);
        Manual(host); Input(host, left: true);
        Accept(host, new SetControllerCommand("train-1", Train(host).Revision, "automatic"));
        Assert.Equal(default, Train(host).Input);
        Assert.Null(Train(host).InputExpiresAt);
    }

    [Fact]
    public void Construction_handles_Int64_price_boundaries_without_partial_mutation()
    {
        var host = Host(ReferenceScenario.Create() with { InitialCash = long.MaxValue, ConstructionCost = long.MaxValue });
        var before = Frame(host, []);
        Assert.Equal("insufficientFunds", Assert.IsType<RejectedMessage>(Send(host, new BuildTrackCommand(new(37, 16, 1), "buffer"))).Code);
        Assert.Equal(before, Frame(host, []));
    }

    [Fact]
    public void Manual_waits_repeat_and_depart_input_takes_precedence_over_reverse()
    {
        var host = Host(); Manual(host); Run(host);
        var events = Advance(host, 13);
        Assert.Contains(events, x => x.Kind == "stationWait" && x.Tick == 13);
        Assert.Equal(18, Train(host).NextMoveTick);
        Advance(host, 4);
        Input(host, depart: true, reverse: true);
        events = Advance(host, 1);
        Assert.Contains(events, x => x.Kind == "stationDeparted" && x.Tick == 18);
        Assert.DoesNotContain(events, x => x.Kind == "reversed");
        Assert.Equal(new GridPosition(21, 8, 1), Train(host).Cars[0]);
    }

    [Fact]
    public void Manual_shift_passes_on_initial_call_and_reverse_swaps_car_order_before_moving()
    {
        var pass = Host(); Manual(pass); Run(pass); Advance(pass, 7); Input(pass, pass: true);
        Assert.Contains(Advance(pass, 1), x => x.Kind == "stationPassed");
        Assert.Equal(9, Train(pass).NextMoveTick);
        Assert.Equal(new GridPosition(21, 8, 1), AdvanceAndTrain(pass, 1).Cars[0]);
        var reverse = Host(); Manual(reverse); Run(reverse); Advance(reverse, 12); Input(reverse, reverse: true);
        var events = Advance(reverse, 1);
        Assert.Contains(events, x => x.Kind == "reversed");
        Assert.Equal(Direction.West, Train(reverse).Direction);
        Assert.Equal<GridPosition>([new GridPosition(17, 8, 1), new(18, 8, 1), new(19, 8, 1)], Train(reverse).Cars);
    }
    private static TrainState AdvanceAndTrain(SessionHost host, int ticks) { Advance(host, ticks); return Train(host); }

    [Fact]
    public void Manual_left_turn_uses_valid_siding_and_stops_at_buffer_without_teleporting_cars()
    {
        var host = Host(); Run(host); Advance(host, 28); Manual(host); Input(host, left: true, right: true);
        var events = Advance(host, 9);
        Assert.Contains(events, x => x.Kind == "junctionTurned" && x.Tick == 29);
        Assert.Contains(events, x => x.Kind == "bufferStopped" && x.Tick == 37);
        Assert.Equal(TrainStatus.Buffer, Train(host).Status);
        Assert.Equal<GridPosition>([new GridPosition(36, 16, 1), new(35, 16, 1), new(34, 16, 1)], Train(host).Cars);
        Advance(host, 20);
        Assert.Equal(new GridPosition(36, 16, 1), Train(host).Cars[0]);
    }

    [Fact]
    public void Manual_controller_checks_left_before_right_and_ignores_invalid_turns()
    {
        var controller = new ManualTrainController();
        var input = new ManualInput(false, false, false, true, true);
        Assert.Equal(Direction.West, controller.AtJunction(input, Direction.North, [Direction.West, Direction.East, Direction.North]));
        Assert.Equal(Direction.East, controller.AtJunction(input, Direction.North, [Direction.East, Direction.North]));
        Assert.Equal(Direction.North, controller.AtJunction(input, Direction.North, [Direction.North]));
    }

    [Fact]
    public void Reset_input_ordering_and_expiry_clear_held_keys()
    {
        var host = Host(); Manual(host); Run(host); Advance(host, 7);
        Input(host, pass: true); Input(host, sequence: 1, reset: true, pass: true);
        Assert.Equal(default, Train(host).Input);
        Assert.Null(Train(host).InputExpiresAt);
        var before = Frame(host, []);
        Assert.IsType<RejectedMessage>(Send(host, new ControllerInputCommand("train-1", 0, true, false, false, false, false, false)));
        Assert.Equal(before, Frame(host, []));
        Advance(host, 1);
        Assert.Equal(TrainStatus.Station, Train(host).Status);
        Input(host, sequence: 2, depart: true);
        var events = Advance(host, 5); // expires at 13, before the station decision.
        Assert.Contains(events, x => x.Kind == "inputExpired" && x.Tick == 13);
        Assert.Contains(events, x => x.Kind == "stationWait" && x.Tick == 13);
        Assert.Equal(default, Train(host).Input);
        Assert.Equal(18, Train(host).NextMoveTick);
    }

    [Fact]
    public void Isolated_sessions_and_copied_read_models_do_not_share_mutation()
    {
        var definition = ReferenceScenario.Create();
        var first = Host(definition); var second = Host(definition);
        var original = Frame(second, []);
        var read = first.ReadState(); var snapshot = first.Snapshot();
        snapshot.Trains[0].CarPositions[0] = new(99, 99, 99);
        snapshot.Tracks[0] = new(new(99, 99, 99), "buffer", 99);
        Assert.Equal(new GridPosition(12, 8, 1), Train(first).Cars[0]);
        Accept(first, new BuildTrackCommand(new(37, 16, 1), "buffer"));
        Accept(first, new RenameStationCommand("station-1", 0, "新中央駅"));
        Manual(first); Input(first, pass: true); Run(first); Advance(first, 100);
        Assert.Equal(original, Frame(second, []));
        Assert.Equal(0, read.Tick);
        Assert.Equal(80, read.Tracks.Length);
        Assert.Equal("中央駅", read.Stations[0].Name);
        Assert.NotEqual(first.ReadState().NextEventOrder, second.ReadState().NextEventOrder);
    }

    [Fact]
    public void Ten_thousand_ticks_match_full_traces_and_assert_movement_dwell_routes_and_accounting()
    {
        var first = Host(); var second = Host(); Run(first); Run(second);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var arrivals = new List<long>(); var departures = new List<long>();
        var moved = 0; var junctions = 0;
        for (var tick = 1; tick <= 10000; tick++)
        {
            var previous = Train(first).Cars;
            var a = first.Pump(1); var b = second.Pump(1);
            var state = first.ReadState(); var train = Train(first);
            var frame = Frame(first, a.Events);
            Assert.Equal(frame, Frame(second, b.Events));
            hash.AppendData(Encoding.UTF8.GetBytes(frame + "\n"));
            Assert.Equal(tick, state.Tick);
            Assert.Equal(1_000_000, state.Cash);
            Assert.Equal(477, state.RandomState); // No random gameplay in this slice.
            Assert.Equal(3, train.Cars.Distinct().Count());
            Assert.Single(state.Pending);
            foreach (var car in train.Cars) Assert.Contains(state.Tracks, x => x.Position == car && state.Map.Contains(car));
            Assert.DoesNotContain(a.Events, x => x.Kind is "blocked" or "bufferStopped" or "junctionTurned");
            var move = a.Events.Any(x => x.Kind == "moved");
            if (move)
            {
                moved++;
                Assert.Equal(previous[0], train.Cars[1]);
                Assert.Equal(previous[1], train.Cars[2]);
                Assert.Equal(1, Math.Abs(train.Cars[0].X - previous[0].X) + Math.Abs(train.Cars[0].Y - previous[0].Y));
                var from = Assert.Single(state.Tracks.Where(x => x.Position == previous[0]));
                var to = Assert.Single(state.Tracks.Where(x => x.Position == train.Cars[0]));
                Assert.Contains(train.Direction, from.Shape.Ports());
                Assert.Contains(train.Direction.Opposite(), to.Shape.Ports());
            }
            else Assert.Equal<GridPosition>(previous, train.Cars);
            if (a.Events.Any(x => x.Kind == "stationArrived")) arrivals.Add(tick);
            if (a.Events.Any(x => x.Kind == "stationDeparted")) departures.Add(tick);
            junctions += a.Events.Count(x => x.Kind == "junctionStraight");
        }
        Assert.Equal(132, arrivals.Count);
        Assert.Equal(132, departures.Count);
        Assert.Equal(132, junctions);
        Assert.Equal(9472, moved);
        Assert.Equal(Enumerable.Range(0, 132).Select(i => 8L + i * 76), arrivals);
        Assert.Equal(arrivals.Select(x => x + 5), departures);
        Assert.Equal("828a860593d74b26d4417410b802f72f4f383535252571901a253deb7575ffe0", Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
