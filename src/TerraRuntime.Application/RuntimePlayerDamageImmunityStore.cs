using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

/// <summary>
/// Owns generation-scoped incoming-damage immunity deadlines for connection-owned players.
/// Slot reuse never inherits immunity from a previous player generation.
/// </summary>
internal sealed class RuntimePlayerDamageImmunityStore
{
    private readonly long[] pvpUntil;
    private readonly PlayerSessionGeneration[] pvpGeneration;
    private readonly long[] pveGeneralUntil;
    private readonly long[] pveBossNoCheeseUntil;
    private readonly PlayerSessionGeneration[] pveGeneration;

    public RuntimePlayerDamageImmunityStore(int capacity)
    {
        if (capacity <= 0 || capacity > byte.MaxValue + 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        pvpUntil = new long[capacity];
        pvpGeneration = new PlayerSessionGeneration[capacity];
        pveGeneralUntil = new long[capacity];
        pveBossNoCheeseUntil = new long[capacity];
        pveGeneration = new PlayerSessionGeneration[capacity];
    }

    public bool IsPvpImmune(PlayerHandle player, long tick)
    {
        int slot = player.Slot.Value;
        return pvpGeneration[slot] == player.Generation && tick < pvpUntil[slot];
    }

    public void RecordPvp(PlayerHandle player, long immuneUntil)
    {
        int slot = player.Slot.Value;
        pvpGeneration[slot] = player.Generation;
        pvpUntil[slot] = immuneUntil;
    }

    public bool IsPveImmune(
        PlayerHandle player,
        VanillaPlayerImmunityChannel1458 channel,
        long tick)
    {
        int slot = player.Slot.Value;
        if (pveGeneration[slot] != player.Generation)
            return false;

        long immuneUntil = channel == VanillaPlayerImmunityChannel1458.BossNoCheese
            ? pveBossNoCheeseUntil[slot]
            : pveGeneralUntil[slot];
        return tick < immuneUntil;
    }

    public void RecordPve(
        PlayerHandle player,
        VanillaPlayerImmunityChannel1458 channel,
        long immuneUntil)
    {
        int slot = player.Slot.Value;
        if (pveGeneration[slot] != player.Generation)
        {
            pveGeneration[slot] = player.Generation;
            pveGeneralUntil[slot] = 0;
            pveBossNoCheeseUntil[slot] = 0;
        }

        if (channel == VanillaPlayerImmunityChannel1458.BossNoCheese)
            pveBossNoCheeseUntil[slot] = immuneUntil;
        else
            pveGeneralUntil[slot] = immuneUntil;
    }

    public void ResetPvp(PlayerSlotId slot)
    {
        int index = slot.Value;
        pvpUntil[index] = 0;
        pvpGeneration[index] = default;
    }
}
