using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeProjectileTileExplosionEchoKey(
    PlayerHandle Owner,
    byte Action,
    short TileX,
    short TileY,
    short Data,
    byte Style);

internal readonly record struct RuntimeProjectileTileExplosionEchoEntry(
    RuntimeProjectileTileExplosionEchoKey Key,
    long ExpiresAfterTick);

/// <summary>
/// Bounded, exact convergence-echo registry for server-committed projectile terrain destruction. It never mutates
/// terrain: a matching packet 17 is consumed only after the trusted explosion already committed that exact action.
/// </summary>
internal sealed class RuntimeProjectileTileExplosionEchoTracker
{
    private const int MaximumEntries = 8_192;
    private const int LifetimeTicks = 120;
    private readonly Dictionary<RuntimeProjectileTileExplosionEchoKey, long> active = new();
    private readonly Queue<RuntimeProjectileTileExplosionEchoEntry> expiry = new();
    private long currentTick;

    internal void AdvanceTo(long tick)
    {
        if (tick < currentTick)
            throw new ArgumentOutOfRangeException(nameof(tick));
        currentTick = tick;
        while (expiry.TryPeek(out RuntimeProjectileTileExplosionEchoEntry entry) && entry.ExpiresAfterTick < tick)
        {
            expiry.Dequeue();
            if (active.TryGetValue(entry.Key, out long expiresAfterTick) &&
                expiresAfterTick == entry.ExpiresAfterTick)
            {
                active.Remove(entry.Key);
            }
        }
    }

    internal void Register(PlayerHandle owner, in TerrariaTileManipulationState state)
    {
        var key = new RuntimeProjectileTileExplosionEchoKey(
            owner,
            state.Action,
            state.TileX,
            state.TileY,
            state.Data,
            state.Style);
        long expiresAfterTick = checked(currentTick + LifetimeTicks);
        active[key] = expiresAfterTick;
        expiry.Enqueue(new RuntimeProjectileTileExplosionEchoEntry(key, expiresAfterTick));
        while (active.Count > MaximumEntries && expiry.TryDequeue(out RuntimeProjectileTileExplosionEchoEntry oldest))
        {
            if (active.TryGetValue(oldest.Key, out long currentExpiry) && currentExpiry == oldest.ExpiresAfterTick)
                active.Remove(oldest.Key);
        }
    }

    internal bool TryConsume(PlayerHandle owner, in TerrariaTileManipulationState state)
    {
        var key = new RuntimeProjectileTileExplosionEchoKey(
            owner,
            state.Action,
            state.TileX,
            state.TileY,
            state.Data,
            state.Style);
        return active.Remove(key);
    }
}
