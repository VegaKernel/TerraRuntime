using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using Xunit;

internal static class GemStaffNetworkChecks
{
    internal static void Run()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        var first = Join(replication, 1);
        var second = Join(replication, 2);
        var store = new RuntimeProjectileStore(1, replication);
        var current = Birth(store);
        Drain(first); Drain(second);
        var key = new TerrariaProjectileKeyState(0, 77, 3);
        Assert.True(replication.WireIdentities.TryBind(in key, current.Handle));

        Publish(store, replication, ref current, false, 101);
        Assert.Equal(0, first.QueuedFrames);
        var late = Join(replication, 3);
        Assert.True(Queue(late).TryRead(out var baseline));
        Assert.Equal(27, baseline.Bytes.Span[2]);
        Assert.Equal(77u << 8 | 3u << 18, BinaryPrimitives.ReadUInt32LittleEndian(baseline.Bytes.Span[3..]));
        Assert.Equal(101f, ReadSingle(baseline.Bytes.Span, 7));

        for (int i = 0; i < 12; i++)
        {
            // Explicit source netUpdate still sends a byte-identical update.
            Publish(store, replication, ref current, true, 101);
            Assert.Equal(i + 1, first.QueuedFrames);
            Assert.Equal(i + 1, second.QueuedFrames);
        }
        Publish(store, replication, ref current, true, 102);
        Assert.Equal(12, first.QueuedFrames);
        Publish(store, replication, ref current, false, 103);
        Assert.Equal(12, first.QueuedFrames);
        replication.AdvanceNetSpamBudget();
        Publish(store, replication, ref current, false, 103);
        Assert.Equal(13, first.QueuedFrames);
        Drain(first);
        Publish(store, replication, ref current, false, 104);
        Assert.Equal(0, first.QueuedFrames);
        Publish(store, replication, ref current, true, 104);
        Assert.Equal(0, first.QueuedFrames);

        // Kill packet29 retains the entry vector, independent of the final physical pose.
        Despawn(store, replication, in current, false);
        Assert.True(Queue(first).TryRead(out var killed));
        var bytes = killed.Bytes.Span;
        Assert.Equal(29, bytes[2]);
        Assert.Equal(77u << 8 | 3u << 18, BinaryPrimitives.ReadUInt32LittleEndian(bytes[3..]));
        Assert.Equal(100f, ReadSingle(bytes, 7));
        Assert.Equal(200f, ReadSingle(bytes, 11));
        Assert.Equal(0, Join(replication, 4).QueuedFrames);

        // A reused slot inherits neither intent nor the depleted packet budget.
        current = Birth(store);
        Drain(first); Drain(second); Drain(late);
        Publish(store, replication, ref current, false, 105);
        Assert.Equal(0, first.QueuedFrames);
        Publish(store, replication, ref current, true, 105);
        Assert.Equal(1, first.QueuedFrames);
        Drain(first);
        Despawn(store, replication, in current, true);
        Assert.True(Queue(first).TryRead(out var bounded));
        Assert.Equal(29, bounded.Bytes.Span[2]);
        Assert.True(float.IsNaN(ReadSingle(bounded.Bytes.Span, 7)));
        Assert.True(float.IsNaN(ReadSingle(bounded.Bytes.Span, 11)));
        Assert.Equal(0, Join(replication, 5).QueuedFrames);
        current = Birth(store);
        Drain(first);
        Despawn(store, replication, in current, false, sourceRemove: true);
        Assert.Equal(0, first.QueuedFrames);
        Assert.Equal(0, Join(replication, 6).QueuedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
        CheckReentrantPublication();
        Console.WriteLine("GEM_NETWORK_PASS explicit27 pending27 exact-key kill-origin29 bounds-NaN29 baseline generation");
    }

    private static void CheckReentrantPublication()
    {
        foreach (int scenario in new[] { 0, 1, 2, 3 })
        {
            var replication = new RuntimeProjectileReplicationRegistry();
            var sink = new BeforeReplicationSink(replication);
            var store = new RuntimeProjectileStore(1, sink);
            var observer = Join(replication, 10);
            var current = Birth(store);
            Drain(observer);
            ProjectileSnapshot replacement = default;
            sink.Before = (kind, historical) =>
            {
                sink.Before = null;
                if (scenario == 0)
                {
                    var moved = State(in historical) with { PositionX = 700 };
                    Assert.True(store.TryUpdate(historical.Handle, in moved, out replacement));
                }
                else
                {
                    if (kind == ProjectileStateCommitKind.Update)
                        Assert.True(store.TryRemove(historical.Handle, out _));
                    replacement = Birth(store);
                    var moved = State(in replacement) with { PositionX = 700 };
                    Assert.True(store.TryUpdate(replacement.Handle, in moved, out replacement));
                }
            };
            if (scenario >= 2) Despawn(store, replication, in current, false, sourceRemove: scenario == 3);
            else Publish(store, replication, ref current, true, 101);
            Assert.True(store.TryGet(replacement.Handle, out var retained));
            Assert.Equal(replacement, retained);
            Assert.Equal(scenario == 0 ? 0 : 1, observer.QueuedFrames);
            Assert.Equal(0, replication.UnsupportedCommits);
            Assert.True(replication.WireIdentities.TryGetWireKey(replacement.Handle, out var retainedKey));
            var joining = Join(replication, 11);
            Assert.True(Queue(joining).TryRead(out var baseline));
            Assert.Equal(700f, ReadSingle(baseline.Bytes.Span, 7));
            uint packed = (uint)(retainedKey.Spawner | retainedKey.ProjectileIndex << 8 | retainedKey.Generation << 18);
            Assert.Equal(packed, BinaryPrimitives.ReadUInt32LittleEndian(baseline.Bytes.Span[3..]));
            Drain(observer);
            Publish(store, replication, ref replacement, false, 701);
            Assert.Equal(0, observer.QueuedFrames);
            Publish(store, replication, ref replacement, true, 701);
            Assert.Equal(1, observer.QueuedFrames);
        }
    }

    private sealed class BeforeReplicationSink(RuntimeProjectileReplicationRegistry replication) : IProjectileStateCommitSink
    {
        internal Action<ProjectileStateCommitKind, ProjectileSnapshot>? Before;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            Before?.Invoke(kind, snapshot);
            replication.ProjectileStateCommitted(kind, in snapshot);
        }
    }

    private static ProjectileSnapshot Birth(RuntimeProjectileStore store)
    {
        var state = new ProjectileStateUpdate(new(121), 0, 100, 200, 8, 0,
            new(VanillaGemStaffProjectile1458.EncodeFeatures(GemStaffFeatures1458.Homing), 0, -1), 0, 20, 3, 0);
        Assert.True(store.TryPrepareVanillaSpawn(in state, null, GemStaffFeatures1458.Homing, out var birth));
        Assert.NotNull(birth);
        Assert.True(birth.TryCommitUnpublished(out var result));
        Assert.True(store.TryMarkCombatTrusted(result.Handle, new(new(0), new(1))));
        Assert.True(birth.TryPublish(in result));
        return result;
    }

    private static void Publish(RuntimeProjectileStore store, RuntimeProjectileReplicationRegistry replication,
        ref ProjectileSnapshot current, bool force, float x)
    {
        Assert.True(store.TryGetLifecycle(current.Handle, out var life));
        Assert.True(store.TryPrepareSimulation(in current, out var step));
        Assert.NotNull(step);
        var state = State(in current) with { PositionX = x };
        Assert.True(step.TryStageMotion(in state, in life));
        Assert.True(step.TryQueuePublication());
        Assert.True(step.TryAdoptUnpublished(out current, out bool expired));
        Assert.False(expired);
        Assert.True(replication.TryPublishGemStaffActor(step, force));
        Assert.False(replication.TryPublishGemStaffActor(step, force));
    }

    private static void Despawn(RuntimeProjectileStore store, RuntimeProjectileReplicationRegistry replication,
        in ProjectileSnapshot current, bool bounds, bool sourceRemove = false)
    {
        Assert.True(store.TryGetLifecycle(current.Handle, out var life));
        Assert.True(store.TryPrepareSimulation(in current, out var step));
        Assert.NotNull(step);
        var state = State(in current) with { PositionX = 94, PositionY = 194 };
        var terminal = life with { TimeLeft = 0 };
        Assert.True(step.TryStageMotion(in state, in terminal));
        Assert.True(sourceRemove ? step.TryQueuePublication() : step.TryQueueGemStaffDespawn(new(100, 200)));
        Assert.True(step.TryAdoptUnpublished(out _, out bool expired));
        Assert.True(expired);
        Assert.True(replication.TryPublishGemStaffActor(step, false, bounds));
    }

    private static ProjectileStateUpdate State(in ProjectileSnapshot p) => new(p.Type,p.Spawner,
        p.PositionX,p.PositionY,p.VelocityX,p.VelocityY,p.Ai,p.BannerIdToRespondTo,p.Damage,p.KnockBack,p.OriginalDamage);

    private static TerrariaConnectionOutboundQueue Join(RuntimeProjectileReplicationRegistry replication, long id)
    {
        var source = GameCommandSourceId.FromConnection(id);
        var queue = new TerrariaConnectionOutboundQueue(new(maxFrames:128,maxQueuedBytes:131072,maxFrameBytes:1024));
        Assert.True(replication.TryRegister(source, queue));
        var connection = new ConnectionHandle(source, new(new((byte)id),new(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot,100,200,0,0,0,0,0);
        replication.PlayerSpawned(connection,in spawn);
        return queue;
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_queue")]
    private static extern ref BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue queue);
    private static void Drain(TerrariaConnectionOutboundQueue queue) { while(Queue(queue).TryRead(out _)) {} }
    private static float ReadSingle(ReadOnlySpan<byte> bytes, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]));
}
