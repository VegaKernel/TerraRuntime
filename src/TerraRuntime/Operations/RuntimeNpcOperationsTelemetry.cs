using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Operations;

/// <summary>
/// TUI-facing immutable projection of authoritative NPC commits. The authoritative store remains the
/// only mutable gameplay owner. Commit writes are single-writer, allocation-free and lock-free; the UI
/// uses a per-slot sequence check to copy a consistent bounded projection without scanning RuntimeNpcStore.
/// </summary>
internal sealed class RuntimeNpcOperationsTelemetry : INpcOperations, INpcStateCommitSink
{
    private readonly SlotState[] slots = CreateSlots();
    private long committedSpawns;
    private long committedUpdates;
    private long committedDespawns;

    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        if (!snapshot.IsActive)
            return;

        SlotState slot = slots[snapshot.Handle.Slot];
        switch (kind)
        {
            case NpcStateCommitKind.Spawn:
                WriteSnapshot(slot, in snapshot);
                committedSpawns++;
                break;

            case NpcStateCommitKind.Update:
                WriteSnapshot(slot, in snapshot);
                committedUpdates++;
                break;

            case NpcStateCommitKind.Despawn:
                if (slot.Active && slot.Generation == snapshot.Handle.Generation.Value)
                    ClearSnapshot(slot);
                committedDespawns++;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public RuntimeNpcsSnapshot CaptureSnapshot()
    {
        var captured = new RuntimeNpcSnapshot[slots.Length];
        int written = 0;

        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            SlotState slot = slots[slotIndex];
            while (true)
            {
                long before = Volatile.Read(ref slot.Sequence);
                if ((before & 1L) != 0)
                    continue;

                bool active = slot.Active;
                RuntimeNpcSnapshot snapshot = slot.Snapshot;
                long after = Volatile.Read(ref slot.Sequence);
                if (before != after || (after & 1L) != 0)
                    continue;

                if (active)
                    captured[written++] = snapshot;
                break;
            }
        }

        return new RuntimeNpcsSnapshot(
            captured.AsMemory(0, written),
            Volatile.Read(ref committedSpawns),
            Volatile.Read(ref committedUpdates),
            Volatile.Read(ref committedDespawns),
            DateTimeOffset.UtcNow);
    }

    private static SlotState[] CreateSlots()
    {
        var result = new SlotState[RuntimeNpcStore.MaximumAddressableCapacity];
        for (int index = 0; index < result.Length; index++)
            result[index] = new SlotState();
        return result;
    }

    private static void WriteSnapshot(SlotState slot, in NpcSnapshot snapshot)
    {
        long writeSequence = BeginWrite(slot);
        slot.Active = true;
        slot.Generation = snapshot.Handle.Generation.Value;
        slot.Snapshot = Project(in snapshot);
        EndWrite(slot, writeSequence);
    }

    private static void ClearSnapshot(SlotState slot)
    {
        long writeSequence = BeginWrite(slot);
        slot.Active = false;
        slot.Generation = 0;
        slot.Snapshot = default;
        EndWrite(slot, writeSequence);
    }

    private static long BeginWrite(SlotState slot)
    {
        long sequence = Volatile.Read(ref slot.Sequence);
        long writeSequence = unchecked(sequence + 1L);
        if ((writeSequence & 1L) == 0)
            writeSequence = unchecked(writeSequence + 1L);
        Volatile.Write(ref slot.Sequence, writeSequence);
        return writeSequence;
    }

    private static void EndWrite(SlotState slot, long writeSequence) =>
        Volatile.Write(ref slot.Sequence, unchecked(writeSequence + 1L));

    private static RuntimeNpcSnapshot Project(in NpcSnapshot snapshot) =>
        new(
            Slot: snapshot.Handle.Slot,
            Generation: snapshot.Handle.Generation.Value,
            Revision: snapshot.Revision.Value,
            Type: snapshot.Type,
            NetId: snapshot.NetId,
            PositionX: snapshot.PositionX,
            PositionY: snapshot.PositionY,
            VelocityX: snapshot.VelocityX,
            VelocityY: snapshot.VelocityY,
            Target: snapshot.Target,
            Ai0: snapshot.Ai.Ai0,
            Ai1: snapshot.Ai.Ai1,
            Ai2: snapshot.Ai.Ai2,
            Ai3: snapshot.Ai.Ai3,
            DirectionX: snapshot.Simulation.DirectionX,
            DirectionY: snapshot.Simulation.DirectionY,
            CollideX: snapshot.Simulation.CollideX,
            CollideY: snapshot.Simulation.CollideY,
            Wet: snapshot.Simulation.Wet,
            NoGravity: snapshot.Simulation.NoGravity,
            NoTileCollide: snapshot.Simulation.NoTileCollide);

    private sealed class SlotState
    {
        public long Sequence;
        public bool Active;
        public ulong Generation;
        public RuntimeNpcSnapshot Snapshot;
    }
}
