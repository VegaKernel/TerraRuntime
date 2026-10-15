using System.Diagnostics;
using System.Security.Cryptography;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Application.Diagnostics;
using TerraRuntime.Application.Operations;
using TerraRuntime.World;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests.DurableWorldChestCustody;

internal static class DurableChestCustodyChecks
{
    private static int checks;
    private static readonly WorldFileLoadLimits Limits = WorldFixture.CreateLimits();
    private static WorldFileData source = null!;
    private static WorldFilePreservedSections preserved = null!;
    private static WorldTileSaveImage tiles = null!;
    private static readonly WorldChest[] Chests = [new(0, 0, 0, "custody", [new(3, 9, 0), new(3, 72, 0), default])];

    internal static int Run()
    {
        checks = 0;
        RunSuite();
        return checks;
    }

    private static void RunSuite()
    {
        byte[] original = WorldFixture.CreateCompleteCurrentWorld();
        source = Load(original);
        Check(WorldFilePreservedSections.TryCapture(original, source.Envelope, out var captured), "preserved sections");
        preserved = captured!;
        var shadow = new WorldTileSaveShadowSynchronizer(source.Tiles, 1);
        Check(shadow.CaptureBootstrap(1) == 1 && shadow.TryCaptureImage(out _), "bounded tile image");
        shadow.TryCaptureImage(out var image); tiles = image!;
        string directory = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", "DurableChestCustody", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "canonical.wld");
        File.WriteAllBytes(path, World(source.Envelope.Revision));
        var initial = Load(File.ReadAllBytes(path));
        var lease = WorldPersistenceOwnerLease.Acquire(path);
        lease.InitializeRevision(initial.Envelope.Revision);
        var first = lease.ReserveNextRevision();
        var second = lease.ReserveNextRevision();
        var third = lease.ReserveNextRevision();
        Check(second.Value == first.Value + 1 && third.Value == second.Value + 1, "unique snapshot revisions");
        Throws<IOException>(() => WorldPersistenceOwnerLease.Acquire(Path.Combine(directory, ".", "canonical.wld")), "same-process lease");
        bool refusedOtherThread = false;
        var otherThread = new Thread(() =>
        {
            try { lease.ReserveNextRevision(); }
            catch (InvalidOperationException) { refusedOtherThread = true; }
        });
        otherThread.Start();
        Check(otherThread.Join(TimeSpan.FromSeconds(10)) && refusedOtherThread, "owner-thread revision reservation");
        using (var write = lease.EnterCheckpointWrite())
        {
            Throws<InvalidOperationException>(() => lease.Dispose(), "cannot dispose during publication");
            Throws<InvalidOperationException>(() => lease.EnterCheckpointWrite(), "cannot overlap writers");
        }
        var artifacts = new RuntimeWorldChestCustodyArtifacts(lease, Limits, 1_000_000);
        Throws<InvalidDataException>(() => artifacts.InitializeTrustedImportAsync(initial, false).GetAwaiter().GetResult(), "explicit import policy");
        artifacts.InitializeTrustedImportAsync(initial, true).GetAwaiter().GetResult();
        var imported = artifacts.ReadManagedAsync(initial).GetAwaiter().GetResult();
        Check(imported.Chests[0].Quantity[0] == 3 && imported.Chests[0].Copper[1] == 300, "trusted initial stock");
        var a = Snapshot(first, 1, 1);
        var b = Snapshot(second, 0, 0);
        var before = File.ReadAllBytes(path);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RuntimeWorldCustodyCheckpoint current = a;
        var coordinator = new WorldSaveCoordinator<RuntimeWorldCustodyCheckpoint>(path, () => current,
            (snapshot, stream, token) => stream.WriteAsync(World(snapshot.Revision.Value), token).AsTask(),
            new(BackupPath: path + ".bak", ValidateCandidateAsync: (candidate, token) => { _ = Load(File.ReadAllBytes(candidate)); return Task.CompletedTask; }),
            _ => { artifacts.PruneCommittedAsync().GetAwaiter().GetResult(); committed.TrySetResult(); },
            async (snapshot, candidate, token) =>
            {
                await artifacts.PrepareCandidateAsync(snapshot, candidate, token);
                if (snapshot.Revision.Value == first.Value) { stopped.SetResult(); await release.Task.WaitAsync(token); }
            }, _ => lease.EnterCheckpointWrite());
        coordinator.RequestSave();
        stopped.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), "artifact prepared before canonical publication");
        Check(File.Exists(artifacts.ArtifactPath(first.Value, SHA256.HashData(World(first.Value)))), "immutable dependency already durable");
        string conflictPath = Path.Combine(directory, "same-generation-candidate.wld");
        File.WriteAllBytes(conflictPath, World(first.Value));
        Throws<InvalidDataException>(() => artifacts.PrepareCandidateAsync(Snapshot(first, 0, 0), conflictPath, default).GetAwaiter().GetResult(), "same generation different custody cannot overwrite");
        Throws<InvalidOperationException>(() => lease.Dispose(), "lease held across full coordinator publication");
        release.SetResult();
        committed.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        var worldA = Load(File.ReadAllBytes(path));
        var savedA = artifacts.ReadManagedAsync(worldA).GetAwaiter().GetResult();
        Check(savedA.Chests[0].Quantity[0] == 1 && savedA.Chests[0].Copper[1] == 1, "partial exact generation A");
        current = b;
        coordinator.RequestSave();
        coordinator.CompleteAsync().GetAwaiter().GetResult();
        coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        var worldB = Load(File.ReadAllBytes(path));
        var savedB = artifacts.ReadManagedAsync(worldB).GetAwaiter().GetResult();
        Check(savedB.Chests[0].Quantity[0] == 0 && savedB.Chests[0].Copper[1] == 0, "same physical changed custody B");
        Check(worldA.Chests[0].Items.AsSpan().SequenceEqual(worldB.Chests[0].Items), "physical projection unchanged");
        Check(worldA.Envelope.Revision != worldB.Envelope.Revision, "canonical generations distinguish custody-only change");
        Check(!SHA256.HashData(World(first.Value)).AsSpan().SequenceEqual(SHA256.HashData(World(second.Value))), "whole world hash changes with revision");
        Check(File.ReadAllBytes(path + ".bak").AsSpan().SequenceEqual(World(first.Value)), "backup exact previous generation");
        RetentionChecks(directory);
        CodecChecks(savedB, worldB);
        StoreChecks(savedB, worldB);
        Check(Directory.GetFiles(path + ".custody", "generation-*.bin").Length == 2, "bounded retention keeps canonical and backup only");
        Failure(path, lease, artifacts, Snapshot(third, 0, 0), afterArtifact: false);
        Check(!File.Exists(artifacts.ArtifactPath(third.Value, SHA256.HashData(World(third.Value)))), "failure before preparation leaves no dependency");
        Failure(path, lease, artifacts, Snapshot(third, 0, 0), afterArtifact: true);
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(World(second.Value)), "failed candidate preserves canonical pair");
        File.Copy(path + ".bak", path, overwrite: true);
        Check(artifacts.ReadManagedAsync(Load(File.ReadAllBytes(path))).GetAwaiter().GetResult().Chests[0].Quantity[0] == 1, "backup restores matching partial custody");
        File.WriteAllBytes(path, World(third.Value + 1));
        Throws<FileNotFoundException>(() => artifacts.ReadManagedAsync(Load(File.ReadAllBytes(path))).GetAwaiter().GetResult(), "missing authoritative generation refuses");
        Throws<InvalidDataException>(() => artifacts.InitializeTrustedImportAsync(Load(File.ReadAllBytes(path)), true).GetAwaiter().GetResult(), "managed missing companion cannot reset full import");
        lease.Dispose();
        using var saturated = WorldPersistenceOwnerLease.Acquire(Path.Combine(directory, "saturated.wld"));
        saturated.InitializeRevision(uint.MaxValue);
        Throws<InvalidOperationException>(() => saturated.ValidateRevision(in first), "foreign lifetime revision rejected");
        Throws<InvalidOperationException>(() => saturated.ReserveNextRevision(), "revision saturation fails closed");
        HostChecks(directory);
        ConfiguredRecoveryMarkerChecks(directory);
        checks += CliNewWorldProof.Run(directory);
        checks += PartialWorldTransferProof.Run(directory);
    }

    private static void Failure(string path, WorldPersistenceOwnerLease owner, RuntimeWorldChestCustodyArtifacts artifacts,
        RuntimeWorldCustodyCheckpoint checkpoint, bool afterArtifact)
    {
        var failed = new WorldSaveCoordinator<RuntimeWorldCustodyCheckpoint>(path, () => checkpoint,
            (snapshot, stream, token) => stream.WriteAsync(World(snapshot.Revision.Value), token).AsTask(), null, null,
            async (snapshot, candidate, token) =>
            {
                if (afterArtifact) await artifacts.PrepareCandidateAsync(snapshot, candidate, token);
                throw new IOException("Configured interruption before canonical replacement");
            }, _ => owner.EnterCheckpointWrite());
        failed.RequestSave();
        Throws<IOException>(() => failed.CompleteAsync().GetAwaiter().GetResult(), "failure propagated");
        Throws<IOException>(() => failed.DisposeAsync().AsTask().GetAwaiter().GetResult(), "failure retained on dispose");
        Check(failed.CaptureSnapshot().FailedWrites == 1, "failed write accounted once");
    }

    // Configured abandoned transactions use the production marker writer. The independent
    // crash-process evidence separately proves the actual after-marker interruption.
    private static void ConfiguredRecoveryMarkerChecks(string root)
    {
        foreach (string mode in new[] { "valid", "missing", "corrupt" })
        {
            string directory = Path.Combine(root, "configured-recovery-" + mode);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "canonical.wld");
            byte[] raw = WorldFixture.CreateCompleteCurrentWorld(100, 100);
            var limits = WorldFixture.CreateLimits() with { MaxTileCount = 10_000 };
            Check(WorldFileLoader.TryLoad(raw, limits, out var basis).IsLoaded, "recovery fixture load");
            Check(WorldFilePreservedSections.TryCapture(raw, basis!.Envelope, out var sections), "recovery preserved sections");
            var shadow = new WorldTileSaveShadowSynchronizer(basis.Tiles, 1);
            while (!shadow.IsBootstrapped) shadow.CaptureBootstrap(1);
            Check(shadow.TryCaptureImage(out var tileImage), "recovery tile image");
            byte[] Rewrite(uint revision)
            {
                using var output = new MemoryStream();
                Check(WorldFileTileChestRewriter.TryRewrite(basis.Envelope.WithRevision(revision), basis.Header,
                    sections!, tileImage!, Chests, output, out _) == WorldFileTileChestRewriteResult.Rewritten,
                    "recovery complete world rewrite");
                return output.ToArray();
            }
            byte[] before = Rewrite(basis.Envelope.Revision);
            File.WriteAllBytes(path, before);
            string candidate = Path.Combine(directory, ".canonical.wld." + Guid.NewGuid().ToString("N") + ".tmp");
            string artifact;
            byte[] candidateBytes;
            using (var owner = WorldPersistenceOwnerLease.Acquire(path))
            {
                owner.InitializeRevision(basis.Envelope.Revision);
                var artifacts = new RuntimeWorldChestCustodyArtifacts(owner, limits, 1_000_000);
                Check(WorldFileLoader.TryLoad(before, limits, out var loaded).IsLoaded, "recovery original load");
                artifacts.InitializeTrustedImportAsync(loaded!, true).GetAwaiter().GetResult();
                var revision = owner.ReserveNextRevision();
                candidateBytes = Rewrite(revision.Value);
                Check(WorldFileLoader.TryLoad(candidateBytes, limits, out var next).IsLoaded, "recovery candidate load");
                var custody = RuntimeWorldChestCustodySnapshot.FullImport(next!);
                custody.Chests[0].Quantity[0] = 1;
                custody.Chests[0].Copper[1] = 1;
                File.WriteAllBytes(candidate, candidateBytes);
                using (owner.EnterCheckpointWrite())
                    artifacts.PrepareCandidateAsync(new(custody, revision), candidate, default).GetAwaiter().GetResult();
                artifact = artifacts.ArtifactPath(revision.Value, SHA256.HashData(candidateBytes));
                File.WriteAllBytes(candidate + ".lease", []);
                File.Copy(path, path + ".bak");
                AtomicSaveFileWriter.WriteRecoveryMarkerForTestingAsync(candidate + ".recovery", candidate, path + ".bak").GetAwaiter().GetResult();
            }
            byte[] originalArtifact = File.ReadAllBytes(artifact);
            if (mode == "missing") File.Delete(artifact);
            if (mode == "corrupt")
            {
                byte[] corrupt = originalArtifact.ToArray();
                corrupt[^1] ^= 1;
                File.WriteAllBytes(artifact, corrupt);
            }
            var logs = new RuntimeHostLog(new RuntimeLogBuffer(), TextWriter.Null, TextWriter.Null,
                new RuntimeHostLoggingOptions { JsonLinesEnabled = false, ConsoleEnabled = false });
            try
            {
                var options = new ServerHostOptions(path, 18572, 1, TerminalUiEnabled: false);
                var outcome = WorldStartupPreparation.PrepareAsync(options, logs).GetAwaiter().GetResult();
                if (mode != "valid")
                {
                    Check(outcome.Status == WorldStartupPreparationStatus.Failed, "unpaired recovery refuses startup");
                    Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), "unpaired marker preserves old canonical");
                    Check(File.Exists(candidate) && File.Exists(candidate + ".recovery") && File.Exists(candidate + ".lease"),
                        "unpaired recovery preserves retry transaction");
                    Check(!File.Exists(candidate + ".recovery-conflict"), "custody rejection is recoverable");
                    File.WriteAllBytes(artifact, originalArtifact);
                    outcome = WorldStartupPreparation.PrepareAsync(options, logs).GetAwaiter().GetResult();
                }
                try
                {
                    Check(outcome.Status == WorldStartupPreparationStatus.Ready && outcome.Startup?.ManagedCustody is not null,
                        "paired marker publishes and starts");
                    Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(candidateBytes), "exact marker candidate published");
                    Check(outcome.Startup!.ManagedCustody!.Loaded.Chests[0].Quantity[0] == 1 &&
                        outcome.Startup.ManagedCustody.Loaded.Chests[0].Copper[1] == 1, "recovered partial custody exact");
                }
                finally { outcome.Startup?.ManagedCustody?.Dispose(); }
            }
            finally { logs.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
    }

    private static void HostChecks(string directory)
    {
        string path = Path.Combine(directory, "host-managed.wld");
        byte[] original = WorldFixture.CreateCompleteCurrentWorld(100, 100);
        var limits = WorldFixture.CreateLimits() with { MaxTileCount = 10_000 };
        Check(WorldFileLoader.TryLoad(original, limits, out var basis).IsLoaded && basis is not null, "host fixture complete load");
        Check(WorldFilePreservedSections.TryCapture(original, basis!.Envelope, out var saved), "host fixture preserved sections");
        var shadow = new WorldTileSaveShadowSynchronizer(basis.Tiles, 1);
        while (!shadow.IsBootstrapped) shadow.CaptureBootstrap(1);
        Check(shadow.TryCaptureImage(out var image), "host fixture tile image");
        using (var stream = new MemoryStream())
        {
            var stock = new WorldChest(0, 20, 20, "host stock", [new(3, 9, 0), new(3, 72, 0), default]);
            Check(WorldFileTileChestRewriter.TryRewrite(basis.Envelope, basis.Header, saved!, image!, [stock], stream, out _) == WorldFileTileChestRewriteResult.Rewritten, "host world file constructed");
            File.WriteAllBytes(path, stream.ToArray());
        }
        var logs = new RuntimeHostLog(new RuntimeLogBuffer(), TextWriter.Null, TextWriter.Null,
            new RuntimeHostLoggingOptions { JsonLinesEnabled = false, ConsoleEnabled = false });
        try
        {
            var options = new ServerHostOptions(path, 18570, 1, TerminalUiEnabled: false);
            Check(!options.ImportInitialWorldCustody, "managed default requires explicit initial import");
            var denied = WorldStartupPreparation.PrepareAsync(options, logs).GetAwaiter().GetResult();
            Check(denied.Status == WorldStartupPreparationStatus.Failed, "host refuses implicit full import");
            Check(ServerHostOptions.TryParse(["--world", path, "--import-world-custody"], out var parsed, out _) && parsed!.ImportInitialWorldCustody, "operator import flag parsed explicitly");
            checks += SandboxCustodyChecks.Unmanaged(path);
            var first = PrepareHost(options with { ImportInitialWorldCustody = true }, logs);
            checks += SandboxCustodyChecks.Managed(path, 3, 300, false);
            Check(WorldStartupPreparation.PrepareAsync(options, logs).GetAwaiter().GetResult().Status == WorldStartupPreparationStatus.Failed, "second startup cannot recover or reopen live world");
            Check(first.ManagedCustody is not null && first.ManagedCustody.Loaded.Chests[0].Quantity[0] == 3, "host explicit trusted initial import");
            uint initialRevision = first.World.Envelope.Revision;
            RunHostGeneration(first, path, 1, 1);
            first.ManagedCustody!.Dispose();
            var second = PrepareHost(options, logs);
            Check(second.Metrics.RuntimeCacheHit, "managed check applies after actual runtime cache hit");
            Check(second.ManagedCustody!.Loaded.Chests[0].Quantity[0] == 1 && second.ManagedCustody.Loaded.Chests[0].Copper[1] == 1, "host restart retains partial custody");
            checks += SandboxCustodyChecks.Managed(path, 1, 1, false);
            uint partialRevision = second.World.Envelope.Revision;
            Check(partialRevision > initialRevision, "host serializes reserved checkpoint revision");
            var physical = second.World.Chests[0].Items.ToArray();
            RunHostGeneration(second, path, 0, 0);
            second.ManagedCustody.Dispose();
            var third = PrepareHost(options, logs);
            Check(third.ManagedCustody!.Loaded.Chests[0].Quantity[0] == 0 && third.ManagedCustody.Loaded.Chests[0].Copper[1] == 0, "host restart keeps q0, not full stock");
            checks += SandboxCustodyChecks.Managed(path, 0, 0, true);
            Check(third.World.Chests[0].Items.AsSpan().SequenceEqual(physical), "host same physical image across custody change");
            Check(third.World.Envelope.Revision > partialRevision, "custody-only host save advances canonical generation");
            string artifact = third.ManagedCustody.Artifacts.ArtifactPath(third.World.Envelope.Revision, SHA256.HashData(File.ReadAllBytes(path)));
            third.ManagedCustody.Dispose();
            byte[] validArtifact = File.ReadAllBytes(artifact);
            byte[] corruptArtifact = validArtifact.ToArray(); corruptArtifact[^1] ^= 1;
            File.WriteAllBytes(artifact, corruptArtifact);
            Check(WorldStartupPreparation.PrepareAsync(options, logs).GetAwaiter().GetResult().Status == WorldStartupPreparationStatus.Failed, "actual host rejects corrupt companion integrity");
            File.WriteAllBytes(artifact, validArtifact);
            File.Move(artifact, artifact + ".held");
            Check(WorldStartupPreparation.PrepareAsync(options, logs).GetAwaiter().GetResult().Status == WorldStartupPreparationStatus.Failed, "cache hit with missing companion refuses host");
            Check(WorldStartupPreparation.PrepareAsync(options with { ImportInitialWorldCustody = true }, logs).GetAwaiter().GetResult().Status == WorldStartupPreparationStatus.Failed, "explicit import cannot refill a managed missing companion");
            File.Move(artifact + ".held", artifact);
            byte[] corrupted = File.ReadAllBytes(path); corrupted[^1] ^= 1;
            File.WriteAllBytes(path, corrupted);
            var recovery = WorldStartupPreparation.PrepareAsync(options, logs).GetAwaiter().GetResult();
            Check(recovery.Status == WorldStartupPreparationStatus.RestartAfterRecovery, "host recovers only valid world and custody backup pair");
            var recovered = PrepareHost(options, logs);
            Check(recovered.ManagedCustody!.Loaded.Chests[0].Quantity[0] == 0, "backup recovery retains exact q0 budget");
            recovered.ManagedCustody.Dispose();
            byte[] backup = File.ReadAllBytes(path + ".bak");
            Check(WorldFileLoader.TryLoad(backup, limits, out var backupWorld).IsLoaded, "backup complete world");
            string backupArtifact = Path.Combine(path + ".custody", $"generation-{backupWorld!.Envelope.Revision:X8}-{Convert.ToHexString(SHA256.HashData(backup))}.bin");
            File.Move(backupArtifact, backupArtifact + ".held");
            File.WriteAllBytes(path, corrupted);
            Check(WorldStartupPreparation.PrepareAsync(options, logs).GetAwaiter().GetResult().Status == WorldStartupPreparationStatus.Failed, "missing backup custody refuses recovery");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(corrupted), "failed recovery never publishes an unpaired backup");
        }
        finally { logs.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    internal static void PersistPartialStore(RuntimeChestStore store, WorldTileStore liveTiles, WorldChest[] before,
        string path, int expectedQuantity, long expectedCopper)
    {
        byte[] original = WorldFixture.CreateCompleteCurrentWorld(100, 100);
        var limits = WorldFixture.CreateLimits() with { MaxTileCount = 10_000 };
        Check(WorldFileLoader.TryLoad(original, limits, out var world).IsLoaded && world is not null, "source transfer persistence fixture load");
        Check(WorldFilePreservedSections.TryCapture(original, world!.Envelope, out var preserved), "source transfer persistence sections");
        var shadow = new WorldTileSaveShadowSynchronizer(liveTiles, 1);
        while (!shadow.IsBootstrapped) shadow.CaptureBootstrap(1);
        Check(shadow.TryCaptureImage(out var tiles), "source transfer live tiles captured");
        using (var stream = new MemoryStream())
        {
            Check(WorldFileTileChestRewriter.TryRewrite(world.Envelope, world.Header, preserved!, tiles!, before, stream, out _) == WorldFileTileChestRewriteResult.Rewritten, "source transfer initial world constructed");
            File.WriteAllBytes(path, stream.ToArray());
        }
        var logs = new RuntimeHostLog(new RuntimeLogBuffer(), TextWriter.Null, TextWriter.Null,
            new RuntimeHostLoggingOptions { JsonLinesEnabled = false, ConsoleEnabled = false });
        try
        {
            var options = new ServerHostOptions(path, 18571, 1, TerminalUiEnabled: false);
            var startup = PrepareHost(options with { ImportInitialWorldCustody = true }, logs);
            using (var owner = startup.ManagedCustody!)
            {
                var coordinator = new RuntimeWorldCheckpointCoordinator(path, startup.World.Envelope, startup.World.Header,
                    startup.SaveTemplate, liveTiles, store, checkpointValidationLimits: startup.LoadLimits, managedCustody: owner);
                coordinator.RequestSave();
                for (int count = 0; count < 64 && coordinator.CaptureStatus().AcceptedSnapshots == 0; count++) coordinator.Tick();
                Check(coordinator.CaptureStatus().AcceptedSnapshots == 1, "source transfer actual owned snapshot queued");
                coordinator.CompleteAsync().GetAwaiter().GetResult();
                coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Check(coordinator.CaptureStatus().CompletedWrites == 1, "source transfer canonical checkpoint committed once");
            }
            var restart = PrepareHost(options, logs);
            using (var owner = restart.ManagedCustody!)
            {
                var saved = owner.Loaded.Chests[0];
                Check(saved.Chest.Items.AsSpan().SequenceEqual(store.CaptureSnapshot()[0].Items), "source transfer restart full physical image");
                Check(saved.Quantity[0] == expectedQuantity && saved.Copper[0] == expectedCopper, "source transfer restart exact private custody");
            }
        }
        finally { logs.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static PreparedWorldStartup PrepareHost(ServerHostOptions options, RuntimeHostLog logs)
    {
        var result = WorldStartupPreparation.PrepareAsync(options, logs).GetAwaiter().GetResult();
        Check(result.Status == WorldStartupPreparationStatus.Ready && result.Startup?.ManagedCustody is not null,
            $"actual host startup ready {result.Status}/{result.ExitCode}");
        return result.Startup!;
    }

    private static void RunHostGeneration(PreparedWorldStartup startup, string path, short quantity, long copper)
    {
        var identity = new WorldRuntimeIdentity(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew());
        using var runtime = new WorldRuntime(identity, new SandboxWorldSource.WorldFile(path), startup.World,
            startup.BootstrapPackets, new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = 1 },
            new WorldRuntimePersistence(path, startup.SaveTemplate, startup.LoadLimits, startup.ManagedCustody));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        Check(runtime.Chests.SupportsDurableCustody && runtime.Chests.TryOpen(connection, 20, 20, out _), "actual host composes managed chest owner");
        Check(runtime.Chests.TryCaptureOwnedTransfer(connection, startup.World.Tiles, out var transfer) && transfer is not null, "configured owner before loop start");
        var quantities = transfer!.Quantity.ToArray(); var coins = transfer.Copper.ToArray();
        quantities[0] = quantity; coins[1] = copper;
        Check(transfer.TryAdoptUnpublished(transfer.Before, quantities, coins), "configured prevalidated custody-only owner adoption before start");
        var forged = new TerrariaChestItemState(0, 0, 999, 0, 9);
        Check(!runtime.Chests.TrySetItem(connection, in forged, out _), "host raw32 cannot mint physical stock");
        runtime.Start();
        Check(runtime.TryRequestSave(), "actual runtime requests save");
        long started = Stopwatch.GetTimestamp();
        while ((runtime.CaptureSaveStatus()?.CompletedWrites ?? 0) == 0 && runtime.GameLoop.Fault is null && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(20)) Thread.Sleep(10);
        Check(runtime.GameLoop.Fault is null && runtime.CaptureSaveStatus()?.CompletedWrites > 0, "actual owner-loop snapshot commits");
        Check(runtime.StopAsync(TimeSpan.FromSeconds(10), captureFinalSave: true).GetAwaiter().GetResult(), "actual shutdown final snapshot handoff");
        Check(startup.ManagedCustody!.RetentionFailures == 0, "host background retention success");
    }

    private static void RetentionChecks(string directory)
    {
        string path = Path.Combine(directory, "retention.wld");
        File.WriteAllBytes(path, World(source.Envelope.Revision));
        using var owner = WorldPersistenceOwnerLease.Acquire(path);
        owner.InitializeRevision(source.Envelope.Revision);
        var artifacts = new RuntimeWorldChestCustodyArtifacts(owner, Limits, 1_000_000);
        artifacts.InitializeTrustedImportAsync(Load(File.ReadAllBytes(path)), true).GetAwaiter().GetResult();
        for (int index = 0; index < 8; index++)
        {
            var revision = owner.ReserveNextRevision();
            var checkpoint = Snapshot(revision, (short)(index % 2), index % 2);
            using var write = owner.EnterCheckpointWrite();
            AtomicSaveFileWriter.WriteAsync(path, (stream, token) => stream.WriteAsync(World(revision.Value), token).AsTask(),
                new AtomicSaveFileWriterOptions(BackupPath: path + ".bak", ValidateCandidateAsync: (candidate, token) => artifacts.PrepareCandidateAsync(checkpoint, candidate, token))).GetAwaiter().GetResult();
            artifacts.PruneCommittedAsync().GetAwaiter().GetResult();
            Check(Directory.GetFiles(path + ".custody", "generation-*.bin").Length == 2, "retention bounded across repeated checkpoints");
            var loaded = artifacts.ReadManagedAsync(Load(File.ReadAllBytes(path))).GetAwaiter().GetResult();
            Check(loaded.Chests[0].Quantity[0] == index % 2, "retention preserves exact current generation");
        }
        File.WriteAllBytes(Path.Combine(path + ".custody", "generation-unknown.bin"), [1]);
        using (owner.EnterCheckpointWrite())
            Throws<InvalidDataException>(() => artifacts.PruneCommittedAsync().GetAwaiter().GetResult(), "unclassified artifact not silently deleted");
        Check(File.Exists(Path.Combine(path + ".custody", "generation-unknown.bin")), "foreign artifact preserved on refusal");
    }

    private static void StoreChecks(RuntimeWorldChestCustodySnapshot custody, WorldFileData world)
    {
        var store = new RuntimeChestStore(world.Chests, custody);
        Check(store.SupportsDurableCustody && store.OwnsLoadedWorldStock, "managed slot owner mode");
        var capture = store.CaptureCustodySnapshot(world.Header.UniqueId, world.Header.WorldId, world.Envelope.Revision);
        Check(capture.Chests[0].Quantity[0] == 0 && capture.Chests[0].Copper[1] == 0, "loaded partial and zero budgets retained");
        capture.Chests[0].Quantity[0] = 3;
        Check(store.CaptureCustodySnapshot(world.Header.UniqueId, world.Header.WorldId, 1).Chests[0].Quantity[0] == 0, "snapshot privately detached");
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        Check(store.TryOpen(connection, 0, 0, out _), "actual world owner opens managed chest");
        var forged = new TerrariaChestItemState(0, 0, 999, 0, 9);
        Check(!store.TrySetItem(connection, in forged, out _), "raw32 cannot fund physical restore");
        Check(store.TryCaptureOwnedTransfer(connection, world.Tiles, out var transfer) && transfer is not null, "existing transfer capture");
        Check(transfer!.Quantity[0] == 0 && transfer.Copper[1] == 0 && transfer.IsCurrent, "capture carries actual private beforeimage");
        short[] quantity = transfer.Quantity.ToArray(); long[] copper = transfer.Copper.ToArray();
        quantity[0] = 1; copper[1] = 1;
        Check(transfer.TryAdoptUnpublished(transfer.Before, quantity, copper), "configured prevalidated private-only adoption");
        Check(transfer.IsAcceptedImageCurrent, "accepted image includes private custody");
        var after = store.CaptureCustodySnapshot(world.Header.UniqueId, world.Header.WorldId, world.Envelope.Revision);
        Check(after.Chests[0].Quantity[0] == 1 && after.Chests[0].Copper[1] == 1, "owner checkpoint captures exact partial tuple");
        var reloaded = new RuntimeChestStore(world.Chests, after);
        Check(reloaded.CaptureCustodySnapshot(world.Header.UniqueId, world.Header.WorldId, 1).Chests[0].Quantity[0] == 1, "managed reload never full defaults");
        var request = new TerrariaCraftingRequest1458([new(9, 1)], [0]);
        for (int use = 0; use < 2; use++)
        {
            var prepared = reloaded.PrepareCrafting(connection, request, world.Tiles, 8);
            int work = 0;
            foreach (int cost in prepared.Prepare()) work += cost;
            Check(work <= 512 && prepared.Accepted, "bounded physical material preparation");
            Check(prepared.TryCaptureFundedMaterialLegs(out var legs) && legs.Length == 1 && legs[0].FundedQuantity == (use == 0 ? 1 : 0), "remote cost carries actual partial source quantity");
            Check(prepared.TryAdoptUnpublished(), "physical material consumption adopts once");
            var spent = reloaded.CaptureCustodySnapshot(world.Header.UniqueId, world.Header.WorldId, 1);
            Check(spent.Chests[0].Quantity[0] == 0 && spent.Chests[0].Chest.Items[0].Stack == 2 - use, "exact debit cannot renew paid material");
        }
        var invalid = after with { Chests = [] };
        Throws<InvalidDataException>(() => new RuntimeChestStore(world.Chests, invalid), "missing slot coverage refuses store import");
    }

    private static void CodecChecks(RuntimeWorldChestCustodySnapshot snapshot, WorldFileData world)
    {
        byte[] hash = SHA256.HashData(World(world.Envelope.Revision));
        byte[] encoded = RuntimeWorldChestCustodyCodec.Encode(snapshot, hash);
        byte[] changed = encoded.ToArray(); changed[^1] ^= 1;
        Throws<InvalidDataException>(() => RuntimeWorldChestCustodyCodec.Decode(changed, world, hash), "corrupt custody checksum");
        Throws<InvalidDataException>(() => RuntimeWorldChestCustodyCodec.Decode(encoded.AsSpan(0, encoded.Length - 1), world, hash), "truncated custody");
        byte[] wrongHash = hash.ToArray(); wrongHash[0] ^= 1;
        Throws<InvalidDataException>(() => RuntimeWorldChestCustodyCodec.Decode(encoded, world, wrongHash), "wrong world hash");
        Throws<InvalidDataException>(() => RuntimeWorldChestCustodyCodec.Decode(encoded, Load(World(world.Envelope.Revision + 1)), hash), "wrong revision");
        var invalid = snapshot with { Chests = [new(snapshot.Chests[0].Chest, [4, 0, 0], [0, 0, 0])] };
        Throws<InvalidDataException>(() => RuntimeWorldChestCustodyCodec.Encode(invalid, hash), "funding cannot exceed physical stock");
    }

    private static RuntimeWorldCustodyCheckpoint Snapshot(WorldPersistenceOwnerLease.Revision revision, short quantity, long copper)
    {
        var full = RuntimeWorldChestCustodySnapshot.FullImport(Load(World(revision.Value)));
        full.Chests[0].Quantity[0] = quantity; full.Chests[0].Copper[1] = copper;
        return new(full, revision);
    }

    private static byte[] World(uint revision)
    {
        var envelope = new WorldFileEnvelope(source.Envelope.FormatVersion, revision, source.Envelope.FavoriteFlags,
            source.Envelope.SectionOffsets.ToArray(), source.Envelope.FrameImportanceCount, source.Envelope.FrameImportanceBits.ToArray());
        using var stream = new MemoryStream();
        Check(WorldFileTileChestRewriter.TryRewrite(envelope, source.Header, preserved, tiles, Chests, stream, out _) == WorldFileTileChestRewriteResult.Rewritten, "complete world rewrite");
        return stream.ToArray();
    }

    private static WorldFileData Load(byte[] bytes)
    {
        var result = WorldFileLoader.TryLoad(bytes, Limits, out var world);
        Check(result.IsLoaded && world is not null, $"complete world load {result}");
        return world!;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { checks++; return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + message);
    }
}
