using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

/// <summary>
/// Owns the bounded per-player packet-17 tile-edit admission state for one world runtime.
/// This object is thread-affine to the authoritative world owner; it does not synchronize internally.
/// </summary>
internal sealed class PlayerTileEditBudget
{
    // Existing abuse-safety ceiling. This is runtime admission policy, not a vanilla gameplay constant.
    private const int MaxEditsPerTickPerPlayer = 8;

    private readonly int[] editCounts;
    private long tick;
    private bool used;

    public PlayerTileEditBudget(int playerCapacity)
    {
        if (playerCapacity <= 0 || playerCapacity > byte.MaxValue + 1)
            throw new ArgumentOutOfRangeException(nameof(playerCapacity));

        editCounts = new int[playerCapacity];
    }

    public bool TryConsume(PlayerSlotId slot)
    {
        int index = slot.Value;
        if ((uint)index >= (uint)editCounts.Length || editCounts[index] >= MaxEditsPerTickPerPlayer)
            return false;

        editCounts[index]++;
        used = true;
        return true;
    }

    public void AdvanceTo(long currentTick)
    {
        if (currentTick == tick)
            return;

        if (used)
        {
            Array.Clear(editCounts, 0, editCounts.Length);
            used = false;
        }

        tick = currentTick;
    }
}
