using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Application;

internal sealed partial class PlayerBuffState
{
    // Provenance belongs to the actual slot, beside its existing timer. Packet 50 cannot grant,
    // refresh or remove these effects. There is no second contact-buff duration ledger.
    private readonly bool[] ownedNpcContact = new bool[Capacity];
    private PlayerDebuffSnapshot1458 contactDebuffsForUpdate;
    private bool contactFrozenForUpdate;
    private bool contactNoItemsForUpdate;
    private bool contactCrowdControlForUpdate;
    internal bool HasOwnedNpcContactBuffs => contactEffectTypesForUpdate is not null || ownedNpcContact.Contains(true) || contactDebuffsForUpdate != default ||
        contactFrozenForUpdate || contactNoItemsForUpdate || contactCrowdControlForUpdate;
    internal bool IsNpcContactFrozen => contactFrozenForUpdate || ContainsOwnedNpcContact(VanillaBuffIds.Frozen);
    internal PlayerDebuffSnapshot1458 CaptureOwnedNpcContactDebuffs() =>
        new(ContainsOwnedNpcContact(VanillaBuffIds.OnFire), ContainsOwnedNpcContact(VanillaBuffIds.CursedInferno),
            ContainsOwnedNpcContact(VanillaBuffIds.Poisoned))
            { Electrified = ContainsOwnedNpcContact(VanillaBuffIds.Electrified) };
    internal bool ContainsOwnedNpcContact(BuffTypeId type)
    {
        for (int i = 0; i < Capacity; i++)
            if (ownedNpcContact[i] && types[i] == type && durations[i] > 0) return true;
        return false;
    }

    internal bool TryApplyNpcContactBuff(BuffTypeId type, int duration, bool immune = false)
    {
        if (immune || duration <= 0 || !VanillaNpcContactBuffs1458.IsContactBuffType(type)) return false;
        int previous = Array.IndexOf(types, type);
        // A packet50 presentation timer is not an earned AddBuff duration to carry forward.
        if (previous >= 0 && !ownedNpcContact[previous]) durations[previous] = 0;
        // Reuse AddBuff's existing insertion/eviction/refresh mechanics; contact effects have
        // neither the fed-state replacement nor additive Mana Sickness special cases.
        if (!TryApplySelectedConsumableCore(type, duration, false, npcContact: true)) return false;
        int slot = Array.IndexOf(types, type);
        if (slot < 0) return false;
        ownedNpcContact[slot] = true;
        InvalidateFoodClockLineage();
        return true;
    }

    internal bool TickNpcContactBuffs()
    {
        bool hadEffects = HasOwnedNpcContactBuffs;
        bool changed = false;
        CaptureContactEffectsBeforeCountdown();
        contactDebuffsForUpdate = CaptureOwnedNpcContactDebuffs();
        contactFrozenForUpdate = ContainsOwnedNpcContact(VanillaBuffIds.Frozen);
        // The final positive UpdateBuffs iteration still supplies its effects
        // after DelBuff removes the timer. The next update clears these facts.
        contactNoItemsForUpdate = ContainsOwnedNpcContact(VanillaBuffIds.Cursed);
        contactCrowdControlForUpdate = contactFrozenForUpdate ||
            ContainsOwnedNpcContact(VanillaBuffIds.Webbed) || ContainsOwnedNpcContact(VanillaBuffIds.Stoned);
        // Age every slot once before compaction; holes must not move an already-aged slot into the loop again.
        for (int slot = 0; slot < Capacity; slot++)
            if (ownedNpcContact[slot] && durations[slot] > 0) durations[slot]--;
        for (int slot = Capacity - 1; slot >= 0; slot--)
            if (ownedNpcContact[slot] && durations[slot] == 0)
            {
                // DelBuff's unusual final-slot rule is shared with the existing food owner.
                RemoveFedStateSlot(slot);
                changed = true;
            }
        // A pending food transaction may not rewind a contact timer that advanced meanwhile.
        if (hadEffects || changed) InvalidateFoodClockLineage();
        return changed;
    }
}
