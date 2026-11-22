using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private ProjectileActorSimulationResult TrySimulateMoonBoulderWiring(in ProjectileSnapshot initial)
    {
        if (stepper is not VanillaProjectileWorldStateStepper concrete || toolActorWorld is null ||
            initial.Spawner != byte.MaxValue || !projectiles.TryGetServerNpcSource(initial.Handle, out var sourceNpc) ||
            !npcs.TryGet(sourceNpc, out var npc) || npc.TypeIdentity != VanillaNpcIds.MoonLordHead ||
            !VanillaDefinitionCatalog.TryGet(initial.Type, out var body))
            return ProjectileActorSimulationResult.NotApplicable;

        int count = VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(initial.Type, initial.Ai);
        var shot = initial;
        for (int subupdate = 0; subupdate < count; subupdate++)
        {
            var refused = subupdate == 0 ? ProjectileActorSimulationResult.NotApplicable : ProjectileActorSimulationResult.Applied;
            long tick = tickProvider();
            if (!projectiles.TryPrepareSimulation(in shot, out var actor) || actor is null ||
                actor.SourceNpc != sourceNpc || actor.Lifecycle.Reflected) return refused;

            var context = new ProjectileSimulationStepContext(shot, actor.Lifecycle, subupdate, count);
            if (!concrete.TryStepMoonBoulder(in context, out var proposed, out bool tileImpact) ||
                !RuntimeProjectileStateExecutor.TryNormalizeTermination(in proposed, out var next) ||
                !IsGroundCombatStep(in shot, in next)) return refused;

            bool terminal = next.TimeLeft <= 0;
            bool drySingleMove = !terminal && !tileImpact && next.Liquid is { Wet: false } &&
                IsSingleWiringMovement(in shot, in next, in body);
            // Keep the ordinary owner for launches already in an unrepresented movement phase.
            if (subupdate == 0 && !drySingleMove)
                return ProjectileActorSimulationResult.NotApplicable;

            var life = GroundCombatLife(actor.Lifecycle, in shot, in next);
            bool last = terminal || subupdate + 1 == count;
            if (!actor.TryStageMotion(next.State, life) ||
                next.TerminationReason != ProjectileSimulationTerminationReason.WorldBounds &&
                    !actor.TryStageSourceDirection(next.SourceDirection) ||
                last && !actor.TryQueuePublication() || tickProvider() != tick || !actor.IsCurrent ||
                !actor.TryAdoptUnpublished(out var accepted, out _))
                return refused;

            RuntimeProjectileSimulationCommitSink.ConsumeShimmerOffers(in next, projectileRandom);
            ExceptionDispatchInfo? error = null;
            try
            {
                if (drySingleMove)
                {
                    // HandleMovement's actor 4 uses the collision body. Projectile.Update's
                    // actor 3 uses the full body after Damage and re-reads the current world.
                    OfferBoulderPlate(in shot, in accepted, body.CollisionWidth, body.CollisionHeight,
                        body.CollisionOffsetX, body.CollisionOffsetY, WorldPressurePlateActor1458.ProjectileOrBoulder);
                    if (tickProvider() == tick && actor.IsPublicationCurrent)
                        OfferBoulderPlate(in shot, in accepted, body.Width, body.Height, 0, 0, WorldPressurePlateActor1458.Projectile);
                }
            }
            catch (Exception failure) { error = ExceptionDispatchInfo.Capture(failure); }

            try { while (actor.TryPublishNext()) { } }
            catch (Exception failure) { error ??= ExceptionDispatchInfo.Capture(failure); }
            if (terminal)
            {
                var termination = new ProjectileTerminationCommit(initial, accepted, next.TerminationReason,
                    actor.CombatTrusted, actor.TrustedOwner, sourceNpc, next.KillOrigin)
                { NpcProjectile = life.NpcProjectile };
                try { terminationEffects.ProjectileTerminated(in termination); }
                catch (Exception failure) { error ??= ExceptionDispatchInfo.Capture(failure); }
            }
            error?.Throw();
            if (terminal || tickProvider() != tick || !actor.IsPublicationCurrent) break;
            shot = accepted;
        }
        return ProjectileActorSimulationResult.Applied;
    }

    private void OfferBoulderPlate(in ProjectileSnapshot before, in ProjectileSnapshot after, int width, int height,
        float offsetX, float offsetY, WorldPressurePlateActor1458 actor)
    {
        if (toolActorWorld is null || !toolActorWorld.TryFindBoulderPressurePlate(after.PositionX + offsetX,
                after.PositionY + offsetY, width, height, before.PositionX + offsetX, before.PositionY + offsetY,
                actor, out var contact) || HasBoulderDamageCandidate(in after)) return;
        toolActorWorld.PulseProjectilePressurePlate(in contact);
    }

    private bool HasBoulderDamageCandidate(in ProjectileSnapshot shot)
    {
        if (shot.Damage <= 0) return false;
        if (!VanillaDefinitionCatalog.TryGet(shot.Type, out var body)) return true;

        // Do not take over damage. Its existing runtime pass still owns both player populations.
        // Dedicated Damage_PVE can strike friendly NPCs even though this projectile is hostile only.
        Span<PlayerStateSnapshot> targets = stackalloc PlayerStateSnapshot[byte.MaxValue];
        foreach (ref readonly var target in targets[..players.CopyCombatTargets(targets)])
        {
            if (target.IsDead) continue;
            if (target.HasMount && target.MountType > 65) return true;
            var (width, height) = target.HasMount ? VanillaPlayerMountHitbox1458.Resolve(target.MountType) :
                (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight);
            if (MayOverlap(target.PositionX, target.PositionY, width, height, in shot, in body)) return true;
        }

        for (int slot = 0; slot < npcs.Capacity; slot++)
        {
            if (!npcs.TryGetActive((byte)slot, out var target) || target.TypeIdentity == VanillaNpcIds.MoonLordHead ||
                target.Simulation.Friendly == false) continue;
            if (!VanillaNpcDefinitionCatalog.TryGet(target.TypeIdentity, target.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(target.Simulation, out var hitbox) ||
                MayOverlap(target.PositionX, target.PositionY, hitbox.Width, hitbox.Height, in shot, in body, 8))
                return true;
        }
        return false;
    }

    private static bool MayOverlap(float x, float y, float width, float height, in ProjectileSnapshot shot,
        in VanillaProjectileDefinition body, int extra = 0)
    {
        // The source compares integer damage rectangles. Keep a conservative pixel around
        // both boundaries and admit the extra NPC collision padding before excluding targets.
        float padding = extra + 1;
        return shot.PositionX <= x + width + padding && shot.PositionX + body.Width >= x - padding &&
            shot.PositionY <= y + height + padding && shot.PositionY + body.Height >= y - padding;
    }
}
