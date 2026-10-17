using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeTrain.Contracts;

namespace FreeTrain.Server;

public static class HeadlessTrace
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 3 || !int.TryParse(args[1], out var ticks) || ticks < 1 || ticks > 1_000_000)
        {
            Console.Error.WriteLine("Usage: --headless-trace <ticks:1..1000000> [trace.ndjson]");
            return 2;
        }
        var host = SessionComposition.CreateReferenceHost();
        var run = host.Enqueue(new("operator-1"), new("trace-run", 1, new RunCommand()));
        host.Pump();
        if (run.GetAwaiter().GetResult() is not AcceptedMessage) return 1;
        using var writer = args.Length == 3 ? new StreamWriter(args[2], false, new UTF8Encoding(false)) : null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var options = WireJson.CreateOptions();
        var arrivals = 0;
        var departures = 0;
        var junctions = 0;
        var moves = 0;
        for (var i = 0; i < ticks; i++)
        {
            var batch = host.Pump(1);
            var frame = JsonSerializer.Serialize(new { state = host.ReadState(), events = batch.Events }, options);
            hash.AppendData(Encoding.UTF8.GetBytes(frame + "\n"));
            writer?.Write(frame + "\n");
            arrivals += batch.Events.Count(x => x.Kind == "stationArrived");
            departures += batch.Events.Count(x => x.Kind == "stationDeparted");
            junctions += batch.Events.Count(x => x.Kind == "junctionStraight");
            moves += batch.Events.Count(x => x.Kind == "moved");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { scenario = "reference-v1", ticks, arrivals, departures, junctions, moves,
            sha256 = Convert.ToHexStringLower(hash.GetHashAndReset()) }));
        return 0;
    }
}
