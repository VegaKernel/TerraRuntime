using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal enum RuntimeProjectileLiveChildKind : byte
{
    TornadoSegment = 1,
    CultistIceMist = 2
}

internal readonly record struct RuntimeProjectileLiveChildSpawnEvent(
    ProjectileHandle Parent,
    ProjectileSnapshot InitialProjectile,
    ProjectileLifecycleState InitialLifecycle,
    RuntimeProjectileLiveChildKind Kind);

/// <summary>
/// Bounded post-commit handoff for vanilla projectile AI that creates children while the parent remains alive.
/// The executor calls this sink only after the exact parent generation commits. The authority revalidates that
/// same handle and its generation-safe NPC provenance before publishing any child, so a reused projectile or NPC
/// slot cannot inherit an old chain. Child allocation is intentionally deferred until the simulation pass ends;
/// exact same-tick physical-slot update ordering remains a separate roadmap item.
/// </summary>
internal sealed class RuntimeProjectileLiveChildSpawnQueue : IProjectileSimulationCommitSink
{
    private readonly RuntimeProjectileLiveChildSpawnEvent[] events;
    private int count;

    public RuntimeProjectileLiveChildSpawnQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        events = new RuntimeProjectileLiveChildSpawnEvent[capacity];
    }

    public ReadOnlySpan<RuntimeProjectileLiveChildSpawnEvent> Events => events.AsSpan(0, count);

    public void Reset() => count = 0;

    public void ProjectileSimulationCommitted(
        in ProjectileSnapshot initialProjectile,
        in ProjectileLifecycleState initialLifecycle,
        ReadOnlySpan<ProjectileSimulationStepResult> subupdates,
        in ProjectileSnapshot finalProjectile,
        bool expired)
    {
        if (expired || subupdates.IsEmpty || !initialProjectile.Handle.IsAssigned)
            return;

        RuntimeProjectileLiveChildKind kind;
        if ((initialProjectile.Type == VanillaProjectileIds.Sharknado ||
             initialProjectile.Type == VanillaProjectileIds.Cthulunado) &&
            initialProjectile.Ai.Ai0 == 2f &&
            initialProjectile.Ai.Ai1 > 0f &&
            finalProjectile.Ai.Ai0 == 1f)
        {
            // TerrariaServer 1.4.5.8 AI(), aiStyle 64: ai[0] decrements first, then ai[0] == 1 emits
            // the next tornado segment and, at source-backed intervals, one Sharkron/Sharkron2 NPC.
            kind = RuntimeProjectileLiveChildKind.TornadoSegment;
        }
        else if (initialProjectile.Type == VanillaProjectileIds.CultistBossIceMist &&
                 initialProjectile.Ai.Ai1 == 1f &&
                 finalProjectile.Ai.Ai0 is > 0f and < 150f &&
                 IsThirtyUpdateBoundary(finalProjectile.Ai.Ai0))
        {
            // AI_086 increments ai[0] before the modulo check and emits at 30/60/90/120.
            kind = RuntimeProjectileLiveChildKind.CultistIceMist;
        }
        else
        {
            return;
        }

        if (count >= events.Length)
            throw new InvalidOperationException("Projectile live child-spawn queue capacity was exceeded by one simulation tick.");

        events[count++] = new RuntimeProjectileLiveChildSpawnEvent(
            initialProjectile.Handle,
            initialProjectile,
            initialLifecycle,
            kind);
    }

    private static bool IsThirtyUpdateBoundary(float ai0)
    {
        int integral = (int)ai0;
        return ai0 == integral && integral % 30 == 0;
    }
}

/// <summary>TerrariaServer 1.4.5.8 AI_064 live child facts for Sharknado/Cthulunado (#384/#386).</summary>
internal static class RuntimeTornadoLiveChildSpawn1458
{
    public static bool TryCreateIntents(
        in RuntimeProjectileLiveChildSpawnEvent child,
        out NpcAiProjectileIntent projectileIntent,
        out bool hasNpcIntent,
        out NpcAiSpawnIntent npcIntent)
    {
        ProjectileSnapshot parent = child.InitialProjectile;
        bool cthulunado = parent.Type == VanillaProjectileIds.Cthulunado;
        if (child.Kind != RuntimeProjectileLiveChildKind.TornadoSegment ||
            (parent.Type != VanillaProjectileIds.Sharknado && !cthulunado) ||
            parent.Ai.Ai0 != 2f || parent.Ai.Ai1 <= 0f ||
            !VanillaDefinitionCatalog.TryGet(parent.Type, out VanillaProjectileDefinition definition))
        {
            projectileIntent = default;
            hasNpcIntent = false;
            npcIntent = default;
            return false;
        }

        int startDelay = cthulunado ? 16 : 10;
        int segmentCount = cthulunado ? 16 : 15;
        float scaleMultiplier = cthulunado ? 1.5f : 1f;
        const float baseWidth = 150f;
        const float baseHeight = 42f;
        float denominator = startDelay + segmentCount;
        float currentScale = (denominator - parent.Ai.Ai1) * scaleMultiplier / denominator;
        float nextScale = (denominator - parent.Ai.Ai1 + 1f) * scaleMultiplier / denominator;
        if (!(currentScale > 0f) || !(nextScale > 0f) ||
            !float.IsFinite(currentScale) || !float.IsFinite(nextScale))
        {
            projectileIntent = default;
            hasNpcIntent = false;
            npcIntent = default;
            return false;
        }

        int currentWidth = (int)(baseWidth * currentScale);
        int currentHeight = (int)(baseHeight * currentScale);
        int nextHeight = (int)(baseHeight * nextScale);
        if (currentWidth <= 0 || currentHeight <= 0 || nextHeight <= 0)
        {
            projectileIntent = default;
            hasNpcIntent = false;
            npcIntent = default;
            return false;
        }

        float centerX = parent.PositionX + currentWidth * 0.5f;
        float centerY = parent.PositionY + currentHeight * 0.5f;
        // Source uses the float-scaled 42px base height here, not the integer-resized hitbox heights.
        float childCenterY = centerY - baseHeight * currentScale * 0.5f - baseHeight * nextScale * 0.5f + 2f;

        projectileIntent = new NpcAiProjectileIntent(
            parent.Type,
            centerX - definition.Width * 0.5f,
            childCenterY - definition.Height * 0.5f,
            parent.VelocityX,
            parent.VelocityY,
            parent.Damage,
            parent.KnockBack)
        {
            InitialAi = new ProjectileAiState(10f, parent.Ai.Ai1 - 1f, 0f)
        };

        int npcStride = cthulunado ? 2 : 4;
        hasNpcIntent = (int)parent.Ai.Ai1 % npcStride == 0;
        if (!hasNpcIntent)
        {
            npcIntent = default;
            return true;
        }

        NpcTypeId npcType = cthulunado ? VanillaNpcIds.Sharkron2 : VanillaNpcIds.Sharkron;
        npcIntent = new NpcAiSpawnIntent(
            npcType,
            (int)centerX,
            (int)childCenterY,
            parent.VelocityX,
            parent.VelocityY,
            VanillaNpcDefinitionCatalog.DefaultTarget)
        {
            InitialAi = cthulunado
                ? new NpcAiState(0f, 0f, currentWidth, -1.5f)
                : default
        };
        return true;
    }
}

/// <summary>TerrariaServer 1.4.5.8 AI_086 emitter child facts for CultistBossIceMist (#464).</summary>
internal static class RuntimeCultistIceMistLiveChildSpawn1458
{
    public static bool TryCreateIntent(
        in RuntimeProjectileLiveChildSpawnEvent child,
        out NpcAiProjectileIntent intent)
    {
        ProjectileSnapshot parent = child.InitialProjectile;
        if (child.Kind != RuntimeProjectileLiveChildKind.CultistIceMist ||
            parent.Type != VanillaProjectileIds.CultistBossIceMist ||
            parent.Ai.Ai1 != 1f ||
            !VanillaDefinitionCatalog.TryGet(parent.Type, out VanillaProjectileDefinition definition) ||
            !float.IsFinite(child.InitialLifecycle.LocalAi.Ai2))
        {
            intent = default;
            return false;
        }

        float centerX = parent.PositionX + definition.Width * 0.5f;
        float centerY = parent.PositionY + definition.Height * 0.5f;
        float rotation = child.InitialLifecycle.LocalAi.Ai2;
        intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.CultistBossIceMist,
            centerX - definition.Width * 0.5f,
            centerY - definition.Height * 0.5f,
            MathF.Cos(rotation),
            MathF.Sin(rotation),
            parent.Damage,
            parent.KnockBack);
        return true;
    }
}
