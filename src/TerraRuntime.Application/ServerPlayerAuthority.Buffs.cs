using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    // Clientless players run the local-player Moon Leech timer. Remote client snapshots never write it.
    private readonly PlayerHandle[] moonLeechOwners;
    private readonly int[] moonLeechTicks;
    private readonly bool[] moonLeechBeforeBurning;

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
        // Existing lava and Moon Leech owners contribute to one packet-50 snapshot. Neither source may
        // erase the other's live effect when it refreshes or expires.
        Span<BuffTypeId> buffs = stackalloc BuffTypeId[2];
        int count = 0;
        ref LavaState lava = ref lavaStates[player.Slot.Value];
        bool burning = lava.Owner == player && lava.BurningTicks > 0;
        bool leech = GetMoonLeechDuration(player) > 0;
        bool leechFirst = moonLeechBeforeBurning[player.Slot.Value];
        if (leech && leechFirst) buffs[count++] = VanillaBuffIds.MoonLeech;
        if (burning) buffs[count++] = VanillaBuffIds.OnFire;
        if (leech && !leechFirst) buffs[count++] = VanillaBuffIds.MoonLeech;
        events?.ServerPlayerBuffTypesUpdated(player, buffs[..count]);
    }

    private void ResetPlayerBuffs(PlayerHandle player)
    {
        int slot = player.Slot.Value;
        bool changed = false;
        if (moonLeechOwners[slot] == player)
        {
            changed = moonLeechTicks[slot] > 0;
            moonLeechTicks[slot] = 0;
        }
        if (lavaStates[slot].Owner == player)
        {
            changed |= lavaStates[slot].PublishedBurning;
            lavaStates[slot] = default;
        }
        if (changed) PublishBuffTypes(player);
    }
}
