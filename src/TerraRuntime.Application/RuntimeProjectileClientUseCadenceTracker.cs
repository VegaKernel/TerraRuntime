using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Generation-aware authoritative item-use cadence for client projectile requests and prepared trusted Bot item uses.
/// Slot reuse must never inherit cooldown state from a disconnected player generation.
/// </summary>
internal sealed class RuntimeProjectileClientUseCadenceTracker
{
    private readonly long[] lastUseTick = new long[byte.MaxValue + 1];
    private readonly int[] lastUseTimeTicks = new int[byte.MaxValue + 1];
    private readonly PlayerSessionGeneration[] generations = new PlayerSessionGeneration[byte.MaxValue + 1];

    internal RuntimeProjectileClientUseCadenceTracker()
    {
        Array.Fill(lastUseTick, long.MinValue);
    }

    internal bool IsOnCooldown(PlayerHandle player, long tick)
    {
        int slot = player.Slot.Value;
        if (generations[slot] != player.Generation)
            return false;

        long previous = lastUseTick[slot];
        // Player.ApplyItemTime stores the accepted item's time. Selecting another item
        // does not replace that outstanding clock with the incoming weapon's useTime.
        return previous != long.MinValue && tick - previous < lastUseTimeTicks[slot];
    }

    internal void MarkUse(PlayerHandle player, long tick, int useTimeTicks)
    {
        int slot = player.Slot.Value;
        generations[slot] = player.Generation;
        lastUseTick[slot] = tick;
        lastUseTimeTicks[slot] = useTimeTicks;
    }
}
