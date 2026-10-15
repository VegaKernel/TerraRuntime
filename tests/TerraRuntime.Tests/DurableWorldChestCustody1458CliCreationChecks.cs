using System.Net;
using System.Net.Sockets;
using TerraRuntime.Application;
using TerraRuntime.Application.Diagnostics;
using TerraRuntime.Application.Operations;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Tests.DurableWorldChestCustody;

internal static class CliNewWorldProof
{
    internal static int Run(string directory)
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        string path = Path.Combine(directory, "new-cli.wld");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var stop = new StopAfterAttach();
        string[] args = ["--create-world", "Durable CLI", "--world-generator", "terraruntime:flat", "--world-seed", "7", "--world-width", "128", "--world-height", "96", "--world-output", path, "--bind", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--no-tui"];
        int result = StartupProgram.Run(args, hostLifecycle: stop);
        Check(result == 29 && stop.Attached, "actual CLI newly generated world reaches trusted host attachment without an import flag");
        Check(File.Exists(path) && File.Exists(Path.Combine(path + ".custody", "managed-v1.anchor")), "actual no-overwrite generation establishes managed world custody");
        var logs = new RuntimeHostLog(new RuntimeLogBuffer(), TextWriter.Null, TextWriter.Null, new RuntimeHostLoggingOptions { ConsoleEnabled = false, JsonLinesEnabled = false });
        try
        {
            var loaded = WorldStartupPreparation.PrepareAsync(new ServerHostOptions(path, port, 1, TerminalUiEnabled: false), logs).GetAwaiter().GetResult();
            Check(loaded.Status == WorldStartupPreparationStatus.Ready && loaded.Startup?.ManagedCustody is not null, "generated world ordinary restart needs no repeated import");
            loaded.Startup!.ManagedCustody!.Dispose();
        }
        finally { logs.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        byte[] before = File.ReadAllBytes(path);
        var blocked = new StopAfterAttach();
        Check(StartupProgram.Run(args, hostLifecycle: blocked) == 25 && !blocked.Attached, "create-world does not overwrite or reauthorize an existing world");
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), "denied create leaves canonical generation unchanged");
        return checks;
    }

    private sealed class StopAfterAttach : ILifecycle
    {
        internal bool Attached;
        public ValueTask AttachRuntimeAsync(IRuntime runtime, CancellationToken cancellationToken = default)
        {
            Attached = true;
            throw new InvalidOperationException("Configured end of CLI creation proof after actual trusted host attachment");
        }
        public ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
