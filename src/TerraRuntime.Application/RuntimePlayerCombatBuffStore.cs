using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime;

/// <summary>
/// Generation-safe server-owned lifetime store for the admitted combat/status subset of player buffs. Network packet
/// 50 is deliberately not an input to this store: gameplay systems must grant a buff only after the server has
/// validated the source that created it.
/// </summary>
internal sealed class RuntimePlayerCombatBuffStore
{
    private readonly Dictionary<PlayerHandle, Dictionary<int, long>> active = [];

    public bool TryGrant(PlayerHandle player, BuffTypeId type, int durationTicks, long currentTick)
    {
        if (!player.IsAssigned || durationTicks <= 0 || currentTick < 0 || !VanillaPlayerCombatBuffCatalog.IsSupported(type))
            return false;

        if (!active.TryGetValue(player, out Dictionary<int, long>? buffs))
        {
            buffs = [];
            active.Add(player, buffs);
        }

        // Player.AddBuff makes fed states mutually exclusive.
        if (type == VanillaBuffIds.WellFed || type == VanillaBuffIds.WellFed2 || type == VanillaBuffIds.WellFed3 ||
            type == VanillaBuffIds.NeutralHunger || type == VanillaBuffIds.Hunger || type == VanillaBuffIds.Starving)
        {
            buffs.Remove(VanillaBuffIds.WellFed.Value);
            buffs.Remove(VanillaBuffIds.WellFed2.Value);
            buffs.Remove(VanillaBuffIds.WellFed3.Value);
            buffs.Remove(VanillaBuffIds.NeutralHunger.Value);
            buffs.Remove(VanillaBuffIds.Hunger.Value);
            buffs.Remove(VanillaBuffIds.Starving.Value);
        }

        long expiresAt = durationTicks > long.MaxValue - currentTick
            ? long.MaxValue
            : currentTick + durationTicks;
        if (!buffs.TryGetValue(type.Value, out long previousExpiry) || previousExpiry < expiresAt)
            buffs[type.Value] = expiresAt;
        return true;
    }

    public bool TryCopyActive(PlayerHandle player, long currentTick, Span<BuffTypeId> destination, out int count)
    {
        count = 0;
        if (!player.IsAssigned || currentTick < 0)
            return false;
        if (!active.TryGetValue(player, out Dictionary<int, long>? buffs))
            return true;

        List<int>? expired = null;
        foreach ((int rawType, long expiresAt) in buffs)
        {
            if (expiresAt <= currentTick)
            {
                (expired ??= []).Add(rawType);
                continue;
            }
            if (count >= destination.Length || !VanillaBuffIds.TryCreate(rawType, out BuffTypeId type))
                return false;
            destination[count++] = type;
        }

        if (expired is not null)
        {
            for (int i = 0; i < expired.Count; i++)
                buffs.Remove(expired[i]);
            if (buffs.Count == 0)
                active.Remove(player);
        }
        return true;
    }

    public bool IsActiveForStatusUpdate(PlayerHandle player, BuffTypeId type, long currentTick)
    {
        if (!player.IsAssigned || currentTick < 0 || !active.TryGetValue(player, out Dictionary<int, long>? buffs) ||
            !buffs.TryGetValue(type.Value, out long expiresAt))
        {
            return false;
        }

        // Player.UpdateBuffs decrements buffTime before applying the current buff body, so a buff at time=1 still
        // contributes to that update. The ordinary combat snapshot uses expiresAt > currentTick; status-tick effects
        // need this inclusive edge to reproduce the final vanilla update after an on-hit AddBuff.
        if (expiresAt >= currentTick)
            return true;

        buffs.Remove(type.Value);
        if (buffs.Count == 0)
            active.Remove(player);
        return false;
    }

    public bool Remove(PlayerHandle player, BuffTypeId type)
    {
        if (!player.IsAssigned || !active.TryGetValue(player, out Dictionary<int, long>? buffs))
            return false;
        bool removed = buffs.Remove(type.Value);
        if (buffs.Count == 0)
            active.Remove(player);
        return removed;
    }

    public void Clear(PlayerHandle player)
    {
        if (player.IsAssigned)
            active.Remove(player);
    }
}
