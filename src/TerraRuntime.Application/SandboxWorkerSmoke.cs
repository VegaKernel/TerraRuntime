using System.Diagnostics;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Application;

/// <summary>Exercises the actual self-contained worker executable, including source-generated IPC under NativeAOT.</summary>
internal static class SandboxWorkerSmoke
{
    internal const string Option = "--sandbox-worker-smoke";

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!args.Contains(Option, StringComparer.Ordinal)) return false;
        if (args.Length != 1) { exitCode = 66; return true; }
        try { RunAsync().GetAwaiter().GetResult(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
            OperationCanceledException or ArgumentException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Sandbox worker smoke failed: {exception.GetType().Name}.");
            exitCode = 66;
        }
        return true;
    }

    private static async Task RunAsync()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Worker smoke requires the application executable.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using SandboxSupervisor first = await SandboxSupervisor.StartAsync(executable, Descriptor(), deadline.Token);
        await using SandboxSupervisor second = await SandboxSupervisor.StartAsync(executable, Descriptor(), deadline.Token);
        long initialTick = second.Snapshot.Tick;
        using (Process child = Process.GetProcessById(first.Snapshot.ProcessId))
        {
            child.Kill();
            await child.WaitForExitAsync(deadline.Token);
        }
        bool observedCrash = false;
        try { _ = await first.ProbeAsync(deadline.Token); }
        catch (IOException) { observedCrash = true; }
        await Task.Delay(100, deadline.Token);
        SandboxWorkerSnapshot live = await second.ProbeAsync(deadline.Token);
        if (!observedCrash || live.Tick <= initialTick || live.TargetTicksPerSecond != 60 ||
            !await second.StopAsync(deadline.Token) || second.Fault is not null)
            throw new InvalidOperationException("Worker isolation/stop smoke assertion failed.");
        Console.WriteLine("Sandbox worker smoke passed: authenticated processes, 60 TPS, crash isolation, graceful stop.");
    }

    private static SandboxWorkerDescriptor Descriptor() => new(Guid.NewGuid(), Guid.NewGuid(),
        SandboxWorkerSourceKind.Generated, "terraruntime:flat", "Worker smoke", 1458, 300, 200,
        WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption, null, null, 4);
}
