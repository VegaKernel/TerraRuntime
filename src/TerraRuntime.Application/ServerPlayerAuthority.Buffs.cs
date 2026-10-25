using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    // Clientless players run the local-player Moon Leech timer. Remote client snapshots never write it.
    private readonly PlayerHandle[] moonLeechOwners;
    private readonly int[] moonLeechTicks;
    private readonly bool[] moonLeechBeforeBurning;
    // The same bounded slot implementation as connected players; the physical slot alone never owns an effect.
    private readonly PlayerHandle[] npcContactBuffOwners = new PlayerHandle[byte.MaxValue + 1];
    private readonly PlayerBuffState?[] npcContactBuffStates = new PlayerBuffState?[byte.MaxValue + 1];
    private readonly PlayerDebuffSnapshot1458[] npcContactDebuffEffects = new PlayerDebuffSnapshot1458[byte.MaxValue + 1];

    internal bool TryApplyNpcContactBuff(PlayerHandle player, BuffTypeId type, int duration, bool immune = false, bool publish = true)
    {
        if (immune || duration <= 0 || !VanillaNpcContactBuffs1458.IsContactBuffType(type) ||
            !states.TryGet(player, out var current) || current.IsDead || current.GodMode ||
            !current.HasHealth || current.Life <= 0) return false;

        int slot = player.Slot.Value;
        if (type == VanillaBuffIds.OnFire)
        {
            // NPC and lava OnFire share one timer, regeneration loss and liquid-extinguish owner.
            // A contact must not create a second burning clock or reset an exhausted lava gauge.
            ref LavaState lava = ref lavaStates[slot];
            if (lava.Owner != player)
            {
                if (!TryCaptureCombatSnapshot(player, out var equipment)) return false;
                lava = new LavaState { Owner = player, Protection = equipment.LavaProtectionTicks };
            }
            lava.BurningTicks = Math.Max(lava.BurningTicks, duration);
            PublishBurning(ref lava, publish);
            return true;
        }

        if (npcContactBuffOwners[slot] != player)
        {
            npcContactBuffOwners[slot] = player;
            npcContactBuffStates[slot] = new PlayerBuffState();
            npcContactDebuffEffects[slot] = default;
        }
        PlayerBuffState buffs = npcContactBuffStates[slot] ??= new PlayerBuffState();
        bool added = buffs.GetDuration(type) == 0;
        if (!buffs.TryApplyNpcContactBuff(type, duration)) return false;
        if (added && publish) PublishBuffTypes(player);
        return true;
    }

    private bool ApplyNpcContactBuffs(PlayerHandle player, in NpcContactBuffSource1458 source,
        in VanillaPlayerCombatSnapshot combat, bool expertMode, bool masterMode)
    {
        if (!states.TryGet(player, out var current) || current.IsDead || current.GodMode || current.Life <= 0)
            return false;
        Span<NpcContactBuff1458> effects = stackalloc NpcContactBuff1458[VanillaNpcContactBuffs1458.MaximumEffects];
        bool statusExpert = expertMode || source.GoodWorld;
        bool statusMaster = masterMode || expertMode && source.GoodWorld;
        int count = VanillaNpcContactBuffs1458.Evaluate(source.Type, source.NetId, source.Ai1,
            statusExpert, npcContactBuffOwners[player.Slot.Value] == player &&
                npcContactBuffStates[player.Slot.Value]?.IsNpcContactFrozen == true,
            source.GoodWorld, source.Random, effects);
        if (!states.TryGet(player, out var afterRandom) || afterRandom != current) return false;
        bool applied = false;
        for (int i = 0; i < count; i++)
        {
            NpcContactBuff1458 effect = effects[i];
            int duration = VanillaNpcContactBuffs1458.ResolveDuration(effect.Type, effect.DurationTicks,
                statusExpert, statusMaster, resistCold: false);
            applied |= TryApplyNpcContactBuff(player, effect.Type, duration,
                combat.IsImmuneToNpcContactBuff(effect.Type), publish: false);
        }
        return applied;
    }

    internal bool TryApplySlimeContactBuff(PlayerHandle player, in NpcContactBuffSource1458 source,
        bool expertMode, bool masterMode)
    {
        if (!VanillaNpcContactBuffs1458.IsSlimeContactOnly(source.Type, source.Ai1) ||
            !states.TryGet(player, out var current) || current.IsDead || current.GodMode || current.Life <= 0 ||
            !TryCaptureCombatSnapshot(player, out var combat)) return false;
        bool applied = TryApplyNpcContactBuff(player, new BuffTypeId(48), 1800,
            combat.IsImmuneToNpcContactBuff(new BuffTypeId(48)), publish: false);
        if (applied) PublishBuffTypes(player);
        return applied;
    }

    internal int GetNpcContactBuffDuration(PlayerHandle player, BuffTypeId type)
    {
        if (!states.TryGet(player, out _)) return 0;
        int slot = player.Slot.Value;
        if (type == VanillaBuffIds.OnFire)
            return lavaStates[slot].Owner == player ? lavaStates[slot].BurningTicks : 0;
        return npcContactBuffOwners[slot] == player ? npcContactBuffStates[slot]?.GetDuration(type) ?? 0 : 0;
    }

    internal bool TryApplyMoonLeech(PlayerHandle player, int duration)
    {
        if (duration <= 0 || !states.TryGet(player, out var current) || current.GodMode) return false;
        int slot = player.Slot.Value;
        if (moonLeechOwners[slot] != player)
        {
            moonLeechOwners[slot] = player;
            moonLeechTicks[slot] = 0;
        }
        bool added = moonLeechTicks[slot] == 0;
        if (added) moonLeechBeforeBurning[slot] = lavaStates[slot].Owner != player || lavaStates[slot].BurningTicks == 0;
        moonLeechTicks[slot] = Math.Max(moonLeechTicks[slot], duration);
        if (added) PublishBuffTypes(player);
        return true;
    }

    internal int GetMoonLeechDuration(PlayerHandle player) =>
        states.TryGet(player, out _) && moonLeechOwners[player.Slot.Value] == player
            ? moonLeechTicks[player.Slot.Value] : 0;

    internal void TickBuffs()
    {
        for (int slot = 0; slot < npcContactBuffStates.Length; slot++)
        {
            if (npcContactBuffStates[slot] is not { } buffs) continue;
            PlayerHandle player = npcContactBuffOwners[slot];
            if (!states.TryGet(player, out var current))
            {
                npcContactBuffStates[slot] = null;
                npcContactBuffOwners[slot] = default;
                npcContactDebuffEffects[slot] = default;
                continue;
            }
            // UpdateBuffs writes effects before decrementing the slot. Retain the last active update for regen.
            npcContactDebuffEffects[slot] = current.IsDead ? default : buffs.CaptureOwnedNpcContactDebuffs();
            bool changed = current.IsDead ? buffs.ClearNonPersistentOnDeath() : buffs.TickNpcContactBuffs();
            if (changed) PublishBuffTypes(player);
        }
        for (int slot = 0; slot < moonLeechTicks.Length; slot++)
        {
            if (moonLeechTicks[slot] == 0) continue;
            PlayerHandle player = moonLeechOwners[slot];
            if (!states.TryGet(player, out var current))
            {
                moonLeechTicks[slot] = 0;
                moonLeechOwners[slot] = default;
                continue;
            }
            if (current.IsDead) moonLeechTicks[slot] = 0;
            else moonLeechTicks[slot]--;
            if (moonLeechTicks[slot] == 0) PublishBuffTypes(player);
        }
    }

    private void PublishBuffTypes(PlayerHandle player)
    {
        if (!states.TryGet(player, out _)) return;
        // NPC contact, lava and Moon Leech contribute to one packet50. The admitted contact type set plus
        // the two legacy effects fits the 44 source slots; no source may erase another's live presence.
        Span<BuffTypeId> buffs = stackalloc BuffTypeId[PlayerBuffState.Capacity];
        int count = 0;
        ref LavaState lava = ref lavaStates[player.Slot.Value];
        bool burning = lava.Owner == player && lava.BurningTicks > 0;
        bool leech = GetMoonLeechDuration(player) > 0;
        bool leechFirst = moonLeechBeforeBurning[player.Slot.Value];
        if (leech && leechFirst) buffs[count++] = VanillaBuffIds.MoonLeech;
        if (burning) buffs[count++] = VanillaBuffIds.OnFire;
        if (leech && !leechFirst) buffs[count++] = VanillaBuffIds.MoonLeech;
        int slot = player.Slot.Value;
        if (npcContactBuffOwners[slot] == player && npcContactBuffStates[slot] is { } contact)
            foreach (BuffTypeId type in contact.CaptureTypes())
                if (!buffs[..count].Contains(type) && count < buffs.Length) buffs[count++] = type;
        events?.ServerPlayerBuffTypesUpdated(player, buffs[..count]);
    }

    private bool ResetPlayerBuffs(PlayerHandle player, bool publish = true)
    {
        int slot = player.Slot.Value;
        bool changed = false;
        if (npcContactBuffOwners[slot] == player && npcContactBuffStates[slot] is { } contact)
        {
            changed = contact.ClearNonPersistentOnDeath();
            npcContactBuffStates[slot] = null;
            npcContactBuffOwners[slot] = default;
            npcContactDebuffEffects[slot] = default;
        }
        if (moonLeechOwners[slot] == player)
        {
            changed |= moonLeechTicks[slot] > 0;
            moonLeechTicks[slot] = 0;
        }
        if (lavaStates[slot].Owner == player)
        {
            changed |= lavaStates[slot].PublishedBurning;
            lavaStates[slot] = default;
        }
        if (changed && publish) PublishBuffTypes(player);
        return changed;
    }
}
