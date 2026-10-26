using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    // This actor owns only the WorldGen tree source's trusted npcProj births.
    // A type number, owner255 or incoming27 alone never enters this path.
    private ProjectileActorSimulationResult TrySimulateWorldToolBombActor(ProjectileSnapshot initial)
    {
        if (!VanillaWorldToolBomb1458.IsSupported(initial.Type) ||
            !projectiles.TryGetLifecycle(initial.Handle, out var life) || !life.NpcProjectile ||
            !projectiles.IsCombatTrusted(initial.Handle) || initial.Spawner != byte.MaxValue ||
            projectiles.TryGetCombatTrustedOwner(initial.Handle, out _) ||
            projectiles.TryGetServerNpcSource(initial.Handle, out _))
            return ProjectileActorSimulationResult.NotApplicable;
        if (toolActorWorld is null || worldTiles is null || stepper is not VanillaProjectileWorldStateStepper concrete ||
            activeOrdinaryArrowCombat is null || life.WorldToolBombKillInProgress || life.TimeLeft <= 0 ||
            life.Reflected || initial.OriginalDamage != 0 || initial.Damage != 0 || life.GemStaff is not null || life.HeldTool is not null || life.ThrownAxe is not null ||
            initial.Ai.Ai0 == 1000f || !projectiles.TryPrepareSimulation(in initial, out var actor) || actor is null)
            return ProjectileActorSimulationResult.Rejected;

        bool outside = initial.PositionX <= 0f || initial.PositionY <= 0f ||
            initial.PositionX + VanillaWorldToolBomb1458.PhysicalBody >= worldTiles.Dimensions.WidthTiles * 16f ||
            initial.PositionY + VanillaWorldToolBomb1458.PhysicalBody >= worldTiles.Dimensions.HeightTiles * 16f;
        if (outside)
        {
            var body = BombUpdate(initial, initial.PositionX, initial.PositionY, initial.VelocityX, initial.VelocityY, initial.Ai);
            var terminal = life with { TimeLeft = 0 };
            if (!actor.TryStageMotion(in body, in terminal) || !actor.TryQueuePublication() || !actor.IsCurrent ||
                !actor.TryAdoptUnpublished(out _, out bool expired) || !expired)
                return ProjectileActorSimulationResult.Rejected;
            PublishBomb(actor, true);
            return ProjectileActorSimulationResult.Applied;
        }

        var beforeRandom = projectileRandom.Clone();
        if (!toolActorWorld.TryBeginWorldToolBombStep(actor, projectileRandom, beforeRandom, out var world) || world is null)
            return ProjectileActorSimulationResult.Rejected;
        bool mustKill;
        using (world)
        {
            bool near = false;
            if (initial.Type.Value == 75 && world.World.TenthAnniversaryWorld == false &&
                !world.TryHasHappyBombProximity(initial.PositionX + 11f, initial.PositionY + 11f, out near))
                return ProjectileActorSimulationResult.Rejected;
            mustKill = near || life.TimeLeft <= VanillaWorldToolBomb1458.FuseThreshold;
            if (!mustKill)
            {
                var wind = concrete.CaptureGemStaffWind();
                if (!VanillaWorldToolBomb1458.TryAdvanceFuseEffects(initial.Type, life.TimeLeft, world.Random.Next))
                    return ProjectileActorSimulationResult.Rejected;
                var context = new ProjectileSimulationStepContext(initial, life, 0, 1);
                if (!concrete.TryStepWorldToolBombState(in context, out var next, out bool sourceImpact) || next.TimeLeft <= 0 ||
                    next.TerminationReason != ProjectileSimulationTerminationReason.None ||
                    !world.TryCaptureLiquid(initial.PositionX, initial.PositionY, in life,
                        out var liquid, out byte wetCount, out byte colourDraws) ||
                    !world.TryRecordMovementReads(in initial, next.State)) return ProjectileActorSimulationResult.Rejected;
                for (int draw = 0; draw < colourDraws; draw++) world.Random.Next(6);
                // Source HandleMovement18060 evaluates this draw before the
                // zero-damage/CanCutTiles predicates, even when no cut occurs.
                if (sourceImpact) _ = world.Random.Next(3);
                int lifetime = next.TimeLeft;
                if (world.World.GetGoodWorld == true && world.World.NoTrapsWorld == true && world.World.TenthAnniversaryWorld == false) lifetime--;
                var ending = life with
                {
                    TimeLeft = lifetime, Liquid = liquid, SourceWetCount = wetCount,
                    OldVelocityX = initial.VelocityX, OldVelocityY = initial.VelocityY,
                    LocalAi = (next.LocalAi ?? life.LocalAi) with
                    { Ai2 = BitConverter.Int32BitsToSingle(VanillaWorldToolBomb1458.SuppressedTrackedSoundBits) }
                };
                // The dedicated source suppresses visual allocations, not their
                // caller argument RNG. No routine27 is introduced by this actor.
                if (lifetime <= 0 || !actor.TryStageMotion(next.State, in ending) ||
                    !actor.TryStageSourceDirection(next.State.VelocityX < 0 ? -1 : 1) ||
                    !actor.TryQueuePublication() || !world.TrySeal() || !world.IsCurrent ||
                    !concrete.IsGemStaffWindCurrent(in wind) || !actor.IsCurrent)
                    return ProjectileActorSimulationResult.Rejected;
                if (!actor.TryAdoptUnpublished(out _, out bool expired) || expired || !world.TryAdoptUnpublished())
                    throw new InvalidOperationException("A prepared source bomb lost joint motion ownership.");
                ExceptionDispatchInfo? failure = null;
                try { PublishBomb(actor, false); } catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
                try { world.TryPublish(); } catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
                failure?.Throw();
                return ProjectileActorSimulationResult.Applied;
            }
        }
        return CompleteWorldToolBombKill(initial, life);
    }

    private ProjectileActorSimulationResult CompleteWorldToolBombKill(ProjectileSnapshot initial, ProjectileLifecycleState originalLife)
    {
        var current = initial;
        var killOrigin = new ProjectileKillOrigin(initial.PositionX, initial.PositionY);
        var life = originalLife with { TimeLeft = 0,
            OldVelocityX = initial.VelocityX, OldVelocityY = initial.VelocityY };
        short damage = initial.Type.Value == 28 ? (short)100 : (short)60;
        var enlarged = BombUpdate(initial, initial.PositionX - 53f, initial.PositionY - 53f,
            initial.VelocityX, initial.VelocityY, initial.Ai with { Ai1 = 0f }) with
            { Damage = damage, KnockBack = VanillaWorldToolBomb1458.ExplosionKnockBack };
        if (!projectiles.TryPrepareSimulation(in current, out var entry) || entry is null ||
            !entry.TryStageWorldToolBombKill(in enlarged, in life))
            return ProjectileActorSimulationResult.Rejected;
        PlayerAuthority.WorldToolBombPlayerDamagePreparation? hurt = null;
        if (initial.Type.Value == 75)
        {
            // Preparation is detached: source-local variation belongs to its
            // independent player stream. Reject live-owner aliasing BEFORE any
            // Kill body/RNG adoption, including the source-private entry phase.
            var explosion = entry.Snapshot;
            var explosionLife = entry.Lifecycle;
            if (!players.TryPrepareWorldToolBombDamage(in explosion, in explosionLife, out hurt) || hurt is null ||
                !hurt.IsRandomDistinctFrom(projectileRandom) || !hurt.ValidateExternalOwners() ||
                !hurt.TrySealForJointAdoption()) return ProjectileActorSimulationResult.Rejected;
        }
        if (!entry.IsCurrent || !entry.TryAdoptUnpublished(out current, out bool expired) || expired)
            return ProjectileActorSimulationResult.Rejected;
        life = entry.Lifecycle;
        ExceptionDispatchInfo? failure = null;
        bool finished = false;
        try
        {
            // Source28 Damage is before gore and29. The observer may retire or
            // replace this exact generation; never resume using a new occupant.
            if (initial.Type.Value == 28)
            {
                var contactCurrent = current;
                var contactRandom = projectileRandom.Clone();
                try
                {
                    if (!activeOrdinaryArrowCombat!.TryProcessWorldToolBombContacts(in current, out _, out contactCurrent, out contactRandom))
                        return ProjectileActorSimulationResult.Rejected;
                }
                catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
                if (!TryReadRetainedBomb(initial, in life, out current) || current != contactCurrent || !projectileRandom.HasSameState(contactRandom))
                { failure?.Throw(); return ProjectileActorSimulationResult.Applied; }
            }
            if (!projectiles.TryPrepareSimulation(in current, out var tail) || tail is null ||
                !toolActorWorld!.TryBeginWorldToolBombStep(tail, projectileRandom, projectileRandom.Clone(), out var world) || world is null)
                return ProjectileActorSimulationResult.Rejected;
            using (world)
            {
                if (!VanillaWorldToolBomb1458.TryAdvanceKillEffects(initial.Type, world.Random.Next))
                    return ProjectileActorSimulationResult.Rejected;
                // Source Kill resets the tracked fuse sound, returns to22px,
                // sends29 at its saved entry origin, then explodes terrain.
                // Global liquid and the common inactive movement tail still run.
                var resetLife = life with { LocalAi = life.LocalAi with { Ai2 = 0f } };
                var resized = BombUpdate(current, killOrigin.X, killOrigin.Y,
                    current.VelocityX, current.VelocityY, current.Ai);
                var wind = ((VanillaProjectileWorldStateStepper)stepper!).CaptureGemStaffWind();
                float tailVx = current.VelocityX;
                if (!world.TrySetSourceActorView(in resized, in resetLife) ||
                    !world.TryStageTerrain(in killOrigin, initial.Type) ||
                    !world.TryApplyPostAiWind(killOrigin.X, killOrigin.Y, in wind, ref tailVx) ||
                    !world.TryCaptureLiquid(killOrigin.X, killOrigin.Y, in life,
                        out var liquid, out byte wetCount, out byte colourDraws)) return ProjectileActorSimulationResult.Rejected;
                for (int draw = 0; draw < colourDraws; draw++) world.Random.Next(6);
                var state = BombUpdate(current, killOrigin.X + tailVx, killOrigin.Y + current.VelocityY,
                    tailVx, current.VelocityY, current.Ai);
                var ending = life with { Liquid = liquid, SourceWetCount = wetCount,
                    LocalAi = life.LocalAi with { Ai2 = 0f } };
                if (!world.TryRecordMovementReads(in current, in state) ||
                    !tail.TryStageWorldToolBombKilledMotion(in state, in ending) ||
                    !tail.TryStageMotion(in state, in ending) || !tail.TryQueueWorldToolBombDespawn(in killOrigin) ||
                    !world.TrySeal() || !world.IsCurrent || !tail.IsCurrent ||
                    !((VanillaProjectileWorldStateStepper)stepper!).IsGemStaffWindCurrent(in wind) ||
                    hurt is not null && (!hurt.IsRandomDistinctFrom(projectileRandom) || !hurt.IsCurrentOwned)) return ProjectileActorSimulationResult.Rejected;
                if (!tail.TryAdoptUnpublished(out _, out expired) || !expired || !world.TryAdoptUnpublished() ||
                    hurt is not null && !hurt.TryAdoptUnpublished())
                    throw new InvalidOperationException("A source world bomb lost its joint Kill participants.");
                finished = true;
                if (hurt is not null)
                {
                    while (true)
                    {
                        try { if (!hurt.TryPublishNext()) break; }
                        catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
                    }
                }
                try { PublishBomb(tail, false); } catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
                try { world.TryPublish(); } catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
            }
            failure?.Throw();
            return ProjectileActorSimulationResult.Applied;
        }
        finally
        {
            // Once source Kill starts it cannot remain a new active explosion on
            // the next tick. This is retirement only, never a replay/refund; exact
            // source-private role/lifecycle and generation remain mandatory.
            if (!finished && TryReadRetainedBomb(initial, in life, out var retained) &&
                projectiles.TryPrepareSimulation(in retained, out var cleanup) && cleanup is not null)
            {
                var body = BombUpdate(retained, retained.PositionX, retained.PositionY,
                    retained.VelocityX, retained.VelocityY, retained.Ai);
                if (cleanup.TryStageMotion(in body, in life) && cleanup.TryQueueWorldToolBombDespawn(in killOrigin) &&
                    cleanup.IsCurrent && cleanup.TryAdoptUnpublished(out _, out bool ended) && ended)
                {
                    try { PublishBomb(cleanup, false); }
                    catch (Exception error) when (failure is not null) { _ = error; }
                }
            }
        }
    }

    private bool TryReadRetainedBomb(in ProjectileSnapshot birth, in ProjectileLifecycleState expectedLife,
        out ProjectileSnapshot current) => projectiles.TryGet(birth.Handle, out current) &&
        current.Spawner == byte.MaxValue && current.Type == birth.Type && current.OriginalDamage == birth.OriginalDamage &&
        current.Damage == (birth.Type.Value == 28 ? 100 : 60) && current.KnockBack == VanillaWorldToolBomb1458.ExplosionKnockBack &&
        projectiles.IsCombatTrusted(birth.Handle) && !projectiles.TryGetCombatTrustedOwner(birth.Handle, out _) &&
        !projectiles.TryGetServerNpcSource(birth.Handle, out _) && projectiles.TryGetLifecycle(birth.Handle, out var life) &&
        life == expectedLife && life.NpcProjectile && life.WorldToolBombKillInProgress;

    private void PublishBomb(RuntimeProjectileStore.SimulationPreparation publication, bool worldBounds)
    {
        if (replication is not null) replication.TryPublishWorldToolBombActor(publication, worldBounds);
        else while (publication.TryPublishNext()) { }
    }

    private static ProjectileStateUpdate BombUpdate(in ProjectileSnapshot source, float x, float y,
        float vx, float vy, in ProjectileAiState ai) => new(source.Type, source.Spawner, x, y, vx, vy, ai,
            source.BannerIdToRespondTo, source.Damage, source.KnockBack, source.OriginalDamage);
}
