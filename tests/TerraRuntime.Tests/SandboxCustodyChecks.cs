using System.Security.Cryptography;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests.DurableWorldChestCustody;

internal static class SandboxCustodyChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Sandbox custody: " + message);
    }
    private static SandboxWorldMaterializationResult Load(string path, bool trust = false, byte[]? hash = null) =>
        new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance, ServerWorldLoadPolicy.CreateLimits())
            .Materialize(new SandboxWorldSource.WorldFile(path) { TrustInitialVanillaStock = trust },
                CancellationToken.None, hash ?? []);

    internal static int Unmanaged(string path)
    {
        int before = checks;
        string copy = path + ".sandbox-template.wld";
        File.Copy(path, copy);
        Check(!Load(copy).Succeeded, "unmanaged file requires explicit import");
        var imported = Load(copy, true);
        Check(imported.Succeeded && imported.Custody!.Chests[0].Quantity[0] == 3 &&
            imported.Custody.Chests[0].Copper[1] == 300, "explicit vanilla template import remains full");
        Check(!Directory.Exists(copy + ".custody"), "readonly import writes no source artifacts");
        Directory.CreateDirectory(copy + ".custody");
        Check(!Load(copy, true).Succeeded, "even empty managed directory prevents full fallback");
        return checks - before;
    }

    internal static int Managed(string path, int quantity, long copper, bool failures)
    {
        int before = checks;
        byte[] original = File.ReadAllBytes(path);
        foreach (bool trust in new[] { false, true })
        {
            var loaded = Load(path, trust, SHA256.HashData(original));
            Check(loaded.Succeeded && loaded.Custody is not null, "managed generation loads with either opt-in");
            Check(loaded.Custody!.Chests[0].Quantity[0] == quantity && loaded.Custody.Chests[0].Copper[1] == copper,
                "materializer preserves exact quantity and copper");
            using var runtime = new WorldRuntime(new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()),
                new SandboxWorldSource.WorldFile(path), loaded.World!, loaded.Bootstrap!, new InterestManagementControl(),
                new WorldRuntimeOptions { MaxPlayers = 2 }, sourceCustody: loaded.Custody);
            var image = runtime.Chests.CaptureCustodySnapshot(loaded.World!.Header.UniqueId,
                loaded.World.Header.WorldId, loaded.World.Envelope.Revision);
            Check(image.Chests[0].Quantity[0] == quantity && image.Chests[0].Copper[1] == copper,
                "runtime preserves exact private funding");
        }
        Check(!Load(path, false, new byte[32]).Succeeded, "world hash mismatch refuses clone");
        using (var registry = new WorldRegistry(2))
        using (var host = new SandboxHost(registry, BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits(), pendingJobCapacity: 2, maxPlayersPerRuntime: 2))
        {
            var name = new SandboxName("custody-copy");
            Check(host.TryCreate(new(name, WorldIsolationLevel.InProcess, new SandboxWorldSource.WorldFile(path)),
                out var job, out var error), "actual host queues managed readonly clone: " + error);
            var done = host.WaitForJobAsync(job, TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            Check(done.Status == SandboxJobStatus.Completed, "actual host completes: " + done.Error);
            Check(host.TryGetLiveRuntime(name, out var live, out error), "host exposes admitted runtime: " + error);
            Check(live!.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false).GetAwaiter().GetResult(),
                "stop clone before inspecting single-owner state");
            var world = live.Chests.CaptureCustodySnapshot(live.World.Header.UniqueId,
                live.World.Header.WorldId, live.World.Envelope.Revision);
            Check(world.Chests[0].Quantity[0] == quantity && world.Chests[0].Copper[1] == copper,
                "actual host constructor passes funding to admitted runtime");
        }
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(original), "clone never changes canonical bytes");
        if (failures)
        {
            var world = Load(path).World!;
            string artifact = RuntimeWorldChestCustodyArtifacts.GetArtifactPath(path, world.Envelope.Revision, SHA256.HashData(original));
            byte[] valid = File.ReadAllBytes(artifact);
            byte[] bad = valid.ToArray(); bad[^1] ^= 1;
            try
            {
                File.WriteAllBytes(artifact, bad);
                Check(!Load(path).Succeeded && !Load(path, true).Succeeded, "corrupt managed funding never refills");
                File.Move(artifact, artifact + ".sandbox-held");
                Check(!Load(path).Succeeded && !Load(path, true).Succeeded, "missing managed funding never refills");
                File.Move(artifact + ".sandbox-held", artifact);
            }
            finally { File.WriteAllBytes(artifact, valid); }
            Check(Load(path).Succeeded, "valid restored managed companion permits clone");
        }
        Console.WriteLine($"SANDBOX_CUSTODY q={quantity} copper={copper} checks={checks-before}");
        return checks - before;
    }
}
