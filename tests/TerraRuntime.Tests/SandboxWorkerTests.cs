using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Transport;

namespace TerraRuntime.Tests;

[CollectionDefinition("Sandbox worker process boundary", DisableParallelization = true)]
public sealed class SandboxWorkerProcessCollection { }

[Collection("Sandbox worker process boundary")]
public sealed class SandboxWorkerTests
{
    [Fact]
    public async Task Real_worker_runs_shared_world_runtime_at_60_tps_and_stops_without_reconnect_listener()
    {
        SandboxWorkerDescriptor descriptor = Generated();
        await using SandboxSupervisor worker = await SandboxSupervisor.StartAsync(Executable(), descriptor, TestContext.Current.CancellationToken);
        SandboxWorkerSnapshot ready = worker.Snapshot;
        Assert.NotEqual(Environment.ProcessId, ready.ProcessId);
        Assert.Equal(descriptor.RuntimeId, ready.RuntimeId);
        Assert.Equal(descriptor.SessionId, ready.SessionId);
        Assert.Equal(60, ready.TargetTicksPerSecond);
        Assert.Equal(WorldRuntimeLifecycle.Running, ready.Lifecycle);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        SandboxWorkerSnapshot later = await worker.ProbeAsync(TestContext.Current.CancellationToken);
        Assert.True(later.Tick > ready.Tick);
        Assert.True(await worker.StopAsync(TestContext.Current.CancellationToken));
        Assert.Equal(WorldRuntimeLifecycle.Stopped, worker.Snapshot.Lifecycle);
        Assert.Null(worker.Fault);
    }

    [Fact]
    public async Task Worker_crash_does_not_stop_a_separate_runtime_worker()
    {
        await using SandboxSupervisor first = await SandboxSupervisor.StartAsync(Executable(), Generated(), TestContext.Current.CancellationToken);
        await using SandboxSupervisor second = await SandboxSupervisor.StartAsync(Executable(), Generated(), TestContext.Current.CancellationToken);
        long before = second.Snapshot.Tick;
        using (Process ownedChild = Process.GetProcessById(first.Snapshot.ProcessId))
        {
            ownedChild.Kill();
            await ownedChild.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        await Assert.ThrowsAnyAsync<IOException>(() => first.ProbeAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(first.Fault);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True((await second.ProbeAsync(TestContext.Current.CancellationToken)).Tick > before);
        Assert.True(await second.StopAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repeated_create_dispose_retires_each_exact_child_and_boot_identity()
    {
        var boots = new HashSet<Guid>();
        for (int i = 0; i < 3; i++)
        {
            SandboxSupervisor worker = await SandboxSupervisor.StartAsync(Executable(), Generated(), TestContext.Current.CancellationToken);
            Assert.True(boots.Add(worker.Snapshot.ProcessInstanceId));
            using Process child = Process.GetProcessById(worker.Snapshot.ProcessId);
            await worker.DisposeAsync();
            Assert.True(child.HasExited);
            Assert.Equal(WorldRuntimeLifecycle.Stopped, worker.Snapshot.Lifecycle);
            Assert.Null(worker.Fault);
        }
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("identity")]
    [InlineData("players")]
    [InlineData("size")]
    [InlineData("seed")]
    public void Worker_descriptor_rejects_unknown_or_unbounded_inputs(string defect)
    {
        SandboxWorkerDescriptor source = Generated();
        SandboxWorkerDescriptor invalid = defect switch
        {
            "kind" => source with { SourceKind = (SandboxWorkerSourceKind)255 },
            "identity" => source with { SessionId = Guid.Empty },
            "players" => source with { MaxPlayers = 256 },
            "size" => source with { WidthTiles = int.MaxValue, HeightTiles = int.MaxValue },
            _ => source with { SeedText = new string('a', 1025) }
        };
        Assert.ThrowsAny<Exception>(() => SandboxWorkerProtocol.Encode(invalid));
    }

    [Fact]
    public async Task Failed_materialization_never_reports_ready()
    {
        await Assert.ThrowsAnyAsync<IOException>(() => SandboxSupervisor.StartAsync(Executable(),
            Generated() with { Source = "terraruntime:unknown-generator" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Child_exit_before_pipe_connection_is_reported_before_the_startup_deadline()
    {
        // The test apphost rejects private server-worker arguments and exits before opening the pipe.
        string nonWorker = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "TerraRuntime.Tests.exe" : "TerraRuntime.Tests");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<IOException>(() => SandboxSupervisor.StartAsync(nonWorker, Generated(), deadline.Token));
        Assert.False(deadline.IsCancellationRequested);
    }

    [Fact]
    public async Task Completed_startup_race_does_not_abandon_unobserved_pipe_faults()
    {
        // This collection does not overlap other tests: the process-wide finalizer hook is diagnostic only.
        int unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            if (args.Exception.Flatten().InnerExceptions.Any(static error =>
                error is IOException && error.StackTrace?.Contains("System.IO.Pipes", StringComparison.Ordinal) == true))
                Interlocked.Increment(ref unobserved);
        };
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            for (int i = 0; i < 3; i++)
                await Child_exit_before_pipe_connection_is_reported_before_the_startup_deadline();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Equal(0, Volatile.Read(ref unobserved));
        }
        finally { TaskScheduler.UnobservedTaskException -= handler; }
    }

    [Fact]
    public async Task Canceled_stop_retires_its_owned_child_instead_of_leaving_an_unmanaged_worker()
    {
        await using SandboxSupervisor worker = await SandboxSupervisor.StartAsync(Executable(), Generated(), TestContext.Current.CancellationToken);
        using Process child = Process.GetProcessById(worker.Snapshot.ProcessId);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.False(await worker.StopAsync(canceled.Token));
        Assert.True(child.HasExited);
        Assert.IsAssignableFrom<OperationCanceledException>(worker.Fault);
    }

    [Fact]
    public async Task Heartbeat_keeps_a_runtime_alive_without_foreground_probes_and_stops_cleanly()
    {
        await using SandboxSupervisor worker = await SandboxSupervisor.StartAsync(Executable(), Generated(), TestContext.Current.CancellationToken);
        long initial = worker.Snapshot.Tick;
        await Task.Delay(2500, TestContext.Current.CancellationToken);
        Assert.True(worker.Snapshot.Tick > initial);
        Assert.True(await worker.StopAsync(TestContext.Current.CancellationToken));
        Assert.Null(worker.Fault);
    }

    [Fact]
    public void Unknown_module_fields_are_not_silently_accepted_as_runtime_only()
    {
        string json = Encoding.UTF8.GetString(SandboxWorkerProtocol.Encode(Generated()));
        json = json[..^1] + ",\"PluginPackage\":\"untrusted.dll\"}";
        Assert.Throws<System.Text.Json.JsonException>(() => SandboxWorkerProtocol.DecodeDescriptor(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Authentication_binds_secret_challenge_pid_and_boot_without_sending_secret()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        byte[] challenge = RandomNumberGenerator.GetBytes(32);
        Guid boot = Guid.NewGuid();
        byte[] proof = SandboxWorkerProtocol.CreateWorkerProof(secret, challenge, boot, 1234);
        Assert.Equal(boot, SandboxWorkerProtocol.ValidateWorkerProof(secret, challenge, proof, 1234));
        Assert.Throws<InvalidDataException>(() => SandboxWorkerProtocol.ValidateWorkerProof(secret, challenge, proof, 1235));
        byte[] replayChallenge = (byte[])challenge.Clone();
        replayChallenge[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => SandboxWorkerProtocol.ValidateWorkerProof(secret, replayChallenge, proof, 1234));
        secret[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => SandboxWorkerProtocol.ValidateWorkerProof(secret, challenge, proof, 1234));
    }

    [Fact]
    public async Task Oversized_control_header_is_rejected_before_any_payload_read()
    {
        byte[] bytes = new byte[32];
        // Independent wire bytes: TRPC / v1 / Request / flags0 / payload8193 / Create / correlation.
        "TRPC"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
        bytes[6] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8193);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 2);
        Guid.NewGuid().TryWriteBytes(bytes.AsSpan(16));
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await BoundedControlFrame.ReadAsync(stream, 8192, TestContext.Current.CancellationToken));
        Assert.Equal(32, stream.Position);
    }

    [Fact]
    public async Task Control_stream_rejects_unknown_flags_and_truncated_payload()
    {
        using var stream = new MemoryStream();
        await BoundedControlFrame.WriteAsync(stream, MessageKind.Request, 2, Guid.NewGuid(), new byte[3], 8192,
            TestContext.Current.CancellationToken);
        byte[] valid = stream.ToArray();
        byte[] unknownFlags = (byte[])valid.Clone();
        unknownFlags[7] = 1;
        using var badFlags = new MemoryStream(unknownFlags);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await BoundedControlFrame.ReadAsync(badFlags, 8192, TestContext.Current.CancellationToken));
        using var truncated = new MemoryStream(valid[..^1]);
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await BoundedControlFrame.ReadAsync(truncated, 8192, TestContext.Current.CancellationToken));
    }

    private static string Executable() => Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "TerraRuntime.Server.exe" : "TerraRuntime.Server");

    private static SandboxWorkerDescriptor Generated() => new(Guid.NewGuid(), Guid.NewGuid(),
        SandboxWorkerSourceKind.Generated, "terraruntime:flat", "Isolated worker", 42, 300, 200,
        WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption, null, null, 4);
}
