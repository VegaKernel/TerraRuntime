using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    internal sealed partial class NaturalSpawnPreview
    {
        internal bool AllowsStatue(int type, int x, int y)
        {
            if (!IsCurrentOwned()) return false;
            Span<NpcSnapshot> scan = stackalloc NpcSnapshot[VanillaNpcSpawnRules.PhysicalSlotCount];
            int count = 0;
            for (int i = 0; i < Math.Min(owner.Capacity, 200); i++)
                if (owner.TryGetActive((byte)i, out var npc)) scan[count++] = npc;
            return VanillaNpcStatue1458.CanSpawn(scan[..count], x, y, type);
        }

        internal bool TryStageStatue(in NpcStateUpdate update, int centerX, int bottomY, out NpcSnapshot? birth)
        {
            birth = null;
            // Source NewNPC returns its sentinel after the GoodWorld draw when the table is full.
            // Generation exhaustion is a runtime ownership failure, rather than a full vanilla table.
            if (!IsCurrentOwned()) return false;
            for (int i = 0; i < Math.Min(owner.Capacity, 200); i++)
                if (owner._slots[i].Generation == ulong.MaxValue) return false;
            if (!TryStage(in update, centerX, bottomY, out var staged))
                return detached is not null && IsCurrentOwned() &&
                    !Enumerable.Range(0, Math.Min(owner.Capacity, 200)).Any(i =>
                        (!owner._slots[i].Active && owner._slots[i].SpawnProtection == 0) ||
                        owner._slots[i].Update.Simulation.CanBeReplacedByOtherNpcs);
            // This bound keeps every admitted birth/WetCollision read inside the retained terrain neighborhood.
            if (!VanillaNpcDefinitionCatalog.TryGet(staged.TypeIdentity, staged.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(staged.Simulation, out var body) || body.Width > 128 || body.Height > 128) return false;
            ref SlotState slot = ref detached!._slots[staged.Handle.Slot];
            slot.Update = slot.Update with { Simulation = slot.Update.Simulation with
                { MoneyValue = 0f, SpawnSlotCost = 0f, SpawnedFromStatue = true, CanBeReplacedByOtherNpcs = true,
                    ReleaseOwner = staged.Simulation.ReleaseOwner ?? 255,
                    CatchableNpcTempImmunityCounter = staged.Simulation.CatchableNpcTempImmunityCounter ?? 0 } };
            child = birth = Capture(staged.Handle.Slot, in slot);
            return true;
        }
    }
}
