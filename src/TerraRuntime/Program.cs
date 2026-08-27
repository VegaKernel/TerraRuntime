using TerraRuntime.Core;

namespace TerraRuntime;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--loop-smoke", StringComparer.Ordinal))
        {
            return RunLoopSmoke();
        }

        Console.WriteLine("TerraRuntime .NET 11 runtime scaffold. Use --loop-smoke to exercise the authoritative game loop.");
        return 0;
    }

    private static int RunLoopSmoke()
    {
        var state = new ServerRuntimeState();
        using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());

        loop.Start();
        if (!loop.TryPost(new ProbeCommand()))
        {
            Console.Error.WriteLine("Failed to enqueue loop smoke command.");
            return 2;
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (loop.Snapshot.Tick < 3 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        loop.Stop(TimeSpan.FromSeconds(1));
        var snapshot = loop.Snapshot;
        if (loop.Fault is not null || snapshot.Tick < 3)
        {
            Console.Error.WriteLine($"Game loop smoke failed: tick={snapshot.Tick}, fault={loop.Fault}");
            return 3;
        }

        Console.WriteLine($"Game loop smoke passed: tick={snapshot.Tick}, thread={snapshot.GameThreadId}, worst={snapshot.WorstTickMilliseconds:F3} ms");
        return 0;
    }
}
