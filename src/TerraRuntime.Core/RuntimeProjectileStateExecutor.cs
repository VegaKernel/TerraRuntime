using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// One local Terraria projectile subupdate. <see cref="VanillaNumUpdates"/> mirrors the value visible after
/// vanilla decrements Projectile.numUpdates at the start of the while-loop: the final subupdate is -1.
/// Lifecycle is runtime-only state and is deliberately not projected through packet 27.
/// </summary>
public readonly record struct ProjectileSimulationStepContext(
    ProjectileSnapshot Projectile,
    ProjectileLifecycleState Lifecycle,
    int SubupdateIndex,
    int SubupdatesPerWorldTick)
{
    public int VanillaNumUpdates => SubupdatesPerWorldTick - SubupdateIndex - 2;

    public bool IsFinalSubupdate => VanillaNumUpdates == -1;
}

/// <summary>
/// State produced after one complete local projectile subupdate. TimeLeft is the post-subupdate value after
/// any AI refresh/adjustment and the ordinary vanilla lifetime decrement. Keeping it explicit lets a vanilla
/// AI implementation model lifetime mutations without exposing those mutations to client packet ingress.
/// </summary>
public readonly record struct ProjectileSimulationStepResult(
    ProjectileStateUpdate State,
    int TimeLeft);

/// <summary>
/// State-only projectile simulation stepper. Returning false on the first subupdate means the stepper does
/// not own/support that projectile. Once it returns true for a projectile it must return true for every
/// remaining subupdate in that world tick; an inconsistent later false is rejected without a partial commit.
/// </summary>
public interface IProjectileStateStepper
{
    bool TryStepState(
        in ProjectileSimulationStepContext projectile,
        out ProjectileSimulationStepResult next);
}

/// <summary>Bounded accounting for one projectile state-transition pass.</summary>
public readonly record struct ProjectileStateTickSummary(
    int Examined,
    int Proposed,
    int Applied,
    int Rejected);

/// <summary>
/// Runs allocation-stable projectile simulation against a pre-pass snapshot of the live table. Vanilla
/// extraUpdates execute as local subupdates, but only the final state is committed to the authoritative store,
/// so one world tick cannot amplify replication into one packet-27 commit per subupdate. Every final commit is
/// generation-safe, therefore reentrant despawn/slot-reuse cannot let stale simulation mutate a replacement.
/// TerrariaServer 1.4.5.8 updates only slots 0..999; physical overflow slot 1000 is intentionally excluded.
/// </summary>
public sealed class RuntimeProjectileStateExecutor
{
    private readonly RuntimeProjectileStore _projectiles;
    private readonly ProjectileSnapshot[] _snapshotBuffer;

    public RuntimeProjectileStateExecutor(RuntimeProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        _projectiles = projectiles;
        _snapshotBuffer = new ProjectileSnapshot[projectiles.Capacity];
    }

    public ProjectileStateTickSummary Tick(IProjectileStateStepper stepper)
    {
        ArgumentNullException.ThrowIfNull(stepper);

        int captured = _projectiles.CopyActive(_snapshotBuffer);
        int examined = 0;
        int proposed = 0;
        int applied = 0;
        int rejected = 0;

        for (int index = 0; index < captured; index++)
        {
            ProjectileSnapshot projectile = _snapshotBuffer[index];
            if (projectile.Handle.Slot >= RuntimeProjectileStore.VanillaPhysicalSlotCount)
                continue;

            examined++;
            if (!_projectiles.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle))
            {
                rejected++;
                continue;
            }

            int subupdates = VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(projectile.Type);
            ProjectileSnapshot currentProjectile = projectile;
            ProjectileLifecycleState currentLifecycle = lifecycle;
            ProjectileSimulationStepResult finalResult = default;
            bool hasProposal = false;
            bool invalid = false;

            for (int subupdate = 0; subupdate < subupdates; subupdate++)
            {
                var context = new ProjectileSimulationStepContext(
                    currentProjectile,
                    currentLifecycle,
                    subupdate,
                    subupdates);

                if (!stepper.TryStepState(in context, out ProjectileSimulationStepResult next))
                {
                    if (hasProposal)
                        invalid = true;
                    break;
                }

                if (!RuntimeProjectileStore.IsValidState(in next.State) ||
                    !TryProjectLifecycle(
                        currentProjectile.Type,
                        next.State.Type,
                        currentLifecycle,
                        next.TimeLeft,
                        out ProjectileLifecycleState nextLifecycle))
                {
                    invalid = true;
                    break;
                }

                if (!hasProposal)
                {
                    hasProposal = true;
                    proposed++;
                }

                finalResult = next;
                currentProjectile = Project(in currentProjectile, in next.State);
                currentLifecycle = nextLifecycle;

                if (next.TimeLeft <= 0)
                    break;
            }

            if (!hasProposal)
                continue;

            if (invalid)
            {
                rejected++;
                continue;
            }

            ProjectileStateUpdate finalState = finalResult.State;
            if (_projectiles.TryCommitSimulationStep(
                    projectile.Handle,
                    in finalState,
                    finalResult.TimeLeft,
                    out _,
                    out _))
            {
                applied++;
            }
            else
            {
                rejected++;
            }
        }

        return new ProjectileStateTickSummary(examined, proposed, applied, rejected);
    }

    private static bool TryProjectLifecycle(
        ProjectileTypeId previousType,
        ProjectileTypeId nextType,
        ProjectileLifecycleState previous,
        int timeLeft,
        out ProjectileLifecycleState next)
    {
        bool netImportant = previous.NetImportant;
        if (previousType != nextType)
        {
            if (!VanillaProjectileLifecycleFacts.TryGetDefaults(
                    nextType,
                    out VanillaProjectileLifecycleDefaults defaults))
            {
                next = default;
                return false;
            }

            netImportant = defaults.NetImportant;
        }

        next = new ProjectileLifecycleState(timeLeft, netImportant);
        return true;
    }

    private static ProjectileSnapshot Project(
        in ProjectileSnapshot current,
        in ProjectileStateUpdate update) =>
        new(
            current.Handle,
            current.Revision,
            update.Type,
            update.Spawner,
            update.PositionX,
            update.PositionY,
            update.VelocityX,
            update.VelocityY,
            update.Ai,
            update.BannerIdToRespondTo,
            update.Damage,
            update.KnockBack,
            update.OriginalDamage);
}
