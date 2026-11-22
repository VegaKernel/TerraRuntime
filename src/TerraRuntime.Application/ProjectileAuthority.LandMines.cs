using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private ProjectileActorSimulationResult TrySimulateLandMine(in ProjectileSnapshot initial)
    {
        if (toolActorWorld is null || stepper is not VanillaProjectileWorldStateStepper ||
            activeOrdinaryArrowCombat is null || initial.Spawner != byte.MaxValue ||
            initial.Damage != VanillaLandMine1458.Damage || initial.KnockBack != VanillaLandMine1458.KnockBack ||
            initial.OriginalDamage != 0 || initial.VelocityX != 0 || initial.VelocityY != 0 || initial.Ai != default ||
            !projectiles.IsCombatTrusted(initial.Handle) || projectiles.TryGetCombatTrustedOwner(initial.Handle, out _) ||
            projectiles.TryGetServerNpcSource(initial.Handle, out _) ||
            !projectiles.TryPrepareSimulation(in initial, out var entry) || entry is null ||
            entry.Lifecycle is not { TimeLeft: 2, NpcProjectile: false, Reflected: false, WorldToolBombKillInProgress: false })
            return ProjectileActorSimulationResult.Rejected;

        // AI_016 increments ai0 then Kill runs damage on the original 128px body.
        var body = BombUpdate(initial, initial.PositionX, initial.PositionY, 0, 0, initial.Ai with { Ai0 = 1 });
        var ending = entry.Lifecycle with { TimeLeft = 0 };
        if (!entry.TryStageWorldToolBombKill(in body, in ending) ||
            !entry.TryAdoptUnpublished(out var current, out bool expired) || expired) return ProjectileActorSimulationResult.Rejected;
        ending = entry.Lifecycle;
        var randomBeforeRetirement = projectileRandom.Clone();
        ExceptionDispatchInfo? failure = null;
        try
        {
            int tileX = (int)(initial.PositionX + VanillaLandMine1458.BodySize / 2) / 16;
            int tileY = (int)(initial.PositionY + VanillaLandMine1458.BodySize / 2) / 16;
            // Dedicated vanilla leaves the human's self-hit to the client
            // that receives 29. Only source NPC contacts run in this phase.
            var damageBody = current;
            if (toolActorWorld.CanProcessLandMineDamageTerrain(tileX, tileY))
                _ = activeOrdinaryArrowCombat.TryProcessWorldToolBombContacts(in damageBody,
                    out _, out current, out randomBeforeRetirement);
        }
        catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        try { RetireLandMine(in initial, in current, in ending, randomBeforeRetirement); }
        catch (Exception error) when (failure is not null) { _ = error; }
        failure?.Throw();
        return ProjectileActorSimulationResult.Applied;
    }

    private void RetireLandMine(in ProjectileSnapshot initial, in ProjectileSnapshot current,
        in ProjectileLifecycleState ending, VanillaUnifiedRandom1458 randomBefore)
    {
        if (!projectiles.TryGet(current.Handle, out var retained) || retained.Type != initial.Type ||
            !projectiles.TryGetLifecycle(current.Handle, out var life) || life != ending ||
            !projectiles.TryPrepareSimulation(in retained, out var tail) || tail is null) return;

        const int offset = (VanillaLandMine1458.BodySize - VanillaLandMine1458.RetiredBodySize) / 2;
        var after = BombUpdate(retained, initial.PositionX + offset, initial.PositionY + offset, 0, 0, retained.Ai);
        var sourceOrigin = new ProjectileKillOrigin(initial.PositionX, initial.PositionY);
        if (!tail.TryStageMotion(in after, in ending) || !tail.TryQueueWorldToolBombDespawn(in sourceOrigin) ||
            !tail.TryAdoptUnpublished(out _, out bool expired) || !expired)
            throw new InvalidOperationException("An admitted landmine lost its retirement state.");

        if (retained == current && projectileRandom.HasSameState(randomBefore))
        {
            // Dedicated Gore allocates nothing but its four caller draws remain.
            for (int index = 0; index < 4; index++) projectileRandom.Next(61, 64);
        }
        while (tail.TryPublishNext()) { }
    }
}
