using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal enum RuntimeProjectileLiveChildKind : byte
{
    TornadoSegment = 1,
    CultistIceMist = 2,
    CultistLightningOrb = 3
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
        else if (initialProjectile.Type == VanillaProjectileIds.CultistBossLightningOrb &&
                 finalProjectile.Ai.Ai0 is > 0f and < 180f &&
                 IsThirtyUpdateBoundary(finalProjectile.Ai.Ai0))
        {
            // AI_088 increments ai[0] before emitting arcs at 30/60/90/120/150.
            kind = RuntimeProjectileLiveChildKind.CultistLightningOrb;
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

/// <summary>TerrariaServer 1.4.5.8 AI_088 child facts for CultistBossLightningOrb (#465).</summary>
internal static class RuntimeCultistLightningOrbLiveChildSpawn1458
{
    public const int MaximumTargets = 5;

    public static int CopyIntents(
        in RuntimeProjectileLiveChildSpawnEvent child,
        VanillaProjectilePlayerTargetResolver targets,
        VanillaUnifiedRandom1458 random,
        Span<NpcAiProjectileIntent> intents)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(random);
        if (child.Kind != RuntimeProjectileLiveChildKind.CultistLightningOrb ||
            child.InitialProjectile.Type != VanillaProjectileIds.CultistBossLightningOrb ||
            intents.IsEmpty ||
            !VanillaDefinitionCatalog.TryGet(child.InitialProjectile.Type, out VanillaProjectileDefinition definition))
        {
            return 0;
        }

        ProjectileSnapshot parent = child.InitialProjectile;
        float sourceCenterX = parent.PositionX + definition.Width * 0.5f;
        float sourceCenterY = parent.PositionY + definition.Height * 0.5f;
        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[MaximumTargets];
        Span<float> centerXs = stackalloc float[MaximumTargets];
        Span<float> centerYs = stackalloc float[MaximumTargets];
        int targetCount = targets.CopyTargetsWithLineOfSight(
            sourceCenterX,
            sourceCenterY,
            2000f,
            slots,
            centerXs,
            centerYs);
        int candidateCount = Math.Min(targetCount, intents.Length);
        int count = 0;

        for (int i = 0; i < candidateCount; i++)
        {
            float dx = centerXs[i] - sourceCenterX;
            float dy = centerYs[i] - sourceCenterY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (!(distance > 0f) || !float.IsFinite(distance))
                continue;

            float aiSeed = random.Next(100);
            const double maximumRotation = Math.PI / 4d;
            // Terraria.Utils.RotatedByRandom consumes two Main.rand samples and subtracts the angles.
            double randomRotation = random.NextDouble() * maximumRotation - random.NextDouble() * maximumRotation;
            double cos = Math.Cos(randomRotation);
            double sin = Math.Sin(randomRotation);
            float normalizedX = dx / distance;
            float normalizedY = dy / distance;
            float velocityX = (float)(normalizedX * cos - normalizedY * sin) * 7f;
            float velocityY = (float)(normalizedX * sin + normalizedY * cos) * 7f;
            intents[count++] = new NpcAiProjectileIntent(
                VanillaProjectileIds.CultistBossLightningOrbArc,
                sourceCenterX - 7f,
                sourceCenterY - 7f,
                velocityX,
                velocityY,
                parent.Damage,
                0f)
            {
                InitialAi = new ProjectileAiState(MathF.Atan2(dy, dx), aiSeed, 0f)
            };
        }

        return count;
    }
}
