using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Operations;

/// <summary>
/// TUI-facing immutable projection of authoritative NPC commits. The authoritative store remains the
/// only mutable gameplay owner; this observer keeps a bounded, generation-safe copy for cross-thread
/// operations reads and never scans or re-reads RuntimeNpcStore.
/// </summary>
internal sealed class RuntimeNpcOperationsTelemetry : INpcOperations, INpcStateCommitSink
{
    private readonly object sync = new();
    private readonly SlotState[] slots = new SlotState[RuntimeNpcStore.MaximumAddressableCapacity];
    private int activeCount;
    private long committedSpawns;
    private long committedUpdates;
    private long committedDespawns;

    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        if (!snapshot.IsActive)
            return;

        lock (sync)
        {
            ref SlotState slot = ref slots[snapshot.Handle.Slot];
            switch (kind)
            {
                case NpcStateCommitKind.Spawn:
                    Upsert(ref slot, in snapshot);
                    committedSpawns++;
                    break;

                case NpcStateCommitKind.Update:
                    Upsert(ref slot, in snapshot);
                    committedUpdates++;
                    break;

                case NpcStateCommitKind.Despawn:
                    if (slot.Active && slot.Generation == snapshot.Handle.Generation.Value)
                    {
                        slot = default;
                        activeCount--;
                    }
                    committedDespawns++;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public RuntimeNpcsSnapshot CaptureSnapshot()
    {
        lock (sync)
        {
            var snapshot = new RuntimeNpcSnapshot[activeCount];
            int written = 0;
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                ref readonly SlotState slot = ref slots[slotIndex];
                if (!slot.Active)
                    continue;

                snapshot[written++] = slot.Snapshot;
            }

            return new RuntimeNpcsSnapshot(
                snapshot.AsMemory(),
                committedSpawns,
                committedUpdates,
                committedDespawns,
                DateTimeOffset.UtcNow);
        }
    }

    private void Upsert(ref SlotState slot, in NpcSnapshot snapshot)
    {
        if (!slot.Active)
            activeCount++;

        slot.Active = true;
        slot.Generation = snapshot.Handle.Generation.Value;
        slot.Snapshot = Project(in snapshot);
    }

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

    private struct SlotState
    {
        public bool Active;
        public ulong Generation;
        public RuntimeNpcSnapshot Snapshot;
    }
}
