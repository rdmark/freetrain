using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FreeTrain.Contracts;

namespace FreeTrain.Server;

public static class HeadlessProfile
{
    public static int Run()
    {
        var host = SessionComposition.CreateReferenceHost();
        host.Enqueue(new("operator-1"), new("profile-run", 1, new RunCommand())); host.Pump();
        for (var i = 0; i < 1000; i++) host.Pump(1);
        var durations = new double[10000];
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime; var allocated = GC.GetTotalAllocatedBytes(true);
        for (var i = 0; i < durations.Length; i++)
        {
            var start = Stopwatch.GetTimestamp(); host.Pump(1);
            durations[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
        Array.Sort(durations);
        var read = host.ReadState();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scope = "headless SessionHost.Pump(1); 1000 warmup + 10000 measured ticks; no JSON/network/disk",
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription, ticks = durations.Length,
            tickMs = new { p50 = durations[4999], p95 = durations[9499], p99 = durations[9899], max = durations[^1] },
            cpuMs, allocatedBytes = bytes, peakWorkingSetBytes = process.PeakWorkingSet64,
            scene = new { read.Map.Width, read.Map.Height, tracks = read.Tracks.Length, trains = read.Trains.Length, stations = read.Stations.Length, buildings = read.Buildings.Length },
        }));
        return 0;
    }
}
