using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private Action<ProjectileSnapshot, ProjectileSimulationStepResult>? wiringContact;

    internal bool TryScheduleWiringTrapBirth(in ProjectileSnapshot birth) =>
        executor.TryScheduleWiringTrapBirth(in birth);

    private void ApplySingleStepWiringContact(ProjectileSnapshot before, ProjectileSimulationStepResult step)
    {
        if (before.Type.Value != 185 || stepper is not VanillaProjectileWorldStateStepper ||
            before.Spawner != byte.MaxValue || !projectiles.IsCombatTrusted(before.Handle) ||
            projectiles.TryGetCombatTrustedOwner(before.Handle, out _) ||
            projectiles.TryGetServerNpcSource(before.Handle, out _) ||
            !projectiles.TryGetLifecycle(before.Handle, out var life) || life.Reflected || life.NpcProjectile) return;
        ApplyWiringMovementContact(in before, in step);
    }

    private void ApplyWiringMovementContact(in ProjectileSnapshot before, in ProjectileSimulationStepResult step)
    {
        if (toolActorWorld is null || step.Liquid is not { Wet: false } || step.CollisionTileCutOffer is not null ||
            !VanillaDefinitionCatalog.TryGet(before.Type, out var body)) return;
        var after = step.State;
        float vx = after.VelocityX, vy = after.VelocityY;
        int segmentLength = Math.Clamp(Math.Min(body.CollisionWidth, body.CollisionHeight), 3, 16);
        // HandleMovement offers each segment separately above this speed. The
        // endpoint offer below is only valid for the original single move.
        if (vx * vx + vy * vy > segmentLength * segmentLength ||
            after.PositionX != before.PositionX + vx || after.PositionY != before.PositionY + vy ||
            after.PositionX == before.PositionX && after.PositionY == before.PositionY) return;
        toolActorWorld.ApplyProjectilePressurePlate(after.PositionX + body.CollisionOffsetX,
            after.PositionY + body.CollisionOffsetY, body.CollisionWidth, body.CollisionHeight,
            before.PositionX + body.CollisionOffsetX, before.PositionY + body.CollisionOffsetY);
    }

    private ProjectileActorSimulationResult TrySimulateWiringEmitter(in ProjectileSnapshot initial)
    {
        if (stepper is not VanillaProjectileWorldStateStepper || worldTiles is null ||
            initial.Spawner != byte.MaxValue || initial.Damage != 40 || initial.OriginalDamage != 0 || initial.KnockBack != 2 ||
            !projectiles.IsCombatTrusted(initial.Handle) || projectiles.TryGetServerNpcSource(initial.Handle, out _) ||
            projectiles.TryGetCombatTrustedOwner(initial.Handle, out _) ||
            !projectiles.TryPrepareSimulation(in initial, out var actor) || actor is null || actor.Lifecycle.Reflected)
            return ProjectileActorSimulationResult.Rejected;
        bool outside = initial.PositionX <= 0 || initial.PositionY <= 0 ||
            initial.PositionX + 6 >= worldTiles.Dimensions.WidthTiles * 16 ||
            initial.PositionY + 6 >= worldTiles.Dimensions.HeightTiles * 16;
        var life = actor.Lifecycle;
        float counter = outside ? initial.Ai.Ai0 : initial.Ai.Ai0 + 1;
        bool emit = !outside && counter >= 6;
        if (emit) counter = 0;
        var after = new ProjectileStateUpdate(initial.Type, initial.Spawner, initial.PositionX, initial.PositionY,
            initial.VelocityX, initial.VelocityY, initial.Ai with { Ai0 = counter }, initial.BannerIdToRespondTo,
            initial.Damage, initial.KnockBack, initial.OriginalDamage);
        var afterLife = life with { TimeLeft = outside ? 0 : life.TimeLeft - 1 };
        using var births = emit ? projectiles.CreateWorldSpawnPreparation() : null;
        ProjectileSnapshot child = default;
        if (births is not null)
        {
            // AI_038 passes the emitter top-left as NewProjectile's CENTER.
            var state = new ProjectileStateUpdate(new(188), byte.MaxValue, initial.PositionX - 3, initial.PositionY - 3,
                initial.VelocityX, initial.VelocityY, default, 0, 40, 2, 0);
            if (!births.TrySetSourceActorView(in initial, in after, in life, true) ||
                !births.TryStage(in state, false, out child)) return ProjectileActorSimulationResult.Rejected;
        }
        if (!actor.TryStageMotion(in after, in afterLife) ||
            !(afterLife.TimeLeft > 0 ? actor.TryQueuePublication() : actor.TryQueueWiringTrapDespawn(new(initial.PositionX, initial.PositionY))) ||
            births is not null && !births.TryBindSourceActor(actor)) return ProjectileActorSimulationResult.Rejected;
        long tick = tickProvider();
        if (tickProvider() != tick || !actor.IsCurrent || births is not null && !births.IsCurrentOwned)
            return ProjectileActorSimulationResult.Rejected;
        if (!actor.TryAdoptUnpublished(out _, out _)) return ProjectileActorSimulationResult.Rejected;
        RuntimeProjectileStore.WorldSpawnPreparation.WorldSpawnPublication? publication = null;
        if (births is not null && (!births.TryAdoptUnpublished(out publication) || !executor.TryScheduleWiringFlameBirth(in child)))
            throw new InvalidOperationException("Admitted flame emitter lost its child participants.");
        ExceptionDispatchInfo? failure = null;
        try { while (publication?.TryPublishNext() == true) { } }
        catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        try { while (actor.TryPublishNext()) { } }
        catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        failure?.Throw();
        return ProjectileActorSimulationResult.Applied;
    }
    private ProjectileActorSimulationResult TrySimulateWiringFlame(in ProjectileSnapshot initial)
    {
        if (stepper is not VanillaProjectileWorldStateStepper concrete || initial.Damage != 40 ||
            initial.KnockBack != 2 || initial.OriginalDamage != 0)
            return ProjectileActorSimulationResult.Rejected;
        bool applied = false;
        int count = TerraRuntime.Gameplay.Projectiles.VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(initial.Type, initial.Ai);
        for (int subupdate = 0; subupdate < count; subupdate++)
        {
            if (!projectiles.TryGet(initial.Handle, out var current) ||
                !projectiles.TryPrepareSimulation(in current, out var actor) || actor is null)
                break;
            long tick = tickProvider();
            var beforeRandom = projectileRandom.Clone(); var afterRandom = beforeRandom.Clone();
            if (!concrete.TryCaptureGroundCombatTerrain(in current, out var terrain))
                return applied ? ProjectileActorSimulationResult.Applied : ProjectileActorSimulationResult.Rejected;
            var context = new ProjectileSimulationStepContext(current, actor.Lifecycle, subupdate, count);
            if (!concrete.TryStepGroundCombatState(in context, afterRandom, out var proposed) ||
                !RuntimeProjectileStateExecutor.TryNormalizeTermination(in proposed, out var next) ||
                !IsGroundCombatStep(in current, in next))
                return applied ? ProjectileActorSimulationResult.Applied : ProjectileActorSimulationResult.Rejected;
            if (next.WiringFlameSourceUpdate) afterRandom.Next(3);
            RuntimeProjectileSimulationCommitSink.ConsumeGroundCombatOffers(in next, afterRandom);
            var life = GroundCombatLife(actor.Lifecycle, in current, in next);
            bool terminal = next.TimeLeft <= 0;
            bool contact = !terminal || next.TerminationReason == ProjectileSimulationTerminationReason.LifetimeExpired;
            var contactLife = life with { TimeLeft = contact ? Math.Max(1, next.TimeLeft) : 0 };
            if (!actor.TryStageMotion(next.State, in contactLife) ||
                contact && next.SourceDirection is { } direction && !actor.TryStageSourceDirection(direction) ||
                !(contact ? terminal || actor.TryQueuePublication() : actor.TryQueueWiringTrapDespawn(next.KillOrigin)) ||
                tickProvider() != tick || !terrain.IsCurrent || !actor.IsCurrent || !projectileRandom.HasSameState(beforeRandom))
                return applied ? ProjectileActorSimulationResult.Applied : ProjectileActorSimulationResult.Rejected;
            if (!actor.TryAdoptUnpublished(out var accepted, out _))
                return applied ? ProjectileActorSimulationResult.Applied : ProjectileActorSimulationResult.Rejected;
            projectileRandom.CopyStateFrom(afterRandom);
            applied = true;
            bool continueFrame = true;
            ExceptionDispatchInfo? failure = null;
            ProjectileSnapshot? phaseImage = null;
            ProjectileLifecycleState? phaseLife = null;
            if (contact)
            {
                try
                {
                    ApplyWiringMovementContact(in current, in next);
                    continueFrame = projectiles.TryGet(accepted.Handle, out var contactActor) && contactActor == accepted;
                    if (continueFrame && activeOrdinaryArrowCombat is not null)
                        continueFrame = activeOrdinaryArrowCombat.TickWiringFlameContact(in accepted);
                    if (continueFrame && projectiles.TryGet(initial.Handle, out var afterNpc) && projectiles.TryGetLifecycle(initial.Handle, out var afterNpcLife))
                    {
                        phaseImage = afterNpc; phaseLife = afterNpcLife;
                        if (groundCombatPlayerCombat is not null) continueFrame &= groundCombatPlayerCombat.TickWiringFlameContact(in afterNpc);
                    }
                    else continueFrame = false;
                }
                catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); continueFrame = false; }
            }
            var publicationRandom = projectileRandom.Clone();
            try { while (actor.TryPublishNext()) { } }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); continueFrame = false; }
            if (tickProvider() != tick || !projectileRandom.HasSameState(publicationRandom)) continueFrame = false;
            if (phaseImage is { } expected && (!projectiles.TryGet(initial.Handle, out var phaseCurrent) || phaseCurrent != expected ||
                !projectiles.TryGetLifecycle(initial.Handle, out var currentLife) || currentLife != phaseLife)) continueFrame = false;
            if (terminal && contact && continueFrame && phaseImage is { } finalImage && projectiles.TryGet(initial.Handle, out var tail) && tail == finalImage &&
                projectiles.TryGetLifecycle(initial.Handle, out var tailLife) && tailLife == phaseLife &&
                projectiles.TryPrepareSimulation(in tail, out var ending) && ending is not null)
            {
                var state = new ProjectileStateUpdate(tail.Type, tail.Spawner, tail.PositionX, tail.PositionY,
                    tail.VelocityX, tail.VelocityY, tail.Ai, tail.BannerIdToRespondTo, tail.Damage, tail.KnockBack, tail.OriginalDamage);
                var finalLife = ending.Lifecycle with { TimeLeft = 0 };
                if (!ending.TryStageMotion(in state, in finalLife) || !ending.TryQueueWiringTrapDespawn(next.KillOrigin) ||
                    !ending.TryAdoptUnpublished(out _, out _)) throw new InvalidOperationException("Accepted final flame lost its lifetime owner.");
                try { while (ending.TryPublishNext()) { } } catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
            }
            failure?.Throw();
            if (terminal || !continueFrame) break;
        }
        return applied ? ProjectileActorSimulationResult.Applied : ProjectileActorSimulationResult.Rejected;
    }

}
