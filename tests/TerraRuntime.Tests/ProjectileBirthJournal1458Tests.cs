using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ProjectileBirthJournal1458Tests
{
    [Fact]
    public void Explicit_source_keys_publish_every_historical_birth_and_observers_without_rebinding()
    {
        using var stream = typeof(ProjectileBirthJournal1458Tests).Assembly.GetManifestResourceStream("RangedSpawnBatch1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var data = JsonDocument.Parse(gzip);
        int rows = 0;
        foreach (var row in data.RootElement.GetProperty("rows").EnumerateArray())
        {
            rows++;
            var registry = new RuntimeProjectileReplicationRegistry();
            var sink = new Fanout(registry);
            var store = ProjectileSpawnBatch1458Tests.CreatePool(row.GetProperty("poolMode").GetString()!, sink);
            sink.Enabled = true;
            foreach (var change in row.GetProperty("generationChanges").EnumerateArray())
                ProjectileSpawnBatch1458Tests.SetGeneration(store, change.GetProperty("slot").GetInt32(), change.GetProperty("before").GetUInt64());
            var frames = row.GetProperty("frames").EnumerateArray().Skip(2).Select(x => x.GetString()!).ToArray();
            var requests = frames.Select(x => new RuntimeProjectileStore.SpawnRequest(ProjectileSpawnBatch1458Tests.ReadSourceBody(x))).ToArray();
            var keys = frames.Select(Key).ToArray();
            var queue = Playing(registry, 2);
            var excluded = Playing(registry, 1);
            Assert.True(store.TryPrepareVanillaSpawnBatch(requests, out var batch));
            using (batch!)
            {
                Assert.True(registry.TryPrepareSpawnBirthJournal(batch!.Births, keys, GameCommandSourceId.FromConnection(1), out var journal));
                Assert.True(journal!.IsCurrentOwned);
                Assert.True(batch.TryCommitUnpublished());
                Assert.True(journal.TryAdoptBaselines(store));
                Assert.True(journal.TryPublish(store));
                sink.Events.Clear();
                using (journal.EnterObserverPublication()) Assert.True(batch.TryPublishBirthJournal());
                Assert.Equal(batch.Births.ToArray(), sink.Events.ToArray());
                Assert.Equal(frames, Drain(queue));
                Assert.Empty(Drain(excluded));
                Assert.False(journal.TryPublish(store));
                foreach (var final in batch.FinalBirths)
                {
                    int index = Array.FindLastIndex(batch.Births.ToArray(), x => x.Handle.Slot == final.Handle.Slot);
                    Assert.True(registry.WireIdentities.TryResolve(in keys[index], out var resolved));
                    Assert.Equal(final.Handle, resolved);
                }
                var join = Playing(registry, 3);
                Assert.Equal(batch.FinalBirths.ToArray().Count(birth => birth.Handle.Slot < RuntimeProjectileStore.VanillaPhysicalSlotCount), Drain(join).Length);
            }
        }
        Assert.Equal(12, rows);
    }

    [Fact]
    public void Reentrant_distinct_spawn_keeps_new_binding_while_remaining_historical_observers_run()
    {
        var registry = new RuntimeProjectileReplicationRegistry();
        var sink = new Fanout(registry);
        var store = ProjectileSpawnBatch1458Tests.CreatePool("full-old-long", sink);
        sink.Enabled = true;
        var state = new ProjectileStateUpdate(new(14), 0, 100, 100, 1, 0, default, 0, 20, 0, 20);
        Assert.True(store.TryPrepareVanillaSpawnBatch([new(state), new(state)], out var batch));
        using (batch!)
        {
            var births = batch!.Births.ToArray();
            var keys = births.Select(x => new TerrariaProjectileKeyState(0, 17, checked((ushort)x.Handle.Generation.Value))).ToArray();
            var queue = Playing(registry, 2);
            Assert.True(registry.TryPrepareSpawnBirthJournal(births, keys, default, out var journal));
            Assert.True(batch.TryCommitUnpublished());
            Assert.True(journal!.TryAdoptBaselines(store));
            Assert.True(journal.TryPublish(store));
            sink.Events.Clear();
            ProjectileSnapshot replacement = default;
            sink.Callback = () =>
            {
                Assert.True(store.TryDespawn(births[^1].Handle, out _));
                Assert.True(store.TrySpawn(births[0].Handle.Slot, state with { PositionX = 300 }, out replacement));
            };
            using (journal.EnterObserverPublication()) Assert.True(batch.TryPublishBirthJournal());
            Assert.Contains(births[0], sink.Events);
            Assert.Contains(births[1], sink.Events);
            Assert.Contains(replacement, sink.Events);
            Assert.True(registry.WireIdentities.TryGetWireKey(replacement.Handle, out var replacementKey));
            Assert.True(registry.WireIdentities.TryResolve(in replacementKey, out var resolved));
            Assert.Equal(replacement.Handle, resolved);
            Assert.Equal(4, Drain(queue).Length);
        }
    }

    [Fact]
    public void Late_key_rebinding_or_final_actor_replacement_consumes_no_historical_publication()
    {
        foreach (bool rebind in new[] { false, true })
        {
            var registry = new RuntimeProjectileReplicationRegistry();
            var store = new RuntimeProjectileStore();
            var state = new ProjectileStateUpdate(new(14), 0, 100, 100, 1, 0, default, 0, 20, 0, 20);
            Assert.True(store.TryPrepareVanillaSpawnBatch([new(state)], out var batch));
            using (batch!)
            {
                var birth = batch!.Births[0];
                var key = new TerrariaProjectileKeyState(0, 17, 1);
                Assert.True(registry.TryPrepareSpawnBirthJournal([birth], [key], default, out var journal));
                Assert.True(batch.TryCommitUnpublished());
                Assert.True(journal!.TryAdoptBaselines(store));
                var queue = Playing(registry, 2); Drain(queue);
                if (rebind) Assert.True(registry.WireIdentities.TryBind(new(0, 18, 2), birth.Handle));
                else
                {
                    Assert.True(store.TryDespawn(birth.Handle, out _));
                    Assert.True(store.TrySpawn(birth.Handle.Slot, state, out _));
                }
                Assert.False(journal.TryPublish(store));
                Assert.False(journal.TryPublish(store));
                Assert.Empty(Drain(queue));
            }
        }
    }

    private static TerrariaProjectileKeyState Key(string frame)
    {
        uint packed = BitConverter.ToUInt32(Convert.FromHexString(frame), 3);
        return new((byte)packed, (ushort)((packed >> 8) & 1023), (ushort)(packed >> 18));
    }
    private static TerrariaConnectionOutboundQueue Playing(RuntimeProjectileReplicationRegistry registry, long id)
    {
        var queue = new TerrariaConnectionOutboundQueue(new(64, 65536, 1024));
        var source = GameCommandSourceId.FromConnection(id);
        Assert.True(registry.TryRegister(source, queue));
        var slot = new PlayerSlotId((byte)id);
        registry.PlayerSpawned(new(source, new(slot, new(1))), new(slot, 1, 1, 0, 0, 0, 0, 0));
        return queue;
    }
    private static string[] Drain(TerrariaConnectionOutboundQueue queue)
    {
        List<string> frames = [];
        var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(queue)!;
        while (inner.TryRead(out var frame)) frames.Add(Convert.ToHexString(frame.Bytes.Span));
        return frames.ToArray();
    }
    private sealed class Fanout(RuntimeProjectileReplicationRegistry registry) : IProjectileStateCommitSink
    {
        internal readonly List<ProjectileSnapshot> Events = [];
        internal Action? Callback;
        internal bool Enabled;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            if (Enabled) registry.ProjectileStateCommitted(kind, in snapshot);
            Events.Add(snapshot);
            var callback = Callback; Callback = null; callback?.Invoke();
        }
    }
}
