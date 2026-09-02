using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;
using System.Security.Cryptography;

namespace TerraRuntime.Tests;

public sealed class Level1SandboxRuntimeTests
{
    [Fact]
    public async Task Two_world_runtimes_tick_independently_and_do_not_share_mutable_world_state()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 1);
        WorldRuntime sandbox = CreateRuntime("Arena", seed: 2);

        Assert.NotSame(primary.World.Tiles, sandbox.World.Tiles);
        Assert.NotSame(primary.Npcs, sandbox.Npcs);
        Assert.NotSame(primary.Projectiles, sandbox.Projectiles);
        Assert.NotSame(primary.WorldItems, sandbox.WorldItems);
        Assert.NotSame(primary.NpcReplication, sandbox.NpcReplication);
        Assert.NotSame(primary.State.WorldItemSpawnRandom, sandbox.State.WorldItemSpawnRandom);

        RuntimeWorldProgressionMutations primaryProgression = primary.WorldProgression;
        RuntimeWorldProgressionMutations sandboxProgression = sandbox.WorldProgression;
        Assert.NotSame(primaryProgression, sandboxProgression);
        Assert.True(sandboxProgression.MarkCompleted(VanillaWorldProgressionId.EyeOfCthulhu));
        Assert.False(primaryProgression.IsCompleted(VanillaWorldProgressionId.EyeOfCthulhu));

        using var registry = new WorldRegistry(capacity: 4);
        Assert.True(registry.TryAdmit(primary, primary: true));
        Assert.True(registry.TryAdmit(sandbox));
        await WaitForTicksAsync(primary, 3);
        await WaitForTicksAsync(sandbox, 3);

        Assert.Equal(2, registry.Count);
        Assert.NotEqual(primary.GameLoop.Snapshot.GameThreadId, sandbox.GameLoop.Snapshot.GameThreadId);

        NpcSnapshot primaryNpc = await SpawnNpcAsync(primary, slot: 7, type: 1);
        NpcSnapshot sandboxNpc = await SpawnNpcAsync(sandbox, slot: 7, type: 1);
        Assert.Equal((ulong)1, primaryNpc.Handle.Generation.Value);
        Assert.Equal((ulong)1, sandboxNpc.Handle.Generation.Value);
        Assert.Equal(1, primary.Npcs.ActiveCount);
        Assert.Equal(1, sandbox.Npcs.ActiveCount);
        Assert.NotSame(primary.Npcs, sandbox.Npcs);
    }

    [Fact]
    public async Task Generated_sandbox_materializes_on_bounded_worker_while_primary_keeps_ticking()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 3);
        using var registry = new WorldRegistry(capacity: 4);
        Assert.True(registry.TryAdmit(primary, primary: true));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var generators = new TestGeneratorSource(new BlockingFlatGenerator(entered, release));
        using var sandboxes = new SandboxHost(
            registry,
            generators,
            ServerWorldLoadPolicy.CreateLimits(),
            materializationConcurrency: 1,
            pendingJobCapacity: 2);

        var source = new SandboxWorldSource.Generated(
            BlockingFlatGenerator.GeneratorId,
            "Background",
            Seed: 10,
            WidthTiles: 32,
            HeightTiles: 24,
            WorldGenerationOptions.Default);
        Assert.True(sandboxes.TryCreate(
            new SandboxCreateRequest(new SandboxName("background"), WorldIsolationLevel.InProcess, source),
            out SandboxJobId jobId,
            out string? error), error);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        long before = primary.CaptureSnapshot().Tick;
        await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
        long after = primary.CaptureSnapshot().Tick;
        Assert.True(after > before, $"Primary tick did not advance while generation was blocked: {before} -> {after}.");
        Assert.Equal(1, registry.Count);

        release.Set();
        SandboxJobSnapshot completed = await sandboxes.WaitForJobAsync(
            jobId,
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        Assert.Equal(SandboxJobStatus.Completed, completed.Status);
        Assert.Equal(2, registry.Count);
        Assert.True(sandboxes.TryGetSandbox(new SandboxName("background"), out SandboxSnapshot sandbox));
        await WaitForTicksAsync(registry, sandbox.Runtime.Identity.RuntimeId, sandbox.Runtime.Tick + 2);
    }

    [Fact]
    public async Task Failed_generation_never_admits_a_half_live_sandbox()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 4);
        using var registry = new WorldRegistry(capacity: 3);
        Assert.True(registry.TryAdmit(primary, primary: true));
        var generators = new TestGeneratorSource(new ThrowingGenerator());
        using var sandboxes = new SandboxHost(
            registry,
            generators,
            ServerWorldLoadPolicy.CreateLimits());
        var notification = new TaskCompletionSource<SandboxJobSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        sandboxes.JobFinished += snapshot => notification.TrySetResult(snapshot);
        var source = new SandboxWorldSource.Generated(
            ThrowingGenerator.GeneratorId,
            "Broken",
            Seed: 1,
            WidthTiles: 16,
            HeightTiles: 12,
            WorldGenerationOptions.Default);

        Assert.True(sandboxes.TryCreate(
            new SandboxCreateRequest(new SandboxName("broken"), WorldIsolationLevel.InProcess, source),
            out SandboxJobId jobId,
            out string? error), error);
        SandboxJobSnapshot failed = await sandboxes.WaitForJobAsync(
            jobId,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(SandboxJobStatus.Failed, failed.Status);
        SandboxJobSnapshot published = await notification.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(failed.Id, published.Id);
        Assert.Equal(SandboxJobStatus.Failed, published.Status);
        Assert.False(string.IsNullOrWhiteSpace(published.Error));
        Assert.Equal(1, registry.Count);
        Assert.Empty(sandboxes.CaptureSandboxes());
    }

    [Fact]
    public void Unknown_generator_is_rejected_before_a_background_job_is_accepted()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 41);
        using var registry = new WorldRegistry(capacity: 3);
        Assert.True(registry.TryAdmit(primary, primary: true));
        using var sandboxes = new SandboxHost(
            registry,
            BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits());
        var source = new SandboxWorldSource.Generated(
            new WorldGeneratorId("missing:generator"),
            "Missing",
            Seed: 1,
            WidthTiles: 32,
            HeightTiles: 24,
            WorldGenerationOptions.Default);

        Assert.False(sandboxes.TryCreate(
            new SandboxCreateRequest(new SandboxName("missing"), WorldIsolationLevel.InProcess, source),
            out SandboxJobId jobId,
            out string? error));

        Assert.False(jobId.IsAssigned);
        Assert.Contains("not registered", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sandboxes.CaptureJobs());
        Assert.Equal(1, registry.Count);

        var operations = new SandboxOperations(
            sandboxes,
            Path.GetTempPath(),
            defaultWidthTiles: 32,
            defaultHeightTiles: 24);
        string feedback = operations.Execute("sb command_error gen missing:generator");
        Assert.Contains("missing:generator", feedback, StringComparison.Ordinal);
        Assert.Contains("not registered", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accepted", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Canceled_generation_releases_the_name_without_admitting_a_runtime()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 40);
        using var registry = new WorldRegistry(capacity: 3);
        Assert.True(registry.TryAdmit(primary, primary: true));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var sandboxes = new SandboxHost(
            registry,
            new TestGeneratorSource(new BlockingFlatGenerator(entered, release)),
            ServerWorldLoadPolicy.CreateLimits());
        var name = new SandboxName("cancel_me");

        Assert.True(sandboxes.TryCreate(
            new SandboxCreateRequest(
                name,
                WorldIsolationLevel.InProcess,
                new SandboxWorldSource.Generated(
                    BlockingFlatGenerator.GeneratorId,
                    "Canceled",
                    Seed: 1,
                    WidthTiles: 32,
                    HeightTiles: 24,
                    WorldGenerationOptions.Default)),
            out SandboxJobId jobId,
            out string? createError), createError);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.False(sandboxes.TryCreate(
            new SandboxCreateRequest(name, WorldIsolationLevel.InProcess, FlatSource("Duplicate", 2)),
            out _,
            out string? duplicateError));
        Assert.Contains("already exists", duplicateError, StringComparison.OrdinalIgnoreCase);

        Assert.True(sandboxes.TryCancel(jobId));
        SandboxJobSnapshot canceled = await sandboxes.WaitForJobAsync(
            jobId,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(SandboxJobStatus.Canceled, canceled.Status);
        Assert.Equal(1, registry.Count);
        Assert.Empty(sandboxes.CaptureSandboxes());
        release.Set();
    }

    [Fact]
    public async Task Regeneration_preserves_runtime_id_rotates_session_and_failed_regen_keeps_old_session()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 5);
        using var registry = new WorldRegistry(capacity: 4);
        Assert.True(registry.TryAdmit(primary, primary: true));
        var switchable = new SwitchableFlatGenerator();
        using var sandboxes = new SandboxHost(
            registry,
            new TestGeneratorSource(switchable),
            ServerWorldLoadPolicy.CreateLimits());
        var name = new SandboxName("regen");
        var source = new SandboxWorldSource.Generated(
            SwitchableFlatGenerator.GeneratorId,
            "Regen",
            Seed: 11,
            WidthTiles: 24,
            HeightTiles: 16,
            WorldGenerationOptions.Default);

        Assert.True(sandboxes.TryCreate(
            new SandboxCreateRequest(name, WorldIsolationLevel.InProcess, source),
            out SandboxJobId createId,
            out string? createError), createError);
        Assert.Equal(
            SandboxJobStatus.Completed,
            (await sandboxes.WaitForJobAsync(createId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Status);
        Assert.True(sandboxes.TryGetSandbox(name, out SandboxSnapshot first));

        Assert.True(sandboxes.TryRegenerate(name, replacementSeed: 12, out SandboxJobId regenId, out string? regenError), regenError);
        Assert.Equal(
            SandboxJobStatus.Completed,
            (await sandboxes.WaitForJobAsync(regenId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Status);
        Assert.True(sandboxes.TryGetSandbox(name, out SandboxSnapshot second));
        Assert.Equal(first.Runtime.Identity.RuntimeId, second.Runtime.Identity.RuntimeId);
        Assert.NotEqual(first.Runtime.Identity.SessionId, second.Runtime.Identity.SessionId);

        switchable.Fail = true;
        Assert.True(sandboxes.TryRegenerate(name, replacementSeed: 13, out SandboxJobId failedId, out string? failedError), failedError);
        Assert.Equal(
            SandboxJobStatus.Failed,
            (await sandboxes.WaitForJobAsync(failedId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Status);
        Assert.True(sandboxes.TryGetSandbox(name, out SandboxSnapshot afterFailure));
        Assert.Equal(second.Runtime.Identity, afterFailure.Runtime.Identity);
        Assert.Equal(WorldRuntimeLifecycle.Running, afterFailure.Runtime.Lifecycle);
    }

    [Fact]
    public async Task Repeated_create_destroy_releases_runtime_registrations()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 6);
        using var registry = new WorldRegistry(capacity: 3);
        Assert.True(registry.TryAdmit(primary, primary: true));
        using var sandboxes = new SandboxHost(
            registry,
            BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits());

        for (int i = 0; i < 3; i++)
        {
            var name = new SandboxName($"cycle_{i}");
            var source = FlatSource($"Cycle{i}", (ulong)i);
            Assert.True(sandboxes.TryCreate(
                new SandboxCreateRequest(name, WorldIsolationLevel.InProcess, source),
                out SandboxJobId createId,
                out string? createError), createError);
            Assert.Equal(
                SandboxJobStatus.Completed,
                (await sandboxes.WaitForJobAsync(createId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Status);
            Assert.Equal(2, registry.Count);

            Assert.True(sandboxes.TryDestroy(name, out SandboxJobId destroyId, out string? destroyError), destroyError);
            Assert.Equal(
                SandboxJobStatus.Completed,
                (await sandboxes.WaitForJobAsync(destroyId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Status);
            Assert.Equal(1, registry.Count);
            Assert.Empty(sandboxes.CaptureSandboxes());
        }
    }

    [Fact]
    public async Task Completed_job_history_is_bounded()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 60);
        using var registry = new WorldRegistry(capacity: 3);
        Assert.True(registry.TryAdmit(primary, primary: true));
        using var sandboxes = new SandboxHost(
            registry,
            BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits(),
            retainedJobCapacity: 2);

        for (int i = 0; i < 3; i++)
        {
            var name = new SandboxName($"history_{i}");
            Assert.True(sandboxes.TryCreate(
                new SandboxCreateRequest(name, WorldIsolationLevel.InProcess, FlatSource($"History{i}", (ulong)i)),
                out SandboxJobId createId,
                out string? createError), createError);
            Assert.Equal(
                SandboxJobStatus.Completed,
                (await sandboxes.WaitForJobAsync(createId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Status);
            Assert.True(sandboxes.TryDestroy(name, out SandboxJobId destroyId, out string? destroyError), destroyError);
            Assert.Equal(
                SandboxJobStatus.Completed,
                (await sandboxes.WaitForJobAsync(destroyId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Status);
        }

        await WaitUntilAsync(() => sandboxes.CaptureJobs().Length <= 2);
        Assert.InRange(sandboxes.CaptureJobs().Length, 1, 2);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public async Task World_file_source_uses_the_same_background_admission_path()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-L1-{Guid.NewGuid():N}.wld");
        try
        {
            await File.WriteAllBytesAsync(
                path,
                CreateFlatWorldBytes("FileArena", seed: 77),
                TestContext.Current.CancellationToken);
            WorldRuntime primary = CreateRuntime("Primary", seed: 7);
            using var registry = new WorldRegistry(capacity: 3);
            Assert.True(registry.TryAdmit(primary, primary: true));
            using var sandboxes = new SandboxHost(
                registry,
                BuiltInWorldGeneratorSource.Instance,
                ServerWorldLoadPolicy.CreateLimits());
            var name = new SandboxName("file_arena");

            Assert.True(sandboxes.TryCreate(
                new SandboxCreateRequest(
                    name,
                    WorldIsolationLevel.InProcess,
                    new SandboxWorldSource.WorldFile(path)),
                out SandboxJobId jobId,
                out string? error), error);
            SandboxJobSnapshot completed = await sandboxes.WaitForJobAsync(
                jobId,
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

            Assert.Equal(SandboxJobStatus.Completed, completed.Status);
            Assert.True(sandboxes.TryGetSandbox(name, out SandboxSnapshot sandbox));
            Assert.Equal("FileArena", sandbox.Runtime.WorldName);
            Assert.IsType<SandboxWorldSource.WorldFile>(sandbox.Runtime.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sandbox_command_parser_uses_primary_dimensions_and_accepts_debug_generation_options()
    {
        string root = Path.Combine(Path.GetTempPath(), "TerraRuntimeSandboxAssets");
        var parser = new SandboxCommandParser(root, defaultWidthTiles: 8400, defaultHeightTiles: 2400);

        Assert.True(parser.TryParse("sb1 arena gen flat", out SandboxOperation? defaultOperation, out string? defaultError), defaultError);
        SandboxOperation.Create defaultCreate = Assert.IsType<SandboxOperation.Create>(defaultOperation);
        SandboxWorldSource.Generated defaultSource = Assert.IsType<SandboxWorldSource.Generated>(defaultCreate.Request.Source);
        Assert.Equal(WorldIsolationLevel.InProcess, defaultCreate.Request.IsolationLevel);
        Assert.Equal("arena", defaultSource.WorldName);
        Assert.Equal(8400, defaultSource.WidthTiles);
        Assert.Equal(2400, defaultSource.HeightTiles);

        Assert.True(parser.TryParse(
            "sb2 bossrush gen flat seed 42 size 4200x1200 mode master evil crimson",
            out SandboxOperation? configuredOperation,
            out string? configuredError), configuredError);
        SandboxOperation.Create configuredCreate = Assert.IsType<SandboxOperation.Create>(configuredOperation);
        SandboxWorldSource.Generated configuredSource = Assert.IsType<SandboxWorldSource.Generated>(configuredCreate.Request.Source);
        Assert.Equal(WorldIsolationLevel.DedicatedProcess, configuredCreate.Request.IsolationLevel);
        Assert.Equal((ulong)42, configuredSource.Seed);
        Assert.Equal(4200, configuredSource.WidthTiles);
        Assert.Equal(1200, configuredSource.HeightTiles);
        Assert.Equal(WorldGenerationGameMode.Master, configuredSource.Options.GameMode);
        Assert.Equal(WorldGenerationEvil.Crimson, configuredSource.Options.Evil);

        Assert.True(parser.TryParse(
            "sb1 mirror gen flat size primary",
            out SandboxOperation? primarySizeOperation,
            out string? primarySizeError), primarySizeError);
        SandboxWorldSource.Generated primarySize = Assert.IsType<SandboxWorldSource.Generated>(
            Assert.IsType<SandboxOperation.Create>(primarySizeOperation).Request.Source);
        Assert.Equal(8400, primarySize.WidthTiles);
        Assert.Equal(2400, primarySize.HeightTiles);

        Assert.True(parser.TryParse("sandbox jobs", out SandboxOperation? jobs, out string? jobsError), jobsError);
        Assert.IsType<SandboxOperation.Jobs>(jobs);
        Assert.True(parser.TryParse("sb job 42", out SandboxOperation? job, out string? jobError), jobError);
        Assert.Equal(new SandboxJobId(42), Assert.IsType<SandboxOperation.Job>(job).Id);
        Assert.False(parser.TryParse("sandbox create old l1 gen flat", out _, out _));
        Assert.True(parser.TryParse("respawn Alice primary", out SandboxOperation? respawn, out string? respawnError), respawnError);
        Assert.IsType<SandboxOperation.Respawn>(respawn);
    }

    [Fact]
    public async Task Sandbox_operations_expose_registered_generators_and_create_an_in_process_world()
    {
        WorldRuntime primary = CreateRuntime("Primary", seed: 45);
        using var registry = new WorldRegistry(capacity: 3);
        Assert.True(registry.TryAdmit(primary, primary: true));
        using var sandboxes = new SandboxHost(
            registry,
            BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits());
        var operations = new SandboxOperations(
            sandboxes,
            Path.GetTempPath(),
            defaultWidthTiles: 32,
            defaultHeightTiles: 24);

        string[] generators = operations.CaptureWorldGeneratorIds().Select(static id => id.Value).ToArray();
        Assert.Contains("terraruntime:flat", generators);
        Assert.Contains("terraruntime:optimized", generators);
        Assert.Contains("terraruntime:vanilla", generators);
        Assert.Contains("terraruntime:skyblock", generators);

        Assert.True(operations.TryBuildGeneratedCreate(
            "form_inprocess",
            WorldIsolationLevel.InProcess,
            "terraruntime:flat",
            "42",
            widthTiles: 32,
            heightTiles: 24,
            WorldGenerationGameMode.Classic,
            WorldGenerationEvil.Corruption,
            out SandboxOperation.Create? operation,
            out string? buildError), buildError);
        Assert.NotNull(operation);

        string feedback = operations.Execute(operation);
        Assert.Contains("accepted as operation", feedback, StringComparison.OrdinalIgnoreCase);
        SandboxJobSnapshot queued = Assert.Single(sandboxes.CaptureJobs());
        SandboxJobSnapshot completed = await sandboxes.WaitForJobAsync(
            queued.Id,
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.Equal(SandboxJobStatus.Completed, completed.Status);
        Assert.True(sandboxes.TryGetSandbox(new SandboxName("form_inprocess"), out SandboxSnapshot sandbox));
        Assert.Equal(WorldIsolationLevel.InProcess, operation.Request.IsolationLevel);
        Assert.Equal("form_inprocess", sandbox.Name.Value);
    }

    [Fact]
    public void Sandbox_command_parser_rejects_absolute_and_parent_world_paths()
    {
        string root = Path.Combine(Path.GetTempPath(), "TerraRuntimeSandboxAssets");
        var parser = new SandboxCommandParser(root, defaultWidthTiles: 8400, defaultHeightTiles: 2400);

        Assert.False(parser.TryParse(
            "sb1 arena file ../outside.wld",
            out _,
            out string? traversalError));
        Assert.Contains("escapes", traversalError, StringComparison.OrdinalIgnoreCase);

        string absolute = Path.Combine(Path.GetPathRoot(root)!, "outside.wld");
        Assert.False(parser.TryParse(
            $"sb1 arena file {absolute}",
            out _,
            out string? absoluteError));
        Assert.Contains("relative", absoluteError, StringComparison.OrdinalIgnoreCase);

        Assert.False(parser.TryParse(
            "sb1 arena file plugin.dll",
            out _,
            out string? libraryError));
        Assert.Contains(".wld", libraryError, StringComparison.OrdinalIgnoreCase);

        Assert.False(parser.TryParse(
            "sb1 arena file tool.exe",
            out _,
            out string? executableError));
        Assert.Contains(".wld", executableError, StringComparison.OrdinalIgnoreCase);
    }

    private static WorldRuntime CreateRuntime(string name, ulong seed)
    {
        SandboxWorldSource.Generated source = FlatSource(name, seed);
        var materializer = new SandboxWorldMaterializer(
            BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits());
        SandboxWorldMaterializationResult result = materializer.Materialize(source, CancellationToken.None);
        Assert.True(result.Succeeded, result.Error);
        return new WorldRuntime(
            new WorldRuntimeIdentity(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()),
            source,
            result.World!,
            result.Bootstrap!,
            new InterestManagementControl(),
            new WorldRuntimeOptions { MaxPlayers = 4 });
    }

    private static SandboxWorldSource.Generated FlatSource(string name, ulong seed) =>
        new(
            FlatWorldGenerationProvider.GeneratorId,
            name,
            seed,
            WidthTiles: 32,
            HeightTiles: 24,
            WorldGenerationOptions.Default);

    private static byte[] CreateFlatWorldBytes(string name, ulong seed)
    {
        SandboxWorldSource.Generated source = FlatSource(name, seed);
        WorldGenerationRequest request = source.ToRequest();
        RuntimeWorldCreationPipelineResult created = new RuntimeWorldCreationPipeline(
            BuiltInWorldGeneratorSource.Instance).CreateCandidate(in request);
        Assert.True(created.Succeeded);
        Assert.NotNull(created.Candidate);
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            name,
            seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.WidthTiles,
            request.HeightTiles,
            Guid.NewGuid(),
            RandomNumberGenerator.GetInt32(1, int.MaxValue));
        long now = DateTime.UtcNow.ToBinary();
        WorldFileFreshCompose326Diagnostic composition = WorldFileFreshComposer326.TryCompose(
            header,
            created.Metadata,
            created.Candidate!.TileStore,
            created.Candidate.CaptureGeneratedChests(),
            created.Candidate.CaptureGeneratedNpcs(),
            gameMode: (byte)request.Options.GameMode,
            crimson: request.Options.Evil == WorldGenerationEvil.Crimson,
            creationTimeBinary: now,
            lastPlayedBinary: now,
            out byte[] bytes);
        Assert.True(composition.Succeeded, composition.ToString());
        return bytes;
    }

    private static async Task WaitForTicksAsync(WorldRuntime runtime, long minimumTick)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (runtime.CaptureSnapshot().Tick < minimumTick)
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
                throw new TimeoutException("World runtime did not tick.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitForTicksAsync(WorldRegistry registry, WorldRuntimeId id, long minimumTick)
    {
        Assert.True(registry.TryGet(id, out WorldRuntime? runtime));
        await WaitForTicksAsync(runtime!, minimumTick);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!predicate())
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Condition did not become true.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<NpcSnapshot> SpawnNpcAsync(WorldRuntime runtime, byte slot, int type)
    {
        var completion = new TaskCompletionSource<NpcSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var update = new NpcStateUpdate(
            Type: type,
            NetId: checked((short)type),
            PositionX: 32f,
            PositionY: 32f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: ushort.MaxValue,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        Assert.True(runtime.GameLoop.TryPost(new NpcSpawnRuntimeCommand(slot, update, completion)));
        NpcSnapshot? spawned = await completion.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.True(spawned.HasValue);
        return spawned.Value;
    }

    private sealed class TestGeneratorSource(IWorldGenerationProvider provider) : ITerraRuntimeWorldGeneratorSource
    {
        private readonly WorldGeneratorId[] ids = [provider.Id];

        public ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds() => ids;

        public bool TryResolveWorldGenerator(WorldGeneratorId id, out IWorldGenerationProvider? resolved)
        {
            resolved = id == provider.Id ? provider : null;
            return resolved is not null;
        }
    }

    private sealed class BlockingFlatGenerator(
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : IWorldGenerationProvider
    {
        public static readonly WorldGeneratorId GeneratorId = new("tests:blocking-flat");
        public WorldGeneratorId Id => GeneratorId;

        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
        {
            builder.Add(
                new WorldGenerationPassDescriptor(new WorldGenerationPassId("tests:block")),
                new BlockingPass(entered, release));
            new FlatWorldGenerationProvider().BuildPlan(in request, builder);
        }

        private sealed class BlockingPass(
            ManualResetEventSlim entered,
            ManualResetEventSlim release) : IWorldGenerationPass
        {
            public void Execute(IWorldGenerationContext context)
            {
                entered.Set();
                release.Wait(context.CancellationToken);
            }
        }
    }

    private sealed class ThrowingGenerator : IWorldGenerationProvider
    {
        public static readonly WorldGeneratorId GeneratorId = new("tests:throwing");
        public WorldGeneratorId Id => GeneratorId;

        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            builder.Add(
                new WorldGenerationPassDescriptor(new WorldGenerationPassId("tests:throw")),
                ThrowingPass.Instance);

        private sealed class ThrowingPass : IWorldGenerationPass
        {
            public static ThrowingPass Instance { get; } = new();
            public void Execute(IWorldGenerationContext context) => throw new InvalidOperationException("expected failure");
        }
    }

    private sealed class SwitchableFlatGenerator : IWorldGenerationProvider
    {
        public static readonly WorldGeneratorId GeneratorId = new("tests:switchable-flat");
        public WorldGeneratorId Id => GeneratorId;
        public bool Fail { get; set; }

        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
        {
            if (Fail)
            {
                builder.Add(
                    new WorldGenerationPassDescriptor(new WorldGenerationPassId("tests:switch-fail")),
                    SwitchFailPass.Instance);
                return;
            }
            new FlatWorldGenerationProvider().BuildPlan(in request, builder);
        }

        private sealed class SwitchFailPass : IWorldGenerationPass
        {
            public static SwitchFailPass Instance { get; } = new();
            public void Execute(IWorldGenerationContext context) => throw new InvalidOperationException("regen failure");
        }
    }
}
