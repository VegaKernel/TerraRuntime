using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

// Authoritative: source AddBuff120 + BuffFlagsReset/SetFlags/ClearExpired, with exact generation ownership.
// No wire identity can create a generation; every mutation resolves the current authoritative NPC.
internal readonly record struct RuntimeNpcStinkyVisualOffer1458(bool Offered, float VelocityX, float VelocityY);

internal sealed class RuntimeNpcStinkyStatus1458(RuntimeNpcStore npcs, Action<NpcHandle>? buffListChanged = null)
{
    internal static RuntimeNpcStinkyVisualOffer1458 PlanVisualOffer(IRuntimeTownNpcCombatRandom1458 random)
    {
        if (random.Next(5) != 0) return default;
        float dx = random.Next(21) - 10, dy = random.Next(21) - 10;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        dx /= length; dy /= length;
        dx *= .66f; dy = MathF.Abs(dy);
        float multiplier = 3 + random.Next(2);
        // Source multiplication order is vector * integer * .25f, before Dust's dedicated-server return.
        return new(true, dx * multiplier * .25f, dy * multiplier * .25f * .5f);
    }
    private readonly Entry[] entries = new Entry[RuntimeNpcStore.MaximumAddressableCapacity];
    private struct Entry
    {
        internal NpcHandle Handle;
        internal int Duration;
        internal bool Present;
        internal bool PreviousFlag;
        internal bool Flag;
        internal RuntimeNpcStinkyVisualOffer1458 VisualOffer;
    }

    internal void BeginWorldTick(Action<NpcHandle>? expired = null)
    {
        for (int index = 0; index < entries.Length; index++)
        {
            if (!npcs.TryGetActive((byte)index, out NpcSnapshot live) || !IsSupported(in live))
            {
                entries[index] = default;
                continue;
            }
            ref Entry entry = ref entries[index];
            if (entry.Handle != live.Handle) entry = new Entry { Handle = live.Handle };
            entry.PreviousFlag = entry.Flag;
            entry.VisualOffer = default;
            // A buff with time1 flags this tick, then is removed from the source buff list.
            entry.Flag = entry.Present && entry.Duration > 0;
            if (entry.Duration > 0) entry.Duration--;
            if (entry.Present && entry.Duration <= 0)
            {
                entry.Present = false;
                expired?.Invoke(live.Handle);
            }
        }
    }

    internal bool ObserveVisualOffer(NpcHandle handle, in RuntimeNpcStinkyVisualOffer1458 offer)
    {
        if (!TryGetStinky(handle, out bool stinky) || !stinky) return false;
        entries[handle.Slot].VisualOffer = offer;
        return true;
    }
    internal bool TryGetVisualOffer(NpcHandle handle, out RuntimeNpcStinkyVisualOffer1458 offer)
    {
        offer = default;
        if (!EnsureGeneration(handle)) return false;
        offer = entries[handle.Slot].VisualOffer;
        return true;
    }
    internal bool TryRemoveWetStatus(NpcHandle handle)
    {
        if (!EnsureGeneration(handle) || !npcs.TryGet(handle, out NpcSnapshot npc)) return false;
        ref Entry entry = ref entries[handle.Slot];
        if (!npc.Simulation.Wet || !entry.Flag || !entry.Present) return true;
        entry.Present = false;
        entry.Duration = 0;
        buffListChanged?.Invoke(handle);
        return true;
    }

    internal void FinishWorldTick()
    {
        for (int slot = 0; slot < entries.Length; slot++)
            if (npcs.TryGetActive((byte)slot, out NpcSnapshot npc)) TryRemoveWetStatus(npc.Handle);
    }

    internal bool TryApplyRepeated(NpcHandle handle)
    {
        if (!EnsureGeneration(handle) || !npcs.TryGet(handle, out NpcSnapshot npc)) return false;
        if (VanillaNpcStinkyCatalog1458.IsImmune(npc.Type)) return true;
        if (!entries[handle.Slot].Present || entries[handle.Slot].Duration <= 10)
        {
            if (!TryApply(handle, 180)) return false;
            buffListChanged?.Invoke(handle);
        }
        return true;
    }

    internal bool TryApply(NpcHandle handle, int duration)
    {
        if (duration is < 0 or > short.MaxValue || !npcs.TryGet(handle, out NpcSnapshot npc) ||
            !IsSupported(in npc)) return false;
        ref Entry entry = ref entries[handle.Slot];
        if (entry.Handle != handle) entry = new Entry { Handle = handle };
        if (VanillaNpcStinkyCatalog1458.IsImmune(npc.Type)) return true; // AddBuff is ignored, packet53 still requests current-list54.
        if (!entry.Present || entry.Duration < duration)
        {
            entry.Present = true;
            entry.Duration = duration;
        }
        return true;
    }

    public bool TryGetStinky(NpcHandle handle, out bool stinky)
    {
        stinky = false;
        if (!EnsureGeneration(handle)) return false;
        stinky = entries[handle.Slot].Flag;
        return true;
    }

    public bool TryGetSourceOrderedStinky(NpcHandle viewer, NpcHandle peer, out bool stinky)
    {
        if (!TryGetStinky(peer, out stinky)) return false;
        if (peer.Slot > viewer.Slot) stinky = entries[peer.Slot].PreviousFlag;
        return true;
    }

    internal bool TryGetActiveWireDuration(byte slot, out int duration)
    {
        duration = 0;
        return npcs.TryGetActive(slot, out NpcSnapshot npc) && TryGetWireDuration(npc.Handle, out duration);
    }

    internal bool TryGetWireDuration(NpcHandle handle, out int duration)
    {
        duration = 0;
        if (!EnsureGeneration(handle)) return false;
        duration = entries[handle.Slot].Present ? entries[handle.Slot].Duration : -1;
        return true;
    }

    private static bool IsSupported(in NpcSnapshot npc) => npc.Type is >= 0 and < VanillaNpcStinkyCatalog1458.VerifiedNpcTypeCount &&
        VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out _);
    // Spawn registration and lookup resolve the actual authoritative handle; a recycled slot cannot inherit a buff.
    internal bool EnsureGeneration(NpcHandle handle)
    {
        if (!npcs.TryGet(handle, out NpcSnapshot npc) || !IsSupported(in npc)) return false;
        ref Entry entry = ref entries[handle.Slot];
        if (entry.Handle != handle) entry = new Entry { Handle = handle };
        return true;
    }
}
