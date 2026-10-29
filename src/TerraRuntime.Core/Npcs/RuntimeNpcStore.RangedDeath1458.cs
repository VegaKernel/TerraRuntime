using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    internal bool TryStageMaggotDeathChildren(in NpcSnapshot parent, IVanillaNpcRandom random)
    {
        if (Capacity != 200 || parent.Simulation.CanBeReplacedByOtherNpcs || !MatchesSource(in parent) ||
            !VanillaNpcDefinitionCatalog.TryGet(parent.TypeIdentity, parent.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(parent.Simulation, out var body) ||
            !VanillaRangedNpcDeath1458.TryGetMaggotCount(in parent, in body, random, out int count)) return false;
        for (int index = 0; index < count; index++)
        {
            if (!VanillaRangedNpcDeath1458.TryGetMaggotBirth(in parent, in body, index, random, out var request)) return false;
            // Do not resample a saturated source-selected generation or confuse a
            // missing constructor capability with NewNPC's no-slot sentinel.
            int selected = -1, replacement = -1;
            for (int slot = 0; slot < Capacity; slot++)
            {
                ref readonly SlotState candidate = ref _slots[slot];
                if (!candidate.Active && candidate.SpawnProtection == 0) { selected = slot; break; }
                if (replacement < 0 && candidate.Update.Simulation.CanBeReplacedByOtherNpcs) replacement = slot;
            }
            if (selected < 0) selected = replacement;
            if (selected >= 0 && _slots[selected].Generation == ulong.MaxValue) return false;
            var intent = new NpcAiSpawnIntent(new(606), request.BottomX, request.BottomY,
                request.VelocityX, request.VelocityY, 255);
            bool spawned = TrySpawnIntent(in intent, out var birth);
            if (!spawned && selected >= 0) return false;
            if (spawned && !TryRetainPendingBirth(in birth)) return false;
        }
        return MatchesSource(in parent);
    }

    internal bool TryPrepareMaggotDeathBirth(in NpcSnapshot parent, RuntimeNpcStore preview,
        ulong expectedSerial, out DeathBirth? preparation)
    {
        preparation = null;
        if (Capacity != 200 || parent.Type != 632 || parent.Simulation.Life != 0 ||
            mutationSerialExhausted || mutationSerial != expectedSerial || preview.Capacity != Capacity ||
            preview.CapturedDeathSpawnContext is not { IsValid: true } context ||
            !TryGet(parent.Handle, out var live) || live.Revision != parent.Revision ||
            parent.Revision.Value == ulong.MaxValue || !preview.TryGet(parent.Handle, out var retained) ||
            retained.Revision.Value != parent.Revision.Value + 1 || retained with { Revision = parent.Revision } != parent) return false;
        var slots = new List<int>(3);
        for (int slot = 0; slot < Capacity; slot++)
        {
            if (slot == parent.Handle.Slot || SameDeathSlot(_slots[slot], preview._slots[slot])) continue;
            if (slots.Count == 3 || !preview._slots[slot].Active || preview._slots[slot].Update.Type != 606) return false;
            slots.Add(slot);
        }
        // Accepted HP, each physical birth, then terminal removal are all reserved.
        if (expectedSerial > ulong.MaxValue - (ulong)slots.Count - 2UL) return false;
        preparation = new(this, live, expectedSerial, context, slots.ToArray(), preview);
        return true;
    }
}
