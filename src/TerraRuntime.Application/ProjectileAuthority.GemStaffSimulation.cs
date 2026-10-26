using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private ProjectileActorSimulationResult TrySimulateGemStaffActor(ProjectileSnapshot initial)
    {
        if (!VanillaGemStaffProjectile1458.IsGemStaffProjectile(initial.Type))
            return ProjectileActorSimulationResult.NotApplicable;
        if (activeOrdinaryArrowCombat is not { } combat || worldTiles is null ||
            stepper is not VanillaProjectileWorldStateStepper concrete ||
            players.MagicItemWindowsArithmetic is not { } windows ||
            !projectiles.TryGet(initial.Handle, out var current) || current != initial ||
            !projectiles.TryGetLifecycle(initial.Handle, out var lifecycle) ||
            lifecycle.GemStaffFeatures is not { } features || lifecycle.GemStaff is not { } gem ||
            lifecycle.GemStaffKillInProgress || lifecycle.TimeLeft <= 0 ||
            lifecycle.GemStaffUpdateCount == long.MaxValue ||
            !VanillaGemStaffProjectile1458.TryDecodeFeatures(current.Ai.Ai0, out var encoded) || encoded != features ||
            !projectiles.TryGetCombatTrustedOwner(initial.Handle, out var owner)) return ProjectileActorSimulationResult.Rejected;

        bool ownerInactive = !players.TryGet(owner, out _);
        RuntimePlayerProjectileUseCapture? ownerCapture = null;
        if (!ownerInactive && (!players.TryCaptureProjectileUse(owner, out ownerCapture) || ownerCapture is null ||
            !players.TryCaptureProjectileCombatSnapshot(owner, out _))) return ProjectileActorSimulationResult.Rejected;

        long ordinal = lifecycle.GemStaffUpdateCount + 1;
        if (current.PositionX <= 0f || current.PositionY <= 0f ||
            current.PositionX + 10f >= worldTiles.Dimensions.WidthTiles * 16f ||
            current.PositionY + 10f >= worldTiles.Dimensions.HeightTiles * 16f)
        {
            var outside = new GemStaffMotionState1458(current.PositionX, current.PositionY,
                current.VelocityX, current.VelocityY, gem.Scale, gem.Rotation, lifecycle.LocalAi.Ai0,
                current.Ai.Ai1, current.Ai.Ai2);
            var update = Update(in current, in outside);
            var ending = lifecycle with { TimeLeft = 0, GemStaffUpdateCount = ordinal };
            if (!projectiles.TryPrepareSimulation(in current, out var bounds) || bounds is null ||
                !bounds.TryStageMotion(in update, in ending) || !bounds.TryQueueGemStaffDespawn() ||
                !bounds.IsCurrent || !bounds.TryAdoptUnpublished(out _, out bool expired) || !expired)
                return ProjectileActorSimulationResult.Rejected;
            if (replication is not null)
                return replication.TryPublishGemStaffActor(bounds, false, true) ?
                    ProjectileActorSimulationResult.Applied : ProjectileActorSimulationResult.Rejected;
            while (bounds.TryPublishNext()) { }
            return ProjectileActorSimulationResult.Applied;
        }
        var randomBefore = projectileRandom.Clone();
        var plannedRandom = randomBefore.Clone();
        var motion = new GemStaffMotionState1458(current.PositionX, current.PositionY,
            current.VelocityX, current.VelocityY, gem.Scale, gem.Rotation, lifecycle.LocalAi.Ai0,
            current.Ai.Ai1, current.Ai.Ai2);
        if (!TryCaptureGemTargets(in current, ordinal, 10, combat, out var targets, ownerInactive) ||
            !VanillaGemStaffProjectile1458.TryStepMotion(current.Type, features, in motion,
                !ownerInactive, targets, plannedRandom.Next, out var ai, windows)) return ProjectileActorSimulationResult.Rejected;

        var wind = concrete.CaptureGemStaffWind();
        var preflightPose = ai.State;
        int preflightWidth = 10;
        if ((ai.MustKill || ownerInactive) && (features & GemStaffFeatures1458.AoeExplosion) != 0)
        {
            preflightPose = preflightPose with { PositionX = preflightPose.PositionX - 6f,
                PositionY = preflightPose.PositionY - 6f };
            preflightWidth = 22;
        }
        if (!concrete.TryCaptureGemStaffTerrain(in preflightPose, preflightWidth, preflightWidth,
                lifecycle.Liquid, in wind, out _) ||
            (!ownerInactive && !players.IsCurrentProjectileUse(ownerCapture!)) ||
            (ownerInactive && players.TryGet(owner, out _)) ||
            !projectileRandom.HasSameState(randomBefore)) return ProjectileActorSimulationResult.Rejected;

        ExceptionDispatchInfo? failure = null;
        bool killed = false;
        bool forceNetUpdate = ai.TargetChanged;
        ProjectileKillOrigin? killOrigin = null;
        motion = ai.State;
        lifecycle = lifecycle with { GemStaffUpdateCount = ordinal,
            OldVelocityX = initial.VelocityX, OldVelocityY = initial.VelocityY,
            LocalAi = lifecycle.LocalAi with { Ai0 = motion.LocalAi0 },
            GemStaff = gem with { Scale = motion.Scale, Rotation = motion.Rotation } };
        if (!Commit(in motion, in lifecycle, plannedRandom, false, false, false))
            return ProjectileActorSimulationResult.Rejected;

        try
        {
            if ((ai.MustKill || ownerInactive) && !Kill()) return ProjectileActorSimulationResult.Rejected;
            int width = killed && (features & GemStaffFeatures1458.AoeExplosion) != 0 ? 22 : 10;
            if (!concrete.TryCaptureGemStaffTerrain(in motion, width, width, lifecycle.Liquid,
                    in wind, out var terrain)) return ProjectileActorSimulationResult.Rejected;
            motion = terrain.Pose with { VelocityX = terrain.ClampedVelocityX,
                VelocityY = terrain.ClampedVelocityY };
            lifecycle = lifecycle with { Liquid = terrain.Liquid };
            if (terrain.HasImpact)
            {
                var impactRandom = projectileRandom.Clone();
                _ = impactRandom.Next(3);
                var impactInput = terrain.Pose;
                var impactQuery = current with { PositionX = impactInput.PositionX, PositionY = impactInput.PositionY };
                if ((!ownerInactive && !TryCaptureGemTargets(in impactQuery, ordinal, width, combat, out targets)) ||
                    !VanillaGemStaffProjectile1458.TryResolveTileImpact(current.Type, features, in impactInput,
                        terrain.ClampedVelocityX, terrain.ClampedVelocityY, lifecycle.PenetrateOverride ?? 1,
                        !ownerInactive, targets, out var impact, windows, width, width))
                    return ProjectileActorSimulationResult.Rejected;
                motion = impact.State;
                var state = lifecycle.GemStaff!.Value;
                lifecycle = lifecycle with { PenetrateOverride = impact.Penetration,
                    GemStaff = state with { Trail = impact.ClearTrail ? default : state.Trail } };
                forceNetUpdate |= impact.Bounced && (features & GemStaffFeatures1458.Homing) != 0;
                if (!Commit(in motion, in lifecycle, impactRandom, false, killed, false))
                    return ProjectileActorSimulationResult.Rejected;
                if (impact.MustKill && !killed && !Kill()) return ProjectileActorSimulationResult.Rejected;
            }
            motion = VanillaProjectileWorldMotionResolver.CompleteGemStaffPosition(in motion, in terrain);
            if (!Commit(in motion, in lifecycle, null, false, killed, false, sourceMotionCompleted: true))
                return ProjectileActorSimulationResult.Rejected;
            if (!killed)
            {
                if (!Contacts(false)) return ProjectileActorSimulationResult.Rejected;
                if (!projectiles.TryGetLifecycle(current.Handle, out lifecycle))
                    return ProjectileActorSimulationResult.Rejected;
                var retained = lifecycle.GemStaff!.Value;
                lifecycle = lifecycle with { GemStaff = retained with
                    { Trail = retained.Trail.AdvanceAfterDamage(current.PositionX, current.PositionY) },
                    TimeLeft = lifecycle.TimeLeft - 1 };
                // Preserve the active source body until its later lifetime/penetration Kill.
                bool mustKill = lifecycle.TimeLeft <= 0 || lifecycle.PenetrateOverride == 0;
                if (mustKill)
                {
                    lifecycle = lifecycle with { TimeLeft = Math.Max(1, lifecycle.TimeLeft) };
                    if (!Commit(in motion, in lifecycle, null, false, false, false) || !Kill())
                        return ProjectileActorSimulationResult.Rejected;
                }
                else if (!Commit(in motion, in lifecycle, null, true, false, false))
                    return ProjectileActorSimulationResult.Rejected;
            }
            if (killed)
            {
                lifecycle = lifecycle with { TimeLeft = 0 };
                if (!Commit(in motion, in lifecycle, null, true, false, true))
                    return ProjectileActorSimulationResult.Rejected;
            }

            failure?.Throw();
            return ProjectileActorSimulationResult.Applied;
        }
        finally
        {
            // A callback may fail after a source hit was jointly adopted. Never replay it,
            // and never let a half-finished Kill generation survive to a later world tick.
            if (killed && projectiles.TryGet(current.Handle, out var stillCurrent) &&
                projectiles.TryGetLifecycle(current.Handle, out var retained) && retained.GemStaffKillInProgress &&
                retained.GemStaffFeatures == features && retained.GemStaffUpdateCount == ordinal &&
                projectiles.TryGetCombatTrustedOwner(current.Handle, out var terminalOwner) && terminalOwner == owner)
            {
                // An observer can revise this same terminal generation. Its revised pose
                // must not be replaced with an older source preimage, but the privately
                // sealed Kill must still retire. A replacement generation is never read
                // through the old handle and cannot enter this cleanup.
                if (stillCurrent != current && retained.GemStaff is { } revisedGem)
                {
                    current = stillCurrent;
                    motion = new(current.PositionX, current.PositionY, current.VelocityX, current.VelocityY,
                        revisedGem.Scale, revisedGem.Rotation, retained.LocalAi.Ai0, current.Ai.Ai1, current.Ai.Ai2);
                }
                lifecycle = retained with { TimeLeft = 0 };
                _ = Commit(in motion, in lifecycle, null, true, false, true);
            }
            failure?.Throw();
        }

        bool Contacts(bool area)
        {
            // Dedicated-server inactive-owner fallback has no local player PvE authority.
            if (ownerInactive) return true;
            GemStaffContactResult1458 result = new(current, false, 0);
            try { _ = combat.TryProcessGemContacts(in current, area, worldTiles, out result); }
            catch (Exception exception)
            {
                failure ??= ExceptionDispatchInfo.Capture(exception);
            }
            // A rejected NPC proposal has no hit adoption. The already-adopted source
            // update still finishes its motion/trail/lifetime on the exact retained actor.
            if (!projectiles.TryGet(result.Current.Handle, out var observed) || observed != result.Current ||
                !projectiles.TryGetLifecycle(result.Current.Handle, out var after)) return false;
            current = result.Current;
            lifecycle = after;
            return true;
        }

        bool Kill()
        {
            if (killed) return true;
            if (!VanillaGemStaffProjectile1458.TryPrepareKill(current.Type, features, current.Damage,
                    current.KnockBack, windows, out var effect)) return false;
            killOrigin = new(current.PositionX, current.PositionY);
            var random = projectileRandom.Clone();
            for (int i = 0; i < effect.RandomSamples; i++) _ = random.Next();
            var killMotion = motion;
            if (effect.HasAreaDamage)
                killMotion = killMotion with { PositionX = killMotion.PositionX + 5f - 80f,
                    PositionY = killMotion.PositionY + 5f - 80f };
            var killLifecycle = lifecycle with { TimeLeft = 0,
                PenetrateOverride = effect.HasAreaDamage ? -1 : lifecycle.PenetrateOverride };
            var damageState = Update(in current, in killMotion) with
                { Damage = checked((short)effect.Damage), KnockBack = effect.KnockBack };
            if (!projectiles.TryPrepareSimulation(in current, out var preparation) || preparation is null ||
                !preparation.TryStageGemStaffKill(in damageState, in killLifecycle) || !preparation.IsCurrent ||
                !preparation.TryAdoptUnpublished(out current, out bool expired) || expired) return false;
            projectileRandom.CopyStateFrom(random);
            killed = true;
            if (!projectiles.TryGetLifecycle(current.Handle, out lifecycle)) return false;
            motion = killMotion;
            if (effect.HasAreaDamage)
            {
                if (!combat.TryResetGemStaffKillImmunity(in current) || !Contacts(true)) return false;
                motion = motion with { PositionX = motion.PositionX + 69f, PositionY = motion.PositionY + 69f };
                if (!Commit(in motion, in lifecycle, null, false, true, false)) return false;
            }
            return true;
        }

        bool Commit(in GemStaffMotionState1458 pose, in ProjectileLifecycleState nextLifecycle,
            VanillaUnifiedRandom1458? nextRandom, bool publish, bool killedMotion, bool remove, bool sourceMotionCompleted = false)
        {
            if (!projectiles.TryPrepareSimulation(in current, out var preparation) || preparation is null) return false;
            var update = Update(in current, in pose);
            bool staged = killedMotion ? preparation.TryStageGemStaffKilledMotion(in update, in nextLifecycle) :
                preparation.TryStageMotion(in update, in nextLifecycle);
            if (!staged || sourceMotionCompleted && !preparation.TryStageSourceDirection(
                VanillaProjectileSourceDirection1458.TryResolveAfterMotion(update.Type, update.Ai,
                    update.VelocityX, out int direction) ? direction : null) || publish && !(remove && !ownerInactive ? preparation.TryQueueGemStaffDespawn(killOrigin) : preparation.TryQueuePublication()) ||
                !preparation.IsCurrent || !preparation.TryAdoptUnpublished(out current, out bool expired)) return false;
            lifecycle = nextLifecycle;
            if (nextRandom is not null) projectileRandom.CopyStateFrom(nextRandom);
            if (expired != remove) return false;
            if (publish)
                try
                {
                    if (replication is not null)
                    {
                        if (!replication.TryPublishGemStaffActor(preparation, !remove && forceNetUpdate)) return false;
                    }
                    else while (preparation.TryPublishNext()) { }
                }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            return true;
        }
    }

    private bool TryCaptureGemTargets(in ProjectileSnapshot projectile, long ordinal, int width,
        RuntimeProjectileNpcCombatPass combat, out GemStaffTarget1458[] targets, bool retainRawTarget = false)
    {
        targets = [];
        if (worldTiles is null) return false;
        if ((BitConverter.SingleToInt32Bits(projectile.Ai.Ai0) & (int)GemStaffFeatures1458.Homing) == 0) return true;
        var result = new List<GemStaffTarget1458>();
        VanillaNpcRetainedSlot[]? retained = null;
        if (retainRawTarget)
        {
            retained = new VanillaNpcRetainedSlot[npcs.Capacity];
            npcs.CopyRetainedSlots(retained);
        }
        for (int slot = 0; slot < Math.Min(200, npcs.Capacity); slot++)
        {
            if (!npcs.TryGetActive((byte)slot, out var target))
            {
                if (retained is not null && slot == (int)projectile.Ai.Ai2 - 1)
                {
                    var raw = retained[slot];
                    if (raw.Type <= 0 || !VanillaNpcDefinitionCatalog.TryGet(raw.TypeIdentity, new(raw.NetId), out var rawDefinition) ||
                        !rawDefinition.TryResolveHitbox(raw.Simulation, out var rawSize)) return false;
                    result.Add(new(slot, raw.PositionX + rawSize.Width * .5f, raw.PositionY + rawSize.Height * .5f, false, false, false));
                }
                continue;
            }
            bool chaseable = VanillaNpcChaseability1458.CanBeChasedBy(in target);
            if (!chaseable && !(retainRawTarget && slot == (int)projectile.Ai.Ai2 - 1)) continue;
            if (!VanillaNpcDefinitionCatalog.TryGet(target.TypeIdentity, target.NetIdentity, out var definition) ||
                !definition.SupportsRuntimeLifecycle || !definition.TryResolveHitbox(target.Simulation, out var size)) return false;
            bool visible = VanillaWorldCanHit.HasLineOfSight(worldTiles, projectile.PositionX, projectile.PositionY,
                width, width, target.PositionX, target.PositionY, size.Width, size.Height);
            result.Add(new(slot, target.PositionX + size.Width * .5f, target.PositionY + size.Height * .5f,
                chaseable, visible, combat.IsGemStaffLocallyImmune(projectile.Handle, target.Handle, ordinal)));
        }
        targets = result.ToArray();
        return true;
    }

    private static ProjectileStateUpdate Update(in ProjectileSnapshot current, in GemStaffMotionState1458 pose) =>
        new(current.Type, current.Spawner, pose.PositionX, pose.PositionY, pose.VelocityX, pose.VelocityY,
            current.Ai with { Ai1 = pose.Ai1, Ai2 = pose.Ai2 }, current.BannerIdToRespondTo,
            current.Damage, current.KnockBack, current.OriginalDamage);

}
