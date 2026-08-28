using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Operations;

/// <summary>
/// TUI-only projection of committed authoritative projectile state. The simulation thread publishes one
/// compact slot projection through a single-writer sequence; aggregation by spawner/type happens only when
/// the UI captures a snapshot, so projectile commits stay allocation-free and never take a UI lock.
/// </summary>
internal sealed class RuntimeProjectileOperationsTelemetry : IProjectileOperations, IProjectileStateCommitSink
{
    private readonly SlotState[] slots = CreateSlots();
    private long committedSpawns;
    private long committedUpdates;
    private long committedDespawns;

    public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        if (!snapshot.IsActive)
            return;

        SlotState slot = slots[snapshot.Handle.Slot];
        switch (kind)
        {
            case ProjectileStateCommitKind.Spawn:
                WriteSnapshot(slot, in snapshot);
                Interlocked.Increment(ref committedSpawns);
                break;

            case ProjectileStateCommitKind.Update:
                WriteSnapshot(slot, in snapshot);
                Interlocked.Increment(ref committedUpdates);
                break;

            case ProjectileStateCommitKind.Despawn:
                if (slot.Active && slot.Generation == snapshot.Handle.Generation.Value)
                    ClearSnapshot(slot);
                Interlocked.Increment(ref committedDespawns);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public RuntimeProjectilesSnapshot CaptureSnapshot()
    {
        var grouped = new Dictionary<GroupKey, GroupAccumulator>();
        int activeProjectiles = 0;

        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            SlotState slot = slots[slotIndex];
            while (true)
            {
                long before = Volatile.Read(ref slot.Sequence);
                if ((before & 1L) != 0)
                    continue;

                bool active = slot.Active;
                ProjectileProjection projection = slot.Projection;
                Thread.MemoryBarrier();
                long after = Volatile.Read(ref slot.Sequence);
                if (before != after || (after & 1L) != 0)
                    continue;

                if (active)
                {
                    activeProjectiles++;
                    var key = new GroupKey(projection.Spawner, projection.Type);
                    grouped.TryGetValue(key, out GroupAccumulator accumulator);
                    accumulator.Add(in projection);
                    grouped[key] = accumulator;
                }

                break;
            }
        }

        var groups = new RuntimeProjectileGroupSnapshot[grouped.Count];
        int written = 0;
        foreach (KeyValuePair<GroupKey, GroupAccumulator> pair in grouped)
            groups[written++] = pair.Value.ToSnapshot(pair.Key);

        Array.Sort(groups, static (left, right) =>
        {
            int byCount = right.Count.CompareTo(left.Count);
            if (byCount != 0)
                return byCount;

            int bySpawner = left.Spawner.CompareTo(right.Spawner);
            return bySpawner != 0 ? bySpawner : left.Type.CompareTo(right.Type);
        });

        return new RuntimeProjectilesSnapshot(
            activeProjectiles,
            groups.AsMemory(),
            Interlocked.Read(ref committedSpawns),
            Interlocked.Read(ref committedUpdates),
            Interlocked.Read(ref committedDespawns),
            DateTimeOffset.UtcNow);
    }

    private static SlotState[] CreateSlots()
    {
        var result = new SlotState[RuntimeProjectileStore.MaximumProtocolAddressableCapacity];
        for (int index = 0; index < result.Length; index++)
            result[index] = new SlotState();
        return result;
    }

    private static void WriteSnapshot(SlotState slot, in ProjectileSnapshot snapshot)
    {
        BeginWrite(slot);
        slot.Active = true;
        slot.Generation = snapshot.Handle.Generation.Value;
        slot.Projection = new ProjectileProjection(
            snapshot.Spawner,
            snapshot.Type.Value,
            snapshot.PositionX,
            snapshot.PositionY,
            snapshot.VelocityX,
            snapshot.VelocityY,
            snapshot.Damage,
            snapshot.OriginalDamage,
            snapshot.KnockBack);
        EndWrite(slot);
    }

    private static void ClearSnapshot(SlotState slot)
    {
        BeginWrite(slot);
        slot.Active = false;
        slot.Generation = 0;
        slot.Projection = default;
        EndWrite(slot);
    }

    private static void BeginWrite(SlotState slot) => Interlocked.Increment(ref slot.Sequence);

    private static void EndWrite(SlotState slot) => Interlocked.Increment(ref slot.Sequence);

    private readonly record struct GroupKey(byte Spawner, int Type);

    private readonly record struct ProjectileProjection(
        byte Spawner,
        int Type,
        float PositionX,
        float PositionY,
        float VelocityX,
        float VelocityY,
        short Damage,
        short OriginalDamage,
        float KnockBack);

    private struct GroupAccumulator
    {
        private double positionX;
        private double positionY;
        private double velocityX;
        private double velocityY;
        private short maxDamage;
        private short maxOriginalDamage;
        private float maxKnockBack;

        public int Count { get; private set; }

        public void Add(in ProjectileProjection projection)
        {
            positionX += projection.PositionX;
            positionY += projection.PositionY;
            velocityX += projection.VelocityX;
            velocityY += projection.VelocityY;

            if (Count == 0 || projection.Damage > maxDamage)
                maxDamage = projection.Damage;
            if (Count == 0 || projection.OriginalDamage > maxOriginalDamage)
                maxOriginalDamage = projection.OriginalDamage;
            if (Count == 0 || projection.KnockBack > maxKnockBack)
                maxKnockBack = projection.KnockBack;

            Count++;
        }

        public readonly RuntimeProjectileGroupSnapshot ToSnapshot(GroupKey key) =>
            new(
                key.Spawner,
                key.Type,
                Count,
                (float)(positionX / Count),
                (float)(positionY / Count),
                (float)(velocityX / Count),
                (float)(velocityY / Count),
                maxDamage,
                maxOriginalDamage,
                maxKnockBack);
    }

    private sealed class SlotState
    {
        public long Sequence;
        public bool Active;
        public ulong Generation;
        public ProjectileProjection Projection;
    }
}
