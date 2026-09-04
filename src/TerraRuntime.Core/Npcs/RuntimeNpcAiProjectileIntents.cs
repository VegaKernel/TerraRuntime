using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// One speculative server-owned projectile requested by an NPC AI transition. The intent contains gameplay
/// facts only; physical projectile slot allocation remains owned by RuntimeProjectileStore after the source
/// NPC generation-safe transition commits.
/// </summary>
public readonly record struct NpcAiProjectileIntent(
    ProjectileTypeId Type,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    int Damage,
    float KnockBack)
{
    /// <summary>Source-owned initial projectile AI state applied atomically with allocation.</summary>
    public ProjectileAiState InitialAi { get; init; }

    /// <summary>Positive SetDefaults lifetime override applied atomically with allocation; zero keeps catalog defaults.</summary>
    public int TimeLeftOverride { get; init; }
}

/// <summary>
/// Optional capability exposed by an NPC AI composition. Implementations may inspect the proposed NPC state,
/// but must not publish projectiles directly: RuntimeNpcAiStateExecutor applies returned intents only after the
/// exact source NPC generation has committed successfully.
/// </summary>
public interface INpcAiProjectileIntentPlanner
{
    int PlanProjectileSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileIntent> destination);
}

/// <summary>
/// One bounded post-commit mutation of already-live server-owned projectiles sourced by the same exact NPC
/// generation. This covers vanilla cross-entity transitions where NPC AI releases projectiles that were spawned
/// on earlier ticks without allowing a reused NPC slot to mutate another generation's projectiles.
/// </summary>
public readonly record struct NpcAiProjectileMutationIntent(
    ProjectileTypeId Type,
    float VelocityX,
    float VelocityY,
    float Ai0);

/// <summary>
/// Optional capability for NPC AI that mutates already-live projectiles. Intents are planned before the NPC state
/// commit but applied only after the exact source generation commits successfully.
/// </summary>
public interface INpcAiProjectileMutationIntentPlanner
{
    int PlanProjectileMutations(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileMutationIntent> destination);
}

public static class RuntimeNpcProjectileIntentApplier
{
    public const byte ServerSpawner = byte.MaxValue;

    public static bool TryApply(
        RuntimeProjectileStore projectiles,
        in NpcAiProjectileIntent intent,
        out ProjectileSnapshot spawned) =>
        TryApply(projectiles, default, in intent, out spawned);

    public static bool TryApply(
        RuntimeProjectileStore projectiles,
        NpcHandle sourceNpc,
        in NpcAiProjectileIntent intent,
        out ProjectileSnapshot spawned)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        if (!VanillaProjectileLifecycleFacts.IsDefinedLiveType(intent.Type) ||
            !float.IsFinite(intent.PositionX) ||
            !float.IsFinite(intent.PositionY) ||
            !float.IsFinite(intent.VelocityX) ||
            !float.IsFinite(intent.VelocityY) ||
            !float.IsFinite(intent.KnockBack) ||
            intent.KnockBack < 0f ||
            intent.Damage < 0 ||
            intent.Damage > short.MaxValue ||
            !float.IsFinite(intent.InitialAi.Ai0) ||
            !float.IsFinite(intent.InitialAi.Ai1) ||
            !float.IsFinite(intent.InitialAi.Ai2) ||
            intent.TimeLeftOverride < 0)
        {
            spawned = default;
            return false;
        }

        short damage = checked((short)intent.Damage);
        var update = new ProjectileStateUpdate(
            intent.Type,
            ServerSpawner,
            intent.PositionX,
            intent.PositionY,
            intent.VelocityX,
            intent.VelocityY,
            intent.InitialAi,
            BannerIdToRespondTo: 0,
            Damage: damage,
            KnockBack: intent.KnockBack,
            OriginalDamage: damage);
        if (!projectiles.TrySpawnVanilla(in update, intent.TimeLeftOverride > 0 ? intent.TimeLeftOverride : null, out spawned))
            return false;
        if (sourceNpc.IsAssigned && !projectiles.TrySetServerNpcSource(spawned.Handle, sourceNpc))
        {
            projectiles.TryDespawn(spawned.Handle, out _);
            spawned = default;
            return false;
        }
        return true;
    }
}

public static class RuntimeNpcProjectileMutationIntentApplier
{
    /// <summary>
    /// Applies one source-owned mutation in physical projectile-slot order. Both generation-safe NPC provenance and
    /// vanilla's ai[1] source-slot selector must match before the projectile can be changed.
    /// </summary>
    public static int ApplyMatching(
        RuntimeProjectileStore projectiles,
        NpcHandle sourceNpc,
        in NpcAiProjectileMutationIntent intent,
        Span<ProjectileSnapshot> scratch)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        if (!sourceNpc.IsAssigned ||
            !VanillaProjectileLifecycleFacts.IsDefinedLiveType(intent.Type) ||
            !float.IsFinite(intent.VelocityX) ||
            !float.IsFinite(intent.VelocityY) ||
            !float.IsFinite(intent.Ai0) ||
            scratch.Length < projectiles.ActiveCount)
        {
            return 0;
        }

        int captured = projectiles.CopyActive(scratch);
        int applied = 0;
        for (int index = 0; index < captured; index++)
        {
            ProjectileSnapshot projectile = scratch[index];
            if (projectile.Type != intent.Type ||
                projectile.Ai.Ai0 == intent.Ai0 ||
                projectile.Ai.Ai1 != sourceNpc.Slot ||
                !projectiles.TryGetServerNpcSource(projectile.Handle, out NpcHandle actualSource) ||
                actualSource != sourceNpc)
            {
                continue;
            }

            var update = new ProjectileStateUpdate(
                projectile.Type,
                projectile.Spawner,
                projectile.PositionX,
                projectile.PositionY,
                intent.VelocityX,
                intent.VelocityY,
                projectile.Ai with { Ai0 = intent.Ai0 },
                projectile.BannerIdToRespondTo,
                projectile.Damage,
                projectile.KnockBack,
                projectile.OriginalDamage);
            if (projectiles.TryUpdate(projectile.Handle, in update, out _))
                applied++;
        }

        return applied;
    }
}
