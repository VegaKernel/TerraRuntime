using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ProjectileSpawnBatch1458Tests
{
    [Fact]
    public void Ordered_shadow_allocations_and_every_birth_match_original_pool_and_socket_profiles()
    {
        using Stream stream = typeof(ProjectileSpawnBatch1458Tests).Assembly.GetManifestResourceStream("RangedSpawnBatch1458")!;
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument data = JsonDocument.Parse(gzip);
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034",
            data.RootElement.GetProperty("sourceAssemblySha256").GetString());
        int cases = 0;
        foreach (JsonElement row in data.RootElement.GetProperty("rows").EnumerateArray())
        {
            cases++;
            string mode = row.GetProperty("poolMode").GetString()!;
            var sink = new Sink();
            RuntimeProjectileStore store = CreatePool(mode, sink);
            foreach (JsonElement change in row.GetProperty("generationChanges").EnumerateArray())
                SetGeneration(store, change.GetProperty("slot").GetInt32(), change.GetProperty("before").GetUInt64());
            int activeBefore = store.ActiveCount;
            object[] before = RawSlots(store);
            string[] frames = row.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.Equal((byte)13, Convert.FromHexString(frames[0])[2]);
            Assert.Equal((byte)41, Convert.FromHexString(frames[1])[2]);
            var requests = frames.Skip(2).Select(frame => new RuntimeProjectileStore.SpawnRequest(ReadSourceBody(frame))).ToArray();
            Assert.True(store.TryPrepareVanillaSpawnBatch(requests, out var plan));
            Assert.NotNull(plan);
            using (plan!)
            {
                Assert.Empty(sink.Events);
                Assert.Equal(before, RawSlots(store));
                Assert.True(plan.IsCurrentOwned);
                Assert.Equal(requests.Length, plan.Births.Length);
                for (int index = 0; index < plan.Births.Length; index++)
                {
                    ProjectileSnapshot birth = plan.Births[index];
                    JsonElement source = row.GetProperty("publications")[index + 2];
                    Assert.Equal(source.GetProperty("slot").GetInt32(), birth.Handle.Slot);
                    Assert.Equal(source.GetProperty("generation").GetUInt64(), birth.Handle.Generation.Value);
                    Assert.Equal(frames[index + 2], Encode(birth));
                }
                ProjectileSnapshot[] births = plan.Births.ToArray();
                Assert.True(plan.TryCommitUnpublished());
                Assert.False(plan.IsCurrentOwned);
                Assert.False(plan.TryCommitUnpublished());
                Assert.Empty(sink.Events);
                Assert.Equal(mode is "one-free" or "all-important" ? activeBefore + 1 : activeBefore, store.ActiveCount);
                foreach (ProjectileSnapshot final in plan.FinalBirths)
                {
                    Assert.True(store.TryGet(final.Handle, out var retained));
                    Assert.Equal(final, retained);
                    Assert.True(store.TryGetLifecycle(final.Handle, out var lifecycle));
                    Assert.Equal(600, lifecycle.TimeLeft);
                    Assert.False(store.IsCombatTrusted(final.Handle));
                }
                foreach (ProjectileSnapshot overwritten in births)
                    if (!plan.FinalBirths.Contains(overwritten)) Assert.False(store.TryGet(overwritten.Handle, out _));
                foreach (JsonElement change in row.GetProperty("generationChanges").EnumerateArray())
                {
                    Assert.True(store.TryGetActive((ushort)change.GetProperty("slot").GetInt32(), out var final));
                    Assert.Equal(change.GetProperty("after").GetUInt64(), final.Handle.Generation.Value);
                }
                Assert.True(plan.TryPublishBirthJournal());
                Assert.False(plan.TryPublishBirthJournal());
                Assert.Equal(births, sink.Events.Select(x => x.Snapshot).ToArray());
                Assert.All(sink.Events, entry => Assert.Equal(ProjectileStateCommitKind.Spawn, entry.Kind));
                Assert.Equal(frames.Skip(2), sink.Events.Select(x => Encode(x.Snapshot)));
            }
        }
        Assert.Equal(12, cases);
    }

    [Fact]
    public void Late_census_changes_invalid_children_and_later_overflow_refuse_the_whole_batch()
    {
        var requests = new[] { new RuntimeProjectileStore.SpawnRequest(State()), new RuntimeProjectileStore.SpawnRequest(State()) };
        foreach (bool changeInactive in new[] { false, true })
        {
            var sink = new Sink();
            RuntimeProjectileStore store = changeInactive ? new(4, sink) : CreatePool("full-old-long", sink);
            Assert.True(store.TryPrepareVanillaSpawnBatch(requests, out var plan));
            Assert.NotNull(plan);
            using (plan!)
            {
                if (changeInactive)
                {
                    Assert.True(store.TrySpawn(3, State(), out var changed));
                    Assert.True(store.TryDespawn(changed.Handle, out _)); // Same active count; inactive generation changed.
                }
                else
                {
                    Assert.True(store.TryGetActive(999, out var changed));
                    Assert.True(store.TryCommitSimulationStep(changed.Handle, State(), 1, out _, out _));
                }
                sink.Events.Clear();
                ProjectileSnapshot[] afterExternal = Copy(store);
                Assert.False(plan.IsCurrentOwned);
                Assert.False(plan.TryCommitUnpublished());
                Assert.False(plan.TryPublishBirthJournal());
                Assert.Equal(afterExternal, Copy(store));
                Assert.Empty(sink.Events);
            }
        }
        foreach (bool sentinel in new[] { false, true })
        {
            var sink = new Sink();
            RuntimeProjectileStore store = CreatePool(sentinel ? "all-important" : "full-old-long", sink);
            SetGeneration(store, sentinel ? 1000 : 0, ulong.MaxValue - 1);
            ProjectileSnapshot[] before = Copy(store);
            Assert.False(store.TryPrepareVanillaSpawnBatch(requests, out _));
            Assert.Equal(before, Copy(store));
            Assert.Empty(sink.Events);
        }
        var bounded = new RuntimeProjectileStore(4);
        Assert.False(bounded.TryPrepareVanillaSpawnBatch([], out _));
        Assert.False(bounded.TryPrepareVanillaSpawnBatch(Enumerable.Repeat(requests[0], 9).ToArray(), out _));
        Assert.False(bounded.TryPrepareVanillaSpawnBatch([requests[0], new(State() with { PositionX = float.NaN })], out _));
        Assert.False(bounded.TryPrepareVanillaSpawnBatch([requests[0], new(State(), 0)], out _));
        Assert.Equal(0, bounded.ActiveCount);
        var noSentinel = new RuntimeProjectileStore(1000);
        for (ushort slot = 0; slot < 1000; slot++) Assert.True(noSentinel.TrySpawn(slot, State(13), out _));
        Assert.False(noSentinel.TryPrepareVanillaSpawnBatch(requests, out _));
        Assert.Equal(1000, noSentinel.ActiveCount);
        Assert.True(bounded.TryPrepareVanillaSpawnBatch(requests, out var disposed));
        disposed!.Dispose();
        disposed.Dispose();
        Assert.False(disposed.IsCurrentOwned);
        Assert.False(disposed.TryCommitUnpublished());
    }

    [Fact]
    public void Publication_keeps_all_accepted_births_without_rebinding_reentrant_replacement()
    {
        var sink = new Sink();
        RuntimeProjectileStore store = CreatePool("full-old-long", sink);
        var requests = Enumerable.Range(0, 8).Select(index => new RuntimeProjectileStore.SpawnRequest(State() with { PositionX = index })).ToArray();
        Assert.True(store.TryPrepareVanillaSpawnBatch(requests, out var plan));
        Assert.NotNull(plan);
        using (plan!)
        {
            ProjectileSnapshot[] journal = plan.Births.ToArray();
            ProjectileSnapshot final = plan.FinalBirths[0];
            Assert.True(plan.TryCommitUnpublished());
            ProjectileSnapshot replacement = default;
            sink.Callback = () =>
            {
                Assert.True(store.TryGet(final.Handle, out var current));
                Assert.Equal(final, current); // Whole volley already adopted before the first callback.
                Assert.True(store.TryDespawn(final.Handle, out _));
                Assert.True(store.TrySpawn(0, State() with { PositionX = 9000 }, out replacement));
            };
            Assert.True(plan.TryPublishBirthJournal());
            Assert.Equal(journal, sink.Events.Select(x => x.Snapshot).ToArray());
            Assert.True(store.TryGet(replacement.Handle, out var after));
            Assert.Equal(replacement, after);
            Assert.False(store.TryGet(final.Handle, out _));
            Assert.False(plan.TryPublishBirthJournal());
        }
        var single = new RuntimeProjectileStore(2);
        Assert.True(single.TryPrepareVanillaSpawn(State(), null, out var one));
        Assert.True(one!.TryCommitUnpublished(out var original));
        Assert.True(single.TryDespawn(original.Handle, out _));
        Assert.False(one.TryPublish(original)); // The ordinary single-spawn stale publication guard is unchanged.
    }

    internal static RuntimeProjectileStore CreatePool(string mode, IProjectileStateCommitSink? sink = null)
    {
        var store = new RuntimeProjectileStore(commitSink: sink);
        for (ushort slot = 0; slot < 1000; slot++)
        {
            if (mode == "one-free" && slot == 50) continue;
            ProjectileStateUpdate old = State(mode == "all-important" ? 13 : 14);
            Assert.True(store.TrySpawn(slot, old, out var created));
            Assert.True(store.TryCommitSimulationStep(created.Handle, old, mode == "full-old-short" ? 1 : 1000, out _, out _));
        }
        if (sink is Sink recording) recording.Events.Clear();
        return store;
    }

    // Fixture-only access preserves the independently captured cumulative source counters, including inactive slots.
    internal static void SetGeneration(RuntimeProjectileStore store, int slot, ulong generation)
    {
        var slots = (Array)typeof(RuntimeProjectileStore).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        object state = slots.GetValue(slot)!;
        state.GetType().GetField("Generation")!.SetValue(state, generation);
        slots.SetValue(state, slot);
    }

    private static object[] RawSlots(RuntimeProjectileStore store) =>
        ((Array)typeof(RuntimeProjectileStore).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!).Cast<object>().ToArray();

    private static ProjectileStateUpdate State(int type = 14) => new(new(type), 0, 408, 419, 11, 0, default, 0, 10, 2, 0);
    private static ProjectileSnapshot[] Copy(RuntimeProjectileStore store)
    {
        var snapshots = new ProjectileSnapshot[store.ActiveCount];
        Assert.Equal(snapshots.Length, store.CopyActive(snapshots));
        return snapshots;
    }

    internal static ProjectileStateUpdate ReadSourceBody(string hex)
    {
        using var reader = new BinaryReader(new MemoryStream(Convert.FromHexString(hex)));
        Assert.Equal(reader.BaseStream.Length, reader.ReadUInt16());
        Assert.Equal(27, reader.ReadByte());
        uint key = reader.ReadUInt32();
        float x = reader.ReadSingle(), y = reader.ReadSingle(), vx = reader.ReadSingle(), vy = reader.ReadSingle();
        short type = reader.ReadInt16();
        byte flags = reader.ReadByte();
        byte extra = (flags & 4) != 0 ? reader.ReadByte() : (byte)0;
        float ai0 = (flags & 1) != 0 ? reader.ReadSingle() : 0, ai1 = (flags & 2) != 0 ? reader.ReadSingle() : 0;
        ushort banner = (flags & 8) != 0 ? reader.ReadUInt16() : (ushort)0;
        short damage = (flags & 16) != 0 ? reader.ReadInt16() : (short)0;
        float knockback = (flags & 32) != 0 ? reader.ReadSingle() : 0;
        short originalDamage = (flags & 64) != 0 ? reader.ReadInt16() : (short)0;
        float ai2 = (extra & 1) != 0 ? reader.ReadSingle() : 0;
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        return new(new(type), (byte)key, x, y, vx, vy, new(ai0, ai1, ai2), banner, damage, knockback, originalDamage);
    }

    private static string Encode(ProjectileSnapshot birth)
    {
        var wire = new TerrariaProjectileUpdateState(new(birth.Spawner, birth.Handle.Slot, checked((ushort)birth.Handle.Generation.Value)),
            birth.Type.Value, birth.PositionX, birth.PositionY, birth.VelocityX, birth.VelocityY,
            birth.Ai.Ai0, birth.Ai.Ai1, birth.Ai.Ai2, birth.BannerIdToRespondTo, birth.Damage, birth.KnockBack, birth.OriginalDamage);
        Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(wire, out byte[] bytes));
        return Convert.ToHexString(bytes);
    }

    private sealed class Sink : IProjectileStateCommitSink
    {
        internal readonly List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)> Events = [];
        internal Action? Callback;
        private bool reentrant;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            if (reentrant) return;
            Events.Add((kind, snapshot));
            if (Callback is not { } callback) return;
            Callback = null;
            reentrant = true;
            try { callback(); }
            finally { reentrant = false; }
        }
    }
}
