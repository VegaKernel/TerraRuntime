using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private RuntimeProjectilePlayerCombatPass? groundCombatPlayerCombat;

    internal void BindPlayerCombat(RuntimeProjectilePlayerCombatPass combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        if (groundCombatPlayerCombat is not null || activeToolAwareTick ||
            !combat.OwnsGroundCombatWorld(projectiles, npcs, projectileRandom, players, terminationEffects))
            throw new ArgumentException("Player combat must use this world's projectile, player, NPC and RNG owners.", nameof(combat));
        combat.BindGroundCombatActorOwner();
        groundCombatPlayerCombat = combat;
    }

    private ProjectileActorSimulationResult TrySimulateGroundCombatActor(ProjectileSnapshot initial)
    {
        if (!VanillaGroundCombatPlayerHit1458.IsSupported(initial.Type) ||
            !projectiles.TryGetServerNpcSource(initial.Handle, out var sourceNpc))
            return ProjectileActorSimulationResult.NotApplicable;
        var combat = groundCombatPlayerCombat;
        if (combat is null || !combat.MarkGroundCombatHandled(in initial) || worldTiles is null ||
            stepper is not VanillaProjectileWorldStateStepper concrete ||
            !projectiles.TryPrepareSimulation(in initial, out var actor) || actor is null ||
            actor.Lifecycle.Reflected || actor.Lifecycle.SourceWetCount > 10 ||
            !VanillaDefinitionCatalog.TryGet(initial.Type, out var definition) ||
            !concrete.TryCaptureGroundCombatTerrain(in initial, out var terrain) ||
            !combat.TryPrepareGroundCombatContacts(in initial, sourceNpc, terrain, out var humans, out var servers) ||
            humans is null)
            return ProjectileActorSimulationResult.Rejected;

        var originalLife = actor.Lifecycle;
        if (VanillaGroundCombatPlayerHit1458.ConsumesPlayerPenetration(initial.Type) &&
            (originalLife.PenetrateOverride ?? 1) != 1) return ProjectileActorSimulationResult.Rejected;
        var randomBefore = projectileRandom.Clone();
        var plannedRandom = randomBefore.Clone();
        int steps = VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(initial.Type, initial.Ai);
        ProjectileSimulationTerminationReason reason = ProjectileSimulationTerminationReason.None;
        ProjectileKillOrigin? killOrigin = null;
        bool consumed = false;

        for (int subupdate = 0; subupdate < steps && actor.IsActive; subupdate++)
        {
            var current = actor.Snapshot;
            var life = actor.Lifecycle;
            var context = new ProjectileSimulationStepContext(current, life, subupdate, steps);
            if (!concrete.TryStepState(in context, out var proposed) ||
                !RuntimeProjectileStateExecutor.TryNormalizeTermination(in proposed, out var next) ||
                !IsGroundCombatStep(in current, in next)) return ProjectileActorSimulationResult.Rejected;
            RuntimeProjectileSimulationCommitSink.ConsumeGroundCombatOffers(in next, plannedRandom);
            var afterLife = GroundCombatLife(in life, in current, in next);
            bool canContact = next.TimeLeft > 0 ||
                next.TerminationReason == ProjectileSimulationTerminationReason.LifetimeExpired;
            var contactLife = afterLife with { TimeLeft = canContact ? Math.Max(1, next.TimeLeft + 1) : 0 };
            if (!actor.TryStageMotion(next.State, contactLife) ||
                next.TerminationReason != ProjectileSimulationTerminationReason.WorldBounds &&
                    !actor.TryStageSourceDirection(next.SourceDirection)) return ProjectileActorSimulationResult.Rejected;

            if (canContact)
            {
                var shot = actor.Snapshot;
                for (int slot = 0; slot < byte.MaxValue; slot++)
                {
                    bool contact = false;
                    if (humans.HasPlayer(slot))
                    {
                        if (!humans.TryStageTarget(slot, in shot, in definition, plannedRandom, out contact))
                            return ProjectileActorSimulationResult.Rejected;
                    }
                    else if (servers?.HasPlayer(slot) == true &&
                        !servers.TryStageTarget(slot, in shot, in definition, plannedRandom, out contact))
                        return ProjectileActorSimulationResult.Rejected;
                    if (contact && VanillaGroundCombatPlayerHit1458.ConsumesPlayerPenetration(initial.Type))
                    {
                        consumed = true;
                        afterLife = afterLife with { PenetrateOverride = 0 };
                        break;
                    }
                }
            }

            reason = next.TerminationReason;
            if (consumed && reason == ProjectileSimulationTerminationReason.None)
                reason = ProjectileSimulationTerminationReason.BehaviorKill;
            if (reason != ProjectileSimulationTerminationReason.None)
            {
                var death = actor.Snapshot;
                killOrigin = next.KillOrigin ?? (reason == ProjectileSimulationTerminationReason.WorldBounds
                    ? null : new ProjectileKillOrigin(death.PositionX, death.PositionY));
                var termination = new ProjectileTerminationCommit(initial, death, reason,
                    actor.CombatTrusted, actor.TrustedOwner, sourceNpc, killOrigin)
                { NpcProjectile = originalLife.NpcProjectile };
                // Consume the existing exact Kill offers on the SAME working stream before
                // any inactive tail, joint adoption or callback. Publication never redraws them.
                RuntimeProjectileTerminationEffectSink.ConsumeSourceOffers(in termination, plannedRandom);
                afterLife = afterLife with { TimeLeft = 0 };
                if (reason == ProjectileSimulationTerminationReason.LifetimeExpired && subupdate + 1 < steps &&
                    VanillaProjectileUpdateFacts.SupportsInactiveLifetimeMotion1458(initial.Type))
                {
                    var tailContext = new ProjectileSimulationStepContext(death, afterLife, subupdate + 1, steps,
                        ProjectileSimulationTerminationReason.LifetimeExpired, KillOrigin: killOrigin);
                    if (!concrete.TryStepInactiveLifetimeMotion(in tailContext, out var tailProposed) ||
                        !RuntimeProjectileStateExecutor.TryNormalizeTermination(in tailProposed, out var tail) ||
                        !IsGroundCombatStep(in death, in tail) || tail.TimeLeft != 0 ||
                        tail.TerminationReason != ProjectileSimulationTerminationReason.LifetimeExpired ||
                        tail.State.Ai != death.Ai || tail.State.Damage != death.Damage ||
                        tail.LocalAi is { } local && local != afterLife.LocalAi ||
                        tail.PenetrateOverride is { } penetration && penetration != afterLife.PenetrateOverride)
                        return ProjectileActorSimulationResult.Rejected;
                    RuntimeProjectileSimulationCommitSink.ConsumeGroundCombatOffers(in tail, plannedRandom);
                    afterLife = GroundCombatLife(in afterLife, in death, in tail) with { TimeLeft = 0 };
                    if (!actor.TryStageMotion(tail.State, afterLife)) return ProjectileActorSimulationResult.Rejected;
                    break;
                }
            }
            if (actor.IsActive && !actor.TryStageMotion(next.State, afterLife)) return ProjectileActorSimulationResult.Rejected;
        }

        if (actor.IsActive ? !actor.TryQueuePublication() : !actor.TryQueueGroundCombatDespawn(killOrigin))
            return ProjectileActorSimulationResult.Rejected;
        // The external clock can call back. It is read FIRST; every concrete currentness
        // comparison follows it, so a final clock/provider mutation cannot escape validation.
        if (!combat.IsGroundCombatClockCurrent || !actor.IsCurrent || !terrain.IsCurrent ||
            !projectileRandom.HasSameState(randomBefore) || !humans.IsCurrent || servers is not null && !servers.IsCurrent)
            return ProjectileActorSimulationResult.Rejected;
        if (!actor.TryAdoptUnpublished(out var accepted, out bool expired)) return ProjectileActorSimulationResult.Rejected;
        if (!humans.TryAdoptUnpublished() || servers is not null && !servers.TryAdoptUnpublished())
            throw new InvalidOperationException("A preflighted projectile/player participant changed without a callback.");
        projectileRandom.CopyStateFrom(plannedRandom);
        combat.AdoptGroundCombatMetrics(humans, servers, consumed);

        ExceptionDispatchInfo? failure = null;
        if (expired)
        {
            var termination = new ProjectileTerminationCommit(initial, accepted, reason,
                actor.CombatTrusted, actor.TrustedOwner, sourceNpc, killOrigin)
            { NpcProjectile = originalLife.NpcProjectile };
            try { terminationEffects.PublishAfterSourceOffers(in termination); }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
        }
        try { humans.Publish(); }
        catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        try { servers?.Publish(); }
        catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        try { while (actor.TryPublishNext()) { } }
        catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        failure?.Throw();
        return ProjectileActorSimulationResult.Applied;
    }

    private static bool IsGroundCombatStep(in ProjectileSnapshot previous, in ProjectileSimulationStepResult next) =>
        RuntimeProjectileStore.IsValidState(next.State) && next.State.Type == previous.Type &&
        next.State.Spawner == previous.Spawner && next.State.Damage == previous.Damage &&
        next.State.OriginalDamage == previous.OriginalDamage && next.State.KnockBack == previous.KnockBack &&
        next.State.BannerIdToRespondTo == previous.BannerIdToRespondTo && next.PlayerBuff is null &&
        next.NpcHealing is null && !next.OrdinaryAmmoSourceUpdate;

    private static ProjectileLifecycleState GroundCombatLife(in ProjectileLifecycleState previous,
        in ProjectileSnapshot current, in ProjectileSimulationStepResult next) => previous with
    {
        TimeLeft = next.TimeLeft,
        Liquid = next.Liquid ?? previous.Liquid,
        LocalAi = next.LocalAi ?? previous.LocalAi,
        PenetrateOverride = next.PenetrateOverride ?? previous.PenetrateOverride,
        SourceWetCount = next.SourceWetCountOverride ?? previous.SourceWetCount,
        OldVelocityX = current.VelocityX,
        OldVelocityY = current.VelocityY
    };
}
