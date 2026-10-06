using System.Runtime.CompilerServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeNpcStinkyVisualOffer1458(bool Offered, float VelocityX, float VelocityY);
internal readonly record struct RuntimeNpcBuffSlot1458(ushort Type, int Duration);
[InlineArray(RuntimeNpcBuffStatus1458.Capacity)]
internal struct RuntimeNpcBuffSlots1458 { private RuntimeNpcBuffSlot1458 element; }
internal readonly record struct RuntimeNpcBuffPlan1458(NpcSnapshot Expected, ulong StatusRevision,
    RuntimeNpcBuffSlots1458 Slots, PlayerDebuffSnapshot1458 Flags, bool Stinky, bool Expired,
    int LifeAfter, int? CounterAfter, int DotDamage);

// One generation-owned source NPC.maxBuffs table. Preview never emits54, ages live slots or draws RNG.
internal sealed partial class RuntimeNpcBuffStatus1458(RuntimeNpcStore npcs, Action<NpcHandle>? buffListChanged = null)
{
    internal const int Capacity = 20;
    private readonly Entry[] entries = new Entry[RuntimeNpcStore.MaximumAddressableCapacity];
    private struct Entry
    {
        internal NpcHandle Handle;
        internal ulong Revision;
        internal RuntimeNpcBuffSlots1458 Slots;
        internal PlayerDebuffSnapshot1458 Flags;
        internal PlayerDebuffSnapshot1458 PreviousFlags;
        internal bool Stinky;
        internal bool PreviousStinky;
        internal RuntimeNpcStinkyVisualOffer1458 VisualOffer;
        internal int DefinitionType;
        internal int DefinitionNetId;
        internal bool TorchImmunity;
    }

    internal static RuntimeNpcStinkyVisualOffer1458 PlanVisualOffer(IRuntimeTownNpcCombatRandom1458 random)
    {
        if (random.Next(5) != 0) return default;
        float dx = random.Next(21) - 10, dy = random.Next(21) - 10;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        dx /= length; dy /= length; dx *= .66f; dy = MathF.Abs(dy);
        float multiplier = 3 + random.Next(2);
        return new(true, dx * multiplier * .25f, dy * multiplier * .25f * .5f);
    }

    internal static RuntimeNpcStinkyVisualOffer1458 PlanVisualOffers(in RuntimeNpcBuffPlan1458 plan,
        IRuntimeTownNpcCombatRandom1458 random, bool goodWorld)
    {
        int type = plan.Expected.Type;
        if (goodWorld && (type == VanillaNpcIds.Golem.Value || type == VanillaNpcIds.GolemHead.Value ||
            type == VanillaNpcIds.GolemFistLeft.Value || type == VanillaNpcIds.GolemFistRight.Value)) return default;
        if (plan.Flags.Poisoned) _ = random.Next(30);
        if (plan.Flags.OnFire && random.Next(4) < 3) _ = random.Next(4);
        return plan.Stinky ? PlanVisualOffer(random) : default;
    }

    internal void BeginWorldTick()
    {
        // Flag views are prepared without consuming timers; physical actor admission owns expiry publication.
        for (int slot = 0; slot < entries.Length; slot++)
        {
            if (!npcs.TryGetActive((byte)slot, out var npc) || !IsSupported(in npc))
            { entries[slot] = default; continue; }
            EnsureGeneration(npc.Handle);
            ref Entry entry = ref entries[slot];
            entry.PreviousFlags = entry.Flags; entry.PreviousStinky = entry.Stinky; entry.VisualOffer = default;
            ReadFlags(in entry.Slots, out entry.Flags, out entry.Stinky);
        }
    }

    internal bool TryPlan(in NpcSnapshot before, out RuntimeNpcBuffPlan1458 plan, bool goodWorld = false,
        bool allowLethal = false)
    {
        plan = default;
        if (!EnsureGeneration(before.Handle) || !npcs.TryGet(before.Handle, out var current) || current != before) return false;
        ref Entry entry = ref entries[before.Handle.Slot];
        if (entry.Revision == ulong.MaxValue) return false;
        var slots = entry.Slots;
        ReadFlags(in slots, out var flags, out bool stinky);
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Type > 0 && slots[i].Duration > 0) slots[i] = slots[i] with { Duration = slots[i].Duration - 1 };
        bool expired = false;
        // Source increments its loop after DelBuff compacts; don't manufacture a reverse sweep.
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Type > 0 && slots[i].Duration <= 0) { RemoveAt(ref slots, i); expired = true; }
        int life = before.Simulation.Life, damage = 0;
        int? counter = before.Simulation.LifeRegenCounter;
        bool torch = before.TypeIdentity == VanillaNpcIds.BlueSlime &&
            before.Ai.Ai1 == VanillaItemIds.Torch.Value && goodWorld;
        bool slime = before.TypeIdentity == VanillaNpcIds.BlueSlime || before.TypeIdentity == VanillaNpcIds.LavaSlime;
        if (slime)
        {
            if (!VanillaSlimeRegeneration1458.TryStep(in before, goodWorld, flags.Poisoned, flags.OnFire, out var regen))
                return false;
            life = regen.Life;
            counter = regen.Counter;
            damage = regen.Damage;
            if (life <= 0 && !allowLethal) return false;
        }
        else if (!before.Simulation.DontTakeDamage && (flags.Poisoned || flags.OnFire && !torch))
        {
            // This owned ordinary branch has no positive regen, acceleration, realLife or dripping modifiers.
            if (!IsDotResident(before.Type) || before.Simulation.Immortal is not { } immortal ||
                counter is not { } count || count is < -119 or > 119 || before.Simulation.Wet) return false;
            count -= (flags.Poisoned ? 12 : 0) + (flags.OnFire && !torch ? 8 : 0);
            while (count <= -120) { count += 120; damage++; }
            counter = count;
            if (!immortal) life -= damage;
            // Town death chat/tombstones remain unowned. Reject before slots,54,counters or RNG change.
            if (life <= 0 && !allowLethal) return false;
        }
        plan = new(before, entry.Revision, slots, flags, stinky, expired, life, counter, damage);
        return true;
    }

    internal bool IsCurrent(in RuntimeNpcBuffPlan1458 plan) =>
        npcs.TryGet(plan.Expected.Handle, out var current) && current == plan.Expected &&
        entries[plan.Expected.Handle.Slot].Handle == plan.Expected.Handle &&
        entries[plan.Expected.Handle.Slot].Revision == plan.StatusRevision;

    internal bool Commit(in RuntimeNpcBuffPlan1458 plan, in NpcSnapshot committed)
    {
        if (!npcs.TryGet(committed.Handle, out var current) || current != committed ||
            committed.Handle != plan.Expected.Handle || committed.Revision.Value < plan.Expected.Revision.Value ||
            committed.Simulation.LifeRegenCounter != plan.CounterAfter) return false;
        ref Entry entry = ref entries[committed.Handle.Slot];
        if (entry.Handle != committed.Handle || entry.Revision != plan.StatusRevision || entry.Revision == ulong.MaxValue) return false;
        entry.Slots = plan.Slots; entry.Flags = plan.Flags; entry.Stinky = plan.Stinky; entry.Revision++;
        return true;
    }

    internal void PublishExpired(in RuntimeNpcBuffPlan1458 plan)
    { if (plan.Expired) buffListChanged?.Invoke(plan.Expected.Handle); }

    internal bool ObserveVisualOffer(NpcHandle handle, in RuntimeNpcStinkyVisualOffer1458 offer)
    { if (!EnsureGeneration(handle)) return false; entries[handle.Slot].VisualOffer = offer; return true; }
    internal bool TryGetVisualOffer(NpcHandle handle, out RuntimeNpcStinkyVisualOffer1458 offer)
    { offer = default; if (!EnsureGeneration(handle)) return false; offer = entries[handle.Slot].VisualOffer; return true; }

    internal bool TryApply(NpcHandle handle, int duration) => TryApply(handle, VanillaBuffIds.Stinky, duration);
    internal bool TryApply(NpcHandle handle, BuffTypeId type, int duration)
    {
        if (duration is < 0 or > short.MaxValue || !EnsureGeneration(handle) || !npcs.TryGet(handle, out var npc) ||
            type != VanillaBuffIds.Stinky && type != VanillaBuffIds.Poisoned && type != VanillaBuffIds.OnFire) return false;
        if (type != VanillaBuffIds.Stinky && !IsDotResident(npc.Type)) return false;
        if ((npc.TypeIdentity == VanillaNpcIds.BlueSlime || npc.TypeIdentity == VanillaNpcIds.LavaSlime) &&
            (type == VanillaBuffIds.Poisoned || type == VanillaBuffIds.OnFire && npc.TypeIdentity == VanillaNpcIds.LavaSlime))
            return true;
        if (type == VanillaBuffIds.Stinky && VanillaNpcStinkyCatalog1458.IsImmune(npc.Type)) return true;
        ref Entry entry = ref entries[handle.Slot];
        if (entry.Revision == ulong.MaxValue) return false;
        int free = -1;
        for (int i = 0; i < Capacity; i++)
        {
            if (entry.Slots[i].Type == type.Value)
            {
                if (entry.Slots[i].Duration >= duration) return true;
                entry.Slots[i] = new((ushort)type.Value, duration); entry.Revision++; return true;
            }
            if (free < 0 && entry.Slots[i].Type == 0) free = i;
        }
        // All three admitted identities are source debuffs; a full all-debuff table has no victim.
        if (free < 0) return true;
        entry.Slots[free] = new((ushort)type.Value, duration); entry.Revision++; return true;
    }

    internal bool TryApplyRepeated(NpcHandle handle)
    {
        if (!TryGetWireDuration(handle, out int duration)) return false;
        if (duration > 10) return true;
        if (!TryApply(handle, 180)) return false;
        buffListChanged?.Invoke(handle); return true;
    }

    internal bool TryRemoveWetStatus(NpcHandle handle)
    {
        if (!EnsureGeneration(handle) || !npcs.TryGet(handle, out var npc)) return false;
        ref Entry entry = ref entries[handle.Slot];
        if (!npc.Simulation.Wet) return true;
        if (entry.Revision == ulong.MaxValue) return false;
        bool changed = false;
        for (int i = 0; i < Capacity; i++)
            if (entry.Slots[i].Type == VanillaBuffIds.Stinky.Value && entry.Stinky ||
                entry.Slots[i].Type == VanillaBuffIds.OnFire.Value && entry.Flags.OnFire &&
                npc.Simulation.LiquidContact != NpcLiquidContactKind.Lava)
            { RemoveAt(ref entry.Slots, i); changed = true; }
        if (changed)
        {
            entry.Revision++;
            // Commit all removals before invoking a sink that can replace this physical slot.
            buffListChanged?.Invoke(handle);
        }
        return true;
    }
    internal void FinishWorldTick()
    { for (int slot = 0; slot < entries.Length; slot++) if (npcs.TryGetActive((byte)slot, out var npc)) TryRemoveWetStatus(npc.Handle); }

    public bool TryGetStinky(NpcHandle handle, out bool stinky)
    { stinky = false; if (!EnsureGeneration(handle)) return false; stinky = entries[handle.Slot].Stinky; return true; }
    internal bool TryGetDebuffs(NpcHandle handle, out PlayerDebuffSnapshot1458 flags)
    { flags = default; if (!EnsureGeneration(handle)) return false; flags = entries[handle.Slot].Flags; return true; }
    internal bool TryGetSourceOrderedDebuffs(NpcHandle viewer, NpcHandle peer, out PlayerDebuffSnapshot1458 flags)
    {
        if (!TryGetDebuffs(peer, out flags)) return false;
        if (peer.Slot > viewer.Slot) flags = entries[peer.Slot].PreviousFlags;
        return true;
    }
    public bool TryGetSourceOrderedStinky(NpcHandle viewer, NpcHandle peer, out bool stinky)
    { if (!TryGetStinky(peer, out stinky)) return false; if (peer.Slot > viewer.Slot) stinky = entries[peer.Slot].PreviousStinky; return true; }
    internal bool TryGetActiveWireDuration(byte slot, out int duration)
    { duration = 0; return npcs.TryGetActive(slot, out var npc) && TryGetWireDuration(npc.Handle, out duration); }
    internal bool TryGetWireDuration(NpcHandle handle, out int duration)
    {
        duration = -1; if (!EnsureGeneration(handle)) return false;
        for (int i = 0; i < Capacity; i++) if (entries[handle.Slot].Slots[i].Type == VanillaBuffIds.Stinky.Value)
        { duration = entries[handle.Slot].Slots[i].Duration; break; }
        return true;
    }
    internal bool TryCopyWireBuffs(NpcHandle handle, Span<TerrariaNpcBuffEntryState> destination, out int count)
    {
        count = 0; if (destination.Length < Capacity || !EnsureGeneration(handle)) return false;
        for (int i = 0; i < Capacity; i++)
        {
            var slot = entries[handle.Slot].Slots[i];
            if (slot.Type > 0 && slot.Duration > 0) destination[count++] = new(slot.Type, (ushort)slot.Duration);
        }
        return true;
    }
    internal bool TryCopyActiveWireBuffs(byte slot, Span<TerrariaNpcBuffEntryState> destination, out int count)
    {
        count = 0;
        return npcs.TryGetActive(slot, out var npc) && TryCopyWireBuffs(npc.Handle, destination, out count);
    }
    private static bool IsDotResident(int type) => type == VanillaNpcIds.Merchant.Value ||
        type == VanillaNpcIds.Nurse.Value || type == VanillaNpcIds.ArmsDealer.Value ||
        type == VanillaNpcIds.Guide.Value || type == VanillaNpcIds.DyeTrader.Value ||
        type == VanillaNpcIds.Stylist.Value || type == VanillaNpcIds.TaxCollector.Value ||
        type == VanillaNpcIds.BlueSlime.Value || type == VanillaNpcIds.LavaSlime.Value;
    private static void ReadFlags(in RuntimeNpcBuffSlots1458 slots, out PlayerDebuffSnapshot1458 flags, out bool stinky)
    {
        bool fire = false, poison = false; stinky = false;
        for (int i = 0; i < Capacity; i++) if (slots[i].Duration > 0)
        { fire |= slots[i].Type == VanillaBuffIds.OnFire.Value; poison |= slots[i].Type == VanillaBuffIds.Poisoned.Value; stinky |= slots[i].Type == VanillaBuffIds.Stinky.Value; }
        flags = new(fire, false, poison);
    }
    private static void RemoveAt(ref RuntimeNpcBuffSlots1458 slots, int index)
    { for (int i = index; i < Capacity - 1; i++) slots[i] = slots[i + 1]; slots[Capacity - 1] = default; }
    private static bool IsSupported(in NpcSnapshot npc) => npc.Type is >= 0 and < VanillaNpcStinkyCatalog1458.VerifiedNpcTypeCount &&
        VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) && !definition.DefinitionOnly;
    internal bool EnsureGeneration(NpcHandle handle)
    {
        if (!npcs.TryGet(handle, out var npc) || !IsSupported(in npc)) return false;
        if (entries[handle.Slot].Handle != handle)
            entries[handle.Slot] = new Entry { Handle = handle, Revision = 1,
                DefinitionType = npc.Type, DefinitionNetId = npc.NetId };
        else if (entries[handle.Slot].DefinitionType != npc.Type || entries[handle.Slot].DefinitionNetId != npc.NetId)
        {
            ref Entry entry = ref entries[handle.Slot];
            entry.DefinitionType = npc.Type;
            entry.DefinitionNetId = npc.NetId;
            entry.TorchImmunity = false;
            if (entry.Revision != ulong.MaxValue) entry.Revision++;
        }
        return true;
    }
}
