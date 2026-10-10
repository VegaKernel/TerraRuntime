using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private RuntimeProjectileNpcCombatPass? activeOrdinaryArrowCombat;
    private Func<ProjectileSnapshot, ProjectileActorSimulationResult>? ordinaryArrowActor;

    private ProjectileActorSimulationResult TrySimulateOrdinaryArrowActor(ProjectileSnapshot initial)
    {
        var combat = activeOrdinaryArrowCombat;
        if (combat is null || stepper is not VanillaProjectileWorldStateStepper concrete ||
            initial.Type.Value is not (4 or 5)) return ProjectileActorSimulationResult.NotApplicable;
        if (!combat.TryPrepareOrdinaryArrowContinuation(in initial, concrete, out var actor, out bool owned))
        {
            if (!owned) return ProjectileActorSimulationResult.NotApplicable;
            combat.MarkOrdinaryArrowHandled(in initial);
            return ProjectileActorSimulationResult.Rejected;
        }
        using (actor)
        {
            if (actor is null)
            {
                combat.MarkOrdinaryArrowHandled(in initial);
                return ProjectileActorSimulationResult.Rejected;
            }
            var historical = new List<ProjectileSnapshot>();
            for (int i = 0; actor.TryGetProjectilePublication(i, out var kind, out var snapshot); i++)
            {
                if (kind != ProjectileStateCommitKind.Update)
                {
                    combat.MarkOrdinaryArrowHandled(in initial);
                    return ProjectileActorSimulationResult.Rejected;
                }
                historical.Add(snapshot);
            }
            RuntimeProjectileReplicationRegistry.OrdinaryArrowBaselinePreparation? baseline = null;
            if (replication is not null && !replication.TryPrepareOrdinaryArrowBaseline(projectiles,
                    in initial, actor.FinalProjectile, historical.ToArray(), out baseline))
            {
                combat.MarkOrdinaryArrowHandled(in initial);
                return ProjectileActorSimulationResult.Rejected;
            }
            using (baseline)
            {
                if (!combat.CanMarkOrdinaryArrowHandled(in initial) || !actor.CanAdopt ||
                    baseline is not null && !baseline.IsCurrent || !actor.TryAdoptUnpublished())
                {
                    combat.MarkOrdinaryArrowHandled(in initial);
                    return ProjectileActorSimulationResult.Rejected;
                }
                // Concrete callback-free final tail: actor/NPC/RNG/status/immunity/ledger are already adopted.
                if (baseline is not null && !baseline.TryAdoptUnpublished() ||
                    !combat.MarkOrdinaryArrowHandled(in initial))
                    throw new InvalidOperationException("An admitted arrow lost its replication or pass owner.");
                actor.TryPublish();
                return ProjectileActorSimulationResult.Applied;
            }
        }
    }
}
