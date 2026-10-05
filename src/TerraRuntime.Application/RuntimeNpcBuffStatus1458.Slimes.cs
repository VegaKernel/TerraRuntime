using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcBuffStatus1458 : IVanillaSlimeStatusOwner1458
{
    bool IVanillaSlimeStatusOwner1458.TryCaptureRevision(in NpcSnapshot before, out ulong revision)
    {
        revision = 0;
        if (!EnsureGeneration(before.Handle)) return false;
        revision = entries[before.Handle.Slot].Revision;
        return revision != ulong.MaxValue;
    }

    bool IVanillaSlimeStatusOwner1458.TryPrepare(in NpcSnapshot before, IVanillaNpcRandom random,
        bool goodWorld, out IVanillaSlimeStatusPlan1458 plan)
    {
        plan = default!;
        if (!TryPlan(in before, out var status, goodWorld)) return false;
        var offer = PlanVisualOffers(in status, new NpcRuntimeTownCombatRandom1458(random), goodWorld);
        plan = new SlimeStatusPlan(this, status, offer);
        return true;
    }

    private sealed class SlimeStatusPlan(RuntimeNpcBuffStatus1458 owner, RuntimeNpcBuffPlan1458 before,
        RuntimeNpcStinkyVisualOffer1458 offer) : IVanillaSlimeStatusPlan1458
    {
        public ulong Revision => before.StatusRevision;
        public NpcSimulationState Simulation => before.Expected.Simulation with
        {
            Life = before.LifeAfter,
            LifeRegenCounter = before.CounterAfter
        };

        public bool IsCurrent => owner.entries[before.Expected.Handle.Slot].Handle == before.Expected.Handle &&
            owner.entries[before.Expected.Handle.Slot].Revision == before.StatusRevision;

        public void Commit(in NpcSnapshot completed, bool torch)
        {
            if (!IsCurrent || !owner.Commit(in before, in completed)) return;
            owner.ObserveVisualOffer(completed.Handle, in offer);
            // Expiry's source54 observes the aged table before AI001 refreshes the Torch buff.
            owner.PublishExpired(in before);
            if (torch) owner.RefreshTorch(completed.Handle);
            owner.TryRemoveWetStatus(completed.Handle);
        }
    }

    private bool RefreshTorch(NpcHandle handle)
    {
        if (!EnsureGeneration(handle)) return false;
        ref Entry entry = ref entries[handle.Slot];
        if (entry.Revision == ulong.MaxValue) return false;
        entry.TorchImmunity = true;
        int slot = -1;
        for (int i = 0; i < Capacity; i++)
        {
            if (entry.Slots[i].Type == VanillaBuffIds.OnFire.Value)
            {
                if (entry.Slots[i].Duration >= 216000) return true;
                slot = i;
                break;
            }
            if (slot < 0 && entry.Slots[i].Type == 0) slot = i;
        }
        // Every admitted slot is a source debuff; an all-debuff table admits no replacement.
        if (slot < 0) return true;
        entry.Slots[slot] = new((ushort)VanillaBuffIds.OnFire.Value, 216000);
        entry.Revision++;
        buffListChanged?.Invoke(handle);
        return true;
    }

    internal bool IsRetainedCurrent(in RuntimeNpcBuffPlan1458 plan) =>
        entries[plan.Expected.Handle.Slot].Handle == plan.Expected.Handle &&
        entries[plan.Expected.Handle.Slot].Revision == plan.StatusRevision;

    internal bool TryGetTorchImmunity(NpcHandle handle, BuffTypeId type, out bool immune)
    {
        immune = false;
        if (!EnsureGeneration(handle)) return false;
        if (entries[handle.Slot].DefinitionType != VanillaNpcIds.BlueSlime.Value &&
            entries[handle.Slot].DefinitionType != VanillaNpcIds.LavaSlime.Value) return false;
        bool dynamic = entries[handle.Slot].TorchImmunity;
        immune = type == VanillaBuffIds.Poisoned || (type == VanillaBuffIds.OnFire || type == VanillaBuffIds.OnFire3) &&
            entries[handle.Slot].DefinitionType == VanillaNpcIds.LavaSlime.Value || dynamic &&
            (type == VanillaBuffIds.CursedInferno || type == VanillaBuffIds.Frostburn ||
             type == VanillaBuffIds.OnFire3 || type == VanillaBuffIds.Frostburn2);
        return true;
    }
}
