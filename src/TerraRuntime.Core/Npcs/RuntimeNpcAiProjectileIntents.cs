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

public static class RuntimeNpcProjectileIntentApplier
{
    public const byte ServerSpawner = byte.MaxValue;

    public static bool TryApply(
        RuntimeProjectileStore projectiles,
        in NpcAiProjectileIntent intent,
        out ProjectileSnapshot spawned)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        if (!VanillaProjectileIds.IsLiveWireType(intent.Type) ||
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
        return projectiles.TrySpawnVanilla(in update, intent.TimeLeftOverride > 0 ? intent.TimeLeftOverride : null, out spawned);
    }
}
