using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core;

public sealed partial class RuntimeNpcStore
{
    public bool TrySpawn(byte slot, in NpcStateUpdate update, out NpcSnapshot snapshot)
    {
        if (!IsAddressableSlot(slot) || !IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[slot];
        if (state.Active || !TryAdvance(ref state.Generation))
        {
            snapshot = default;
            return false;
        }

        NpcStateUpdate normalized = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in update);
        state.Active = true;
        state.Revision = 1;
        state.Update = normalized;
        _activeCount++;
        snapshot = Capture(slot, in state);
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in snapshot);
        return true;
    }

    /// <summary>Materializes and allocates one committed AI spawn intent using source-backed vanilla defaults.</summary>
    public bool TrySpawnIntent(in NpcAiSpawnIntent intent, out NpcSnapshot snapshot)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(intent.Type, out VanillaNpcDefinition definition) ||
            !float.IsFinite(intent.VelocityX) ||
            !float.IsFinite(intent.VelocityY) ||
            !intent.InitialAi.IsFinite ||
            !intent.InitialLocalAi.IsFinite)
        {
            snapshot = default;
            return false;
        }

        var update = new NpcStateUpdate(
            Type: intent.Type.Value,
            NetId: checked((short)intent.Type.Value),
            PositionX: intent.BottomX - definition.Width * 0.5f,
            PositionY: intent.BottomY - definition.Height,
            VelocityX: intent.VelocityX,
            VelocityY: intent.VelocityY,
            Target: intent.Target,
            Ai: intent.InitialAi,
            Simulation: NpcSimulationState.Initial with
            {
                TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft,
                LocalAi = intent.InitialLocalAi
            });

        return TrySpawnVanilla(in update, out snapshot);
    }

    /// <summary>Allocates the first reusable vanilla NPC slot and advances its generation.</summary>
    public bool TrySpawnVanilla(in NpcStateUpdate update, out NpcSnapshot snapshot)
    {
        if (!IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        for (int slot = 0; slot < _slots.Length; slot++)
        {
            ref readonly SlotState state = ref _slots[slot];
            if (state.Active || state.Generation == ulong.MaxValue)
                continue;

            return TrySpawn(checked((byte)slot), in update, out snapshot);
        }

        snapshot = default;
        return false;
    }
}
