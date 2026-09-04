using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime;

internal readonly record struct RuntimeProjectileExplosionEvent(
    ProjectileSnapshot Projectile,
    PlayerHandle TrustedOwner,
    NpcHandle SourceNpc,
    float Left,
    float Top,
    int Width,
    int Height)
{
    public float CenterX => Left + Width * 0.5f;
    public float CenterY => Top + Height * 0.5f;
}

/// <summary>
/// Bounded same-tick handoff for source-backed projectile Kill() damage. The executor removes the generation first,
/// then this sink preserves the final trusted snapshot long enough for post-simulation NPC/PvP damage. Nothing is
/// emitted for world-bounds removals because that path is not equivalent to vanilla Projectile.Kill().
/// </summary>
internal sealed class RuntimeProjectileExplosionQueue : IProjectileTerminationCommitSink
{
    private readonly RuntimeProjectileExplosionEvent[] events;
    private int count;

    public RuntimeProjectileExplosionQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        events = new RuntimeProjectileExplosionEvent[capacity];
    }

    public ReadOnlySpan<RuntimeProjectileExplosionEvent> Events => events.AsSpan(0, count);

    public void Reset() => count = 0;

    public void ProjectileTerminated(in ProjectileTerminationCommit termination)
    {
        bool trustedPlayerSource = termination.CombatTrusted && termination.TrustedOwner.IsAssigned;
        bool trustedNpcSource = termination.SourceNpc.IsAssigned;
        if ((!trustedPlayerSource && !trustedNpcSource) ||
            termination.Reason == ProjectileSimulationTerminationReason.WorldBounds ||
            !VanillaProjectileExplosionFacts.TryGetOnKillExplosion(
                termination.FinalProjectile.Type,
                out VanillaProjectileExplosionDefinition explosion) ||
            !VanillaProjectileDefinitionCatalog.TryGet(
                termination.FinalProjectile.Type,
                out VanillaProjectileDefinition sourceDefinition))
        {
            return;
        }

        // At most one authoritative termination can be committed for each physical projectile slot in one executor
        // pass, so a queue sized to the store capacity cannot overflow without violating the executor contract.
        if (count >= events.Length)
            throw new InvalidOperationException("Projectile explosion queue capacity was exceeded by one simulation tick.");

        ProjectileSnapshot final = termination.FinalProjectile;
        float centerX = final.PositionX + sourceDefinition.Width * 0.5f;
        float centerY = final.PositionY + sourceDefinition.Height * 0.5f;
        ProjectileSnapshot prepared = final with
        {
            Damage = checked((short)(explosion.DamageOverride ?? final.Damage)),
            KnockBack = explosion.KnockBack
        };
        events[count++] = new RuntimeProjectileExplosionEvent(
            prepared,
            termination.TrustedOwner,
            termination.SourceNpc,
            centerX - explosion.Width * 0.5f,
            centerY - explosion.Height * 0.5f,
            explosion.Width,
            explosion.Height);
    }
}
