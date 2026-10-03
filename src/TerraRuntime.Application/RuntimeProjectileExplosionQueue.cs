using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Application;

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
    private readonly VanillaUnifiedRandom1458? terminationRandom;
    private int count;

    public RuntimeProjectileExplosionQueue(int capacity, VanillaUnifiedRandom1458? terminationRandom = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        events = new RuntimeProjectileExplosionEvent[capacity];
        this.terminationRandom = terminationRandom;
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
            !VanillaDefinitionCatalog.TryGet(
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
        if (termination.KillOrigin is { IsValid: false })
            throw new InvalidOperationException("An accepted projectile termination carried an invalid Kill origin.");
        if (final.Type == VanillaProjectileIds.DrManFlyFlask)
        {
            // Kill consumes these arguments even though Gore.NewGore immediately exits on a dedicated server.
            // The intermediate dust/gore body is 15+40=55, before the second expansion to 135.
            if (terminationRandom is null)
                throw new InvalidOperationException("Dr Man Fly termination requires its authority-owned RNG.");
            for (int index = 0; index < VanillaEclipseProjectileFacts1458.FlaskGoreArgumentCount; index++)
            {
                terminationRandom.Next(VanillaEclipseProjectileFacts1458.FlaskIntermediateBodySize);
                terminationRandom.Next(VanillaEclipseProjectileFacts1458.FlaskIntermediateBodySize);
                terminationRandom.Next(VanillaEclipseProjectileFacts1458.FlaskGoreMinimum, VanillaEclipseProjectileFacts1458.FlaskGoreMaximumExclusive);
            }
        }
        float centerX = (termination.KillOrigin?.X ?? final.PositionX) + sourceDefinition.Width * 0.5f;
        float centerY = (termination.KillOrigin?.Y ?? final.PositionY) + sourceDefinition.Height * 0.5f;
        ProjectileSnapshot prepared = final with
        {
            Damage = checked((short)(explosion.DamageOverride ?? final.Damage)),
            KnockBack = explosion.PreserveKnockBack ? final.KnockBack : explosion.KnockBack
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
