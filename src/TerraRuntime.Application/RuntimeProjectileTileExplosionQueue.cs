using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeProjectileTileExplosionEvent(
    ProjectileSnapshot Projectile,
    PlayerHandle TrustedOwner,
    VanillaProjectileTileExplosionDefinition1458 Definition);

/// <summary>
/// Same-tick handoff for player-owned Projectile.Kill_ExplodeTiles. Only combat-trusted exact player provenance
/// is admitted. NPC/server projectiles and world-bounds cleanup cannot acquire terrain mutation authority here.
/// </summary>
internal sealed class RuntimeProjectileTileExplosionQueue : IProjectileTerminationCommitSink
{
    private readonly RuntimeProjectileTileExplosionEvent[] events;
    private int count;

    public RuntimeProjectileTileExplosionQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        events = new RuntimeProjectileTileExplosionEvent[capacity];
    }

    public ReadOnlySpan<RuntimeProjectileTileExplosionEvent> Events => events.AsSpan(0, count);

    public void Reset() => count = 0;

    public void ProjectileTerminated(in ProjectileTerminationCommit termination)
    {
        if (!termination.CombatTrusted ||
            !termination.TrustedOwner.IsAssigned ||
            termination.Reason == ProjectileSimulationTerminationReason.WorldBounds ||
            !VanillaProjectileTileExplosionFacts1458.TryGet(
                termination.FinalProjectile.Type,
                out VanillaProjectileTileExplosionDefinition1458 definition))
        {
            return;
        }

        if (count >= events.Length)
            throw new InvalidOperationException("Projectile tile-explosion queue capacity was exceeded by one simulation tick.");

        events[count++] = new RuntimeProjectileTileExplosionEvent(
            termination.FinalProjectile,
            termination.TrustedOwner,
            definition);
    }
}
