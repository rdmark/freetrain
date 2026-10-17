using System.Text.Json;
using System.Threading.Channels;
using FreeTrain.Contracts;
using FreeTrain.Session;

namespace FreeTrain.Server;

public sealed class NetworkOptions
{
    public int OutputCapacity { get; init; } = 32;
    public int ReceiptCapacity { get; init; } = 1024;
    public TimeSpan ReceiptRetention { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan InputExpiry { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan DisconnectGrace { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan SocketIdle { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan IdleRetention { get; init; } = TimeSpan.FromMinutes(5);
    public void Validate()
    {
        if (OutputCapacity is < 2 or > 1024 || ReceiptCapacity is < 1 or > 65536 ||
            ReceiptRetention < TimeSpan.FromSeconds(10) || InputExpiry <= TimeSpan.Zero ||
            DisconnectGrace < TimeSpan.Zero || SocketIdle <= InputExpiry || IdleRetention <= ReceiptRetention)
            throw new InvalidOperationException("Invalid FreeTrain:Network limits.");
    }
}

// All transport state is bounded and guarded here. The host remains the sole world owner.
public sealed class NetworkSession(NetworkOptions options)
{
    public const string ContentVersion = "prototype-content-1";
    public static readonly JsonSerializerOptions Json = WireJson.CreateOptions();
    private sealed record Receipt(string Payload, DateTimeOffset Expires, Task<WireMessage> Result);
    public sealed class Peer(int capacity, DateTimeOffset now)
    {
        public Channel<WireMessage> Output { get; } = Channel.CreateBounded<WireMessage>(new BoundedChannelOptions(capacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        public CancellationTokenSource Stop { get; } = new();
        internal SnapshotMessage? LastSnapshot;
        internal DateTimeOffset LastSeen = now;
        internal DateTimeOffset Window = now;
        internal int Messages;
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Receipt> receipts = new(StringComparer.Ordinal);
    private SessionHost host = SessionComposition.CreateReferenceHost();
    private Peer? active;
    private DateTimeOffset? disconnectedAt;
    private DateTimeOffset? lastInput;
    private long lastPublicationSlot;
    public string Incarnation { get; } = Guid.NewGuid().ToString("N");
    public string AccessCookie { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    public JoinMessage Bootstrap() { lock (gate) return Join(); }
    private JoinMessage Join() => new(1, ContentVersion, Incarnation, "reference-v1", "operator-1", host.WorldEpoch, host.Snapshot());
    public Peer? Connect(DateTimeOffset now)
    {
        lock (gate)
        {
            if (active != null) return null;
            active = new(options.OutputCapacity, now);
            var join = Join();
            active.LastSnapshot = join.Snapshot;
            Write(active, join);
            disconnectedAt = null;
            return active;
        }
    }
    public void Disconnect(Peer peer, DateTimeOffset now)
    {
        lock (gate)
        {
            if (active != peer) return;
            active = null;
            peer.Output.Writer.TryComplete();
            peer.Stop.Cancel();
            host.RequestInputReset(false);
            lastInput = null;
            disconnectedAt = now;
        }
    }
    public bool Touch(Peer peer, DateTimeOffset now)
    {
        lock (gate)
        {
            if (peer != active) return false;
            if (now - peer.Window >= TimeSpan.FromSeconds(1)) { peer.Window = now; peer.Messages = 0; }
            if (++peer.Messages > 64) { Disconnect(peer, now); return false; }
            peer.LastSeen = now;
            return true;
        }
    }
    public Task<WireMessage> Submit(Peer peer, CommandMessage command, DateTimeOffset now)
    {
        lock (gate)
        {
            Task<WireMessage> Reject(string code) => Task.FromResult<WireMessage>(new RejectedMessage(command.RequestId, code, new()));
            if (active != peer) return Reject("notAuthorized");
            foreach (var key in receipts.Where(x => x.Value.Expires <= now && x.Value.Result.IsCompleted).Select(x => x.Key).ToArray()) receipts.Remove(key);
            var payload = JsonSerializer.Serialize<WireMessage>(command, Json);
            if (receipts.TryGetValue(command.RequestId, out var receipt))
                return receipt.Payload == payload ? receipt.Result : Reject("requestConflict");
            if (receipts.Count >= options.ReceiptCapacity) return Reject("retryWindowFull");
            var result = host.Enqueue(new("operator-1"), command);
            if (command.Payload is ControllerInputCommand input) result = ObserveInput(result, peer, now, input.Reset);
            receipts.Add(command.RequestId, new(payload, now + options.ReceiptRetention, result));
            return result;
        }
    }
    private async Task<WireMessage> ObserveInput(Task<WireMessage> pending, Peer peer, DateTimeOffset now, bool reset)
    {
        var result = await pending;
        lock (gate) { if (result is AcceptedMessage && peer == active) lastInput = reset ? null : now; }
        return result;
    }
    public void Reply(Peer peer, WireMessage result)
    {
        lock (gate)
        {
            if (peer != active) return;
            Publish(peer); // Confirmed state precedes its committed acknowledgement.
            Write(peer, result);
        }
    }
    public void Resync(Peer peer)
    {
        lock (gate)
        {
            if (peer != active) return;
            peer.LastSnapshot = host.Snapshot();
            Write(peer, peer.LastSnapshot);
        }
    }
    public void ResetInput(Peer peer, bool pause)
    {
        lock (gate) { if (peer == active) { host.RequestInputReset(pause); lastInput = null; } }
    }
    public void Tick(DateTimeOffset now)
    {
        lock (gate)
        {
            disconnectedAt ??= active == null ? now : null;
            if (active != null && now - active.LastSeen >= options.SocketIdle) Disconnect(active, now);
            if (lastInput is { } inputAt && now - inputAt >= options.InputExpiry)
            { host.RequestInputReset(false); lastInput = null; }
            if (disconnectedAt is { } lostAt && now - lostAt >= options.DisconnectGrace) host.RequestInputReset(true);
            if (disconnectedAt is { } idleAt && now - idleAt >= options.IdleRetention)
            {
                host = SessionComposition.CreateReferenceHost(host.WorldEpoch + 1);
                receipts.Clear(); disconnectedAt = now;
            }
            host.Pump(1); // 20Hz, no unbounded catch-up after a stalled process.
            // Fixed 100ms slots avoid accumulating 50ms timer jitter into a slower rate.
            // Skip missed slots after a stall; never burst an unbounded catch-up batch.
            var slot = now.UtcTicks / TimeSpan.TicksPerMillisecond / 100;
            if (active != null && slot > lastPublicationSlot)
            { Publish(active); lastPublicationSlot = slot; }
        }
    }
    private void Publish(Peer peer)
    {
        var next = host.Snapshot();
        var previous = peer.LastSnapshot;
        if (previous?.Revision == next.Revision && previous.WorldEpoch == next.WorldEpoch) return;
        WireMessage message = previous == null || previous.WorldEpoch != next.WorldEpoch ? next : Difference(previous, next);
        if (Write(peer, message)) peer.LastSnapshot = next;
    }
    private bool Write(Peer peer, WireMessage message)
    {
        if (peer.Output.Writer.TryWrite(message)) return true;
        // Never discard a dependent delta or wait for the network in the host pump.
        Disconnect(peer, DateTimeOffset.UtcNow);
        return false;
    }
    public static DeltaMessage Difference(SnapshotMessage previous, SnapshotMessage next)
    {
        static T[] Changed<T, TKey>(T[] before, T[] after, Func<T, TKey> key) where TKey : notnull
        {
            var old = before.ToDictionary(key);
            return after.Where(x => !old.TryGetValue(key(x), out var value) ||
                JsonSerializer.Serialize(value, Json) != JsonSerializer.Serialize(x, Json)).ToArray();
        }
        return new(next.WorldEpoch, previous.Revision, next.Revision, next.Tick, next.Paused, next.Cash,
            Changed(previous.Trains, next.Trains, x => x.Id), Changed(previous.Stations, next.Stations, x => x.Id),
            Changed(previous.Buildings, next.Buildings, x => x.Id), Changed(previous.Tracks, next.Tracks, x => x.Position),
            new(previous.Trains.Select(x => x.Id).Except(next.Trains.Select(x => x.Id)).ToArray(),
                previous.Stations.Select(x => x.Id).Except(next.Stations.Select(x => x.Id)).ToArray(),
                previous.Buildings.Select(x => x.Id).Except(next.Buildings.Select(x => x.Id)).ToArray(),
                previous.Tracks.Select(x => x.Position).Except(next.Tracks.Select(x => x.Position)).ToArray()));
    }
}

public sealed class SessionDriver(NetworkSession session) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (await timer.WaitForNextTickAsync(stoppingToken)) session.Tick(DateTimeOffset.UtcNow);
    }
}
