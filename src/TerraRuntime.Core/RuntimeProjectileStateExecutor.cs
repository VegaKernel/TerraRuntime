using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public enum ProjectileSimulationTerminationReason : byte
{
    None = 0,
    LifetimeExpired = 1,
    TileCollision = 2,
    BehaviorKill = 3,
    WorldBounds = 4
}

/// <summary>
/// One local Terraria projectile subupdate. <see cref="VanillaNumUpdates"/> mirrors the value visible after
/// vanilla decrements Projectile.numUpdates at the start of the while-loop: the final subupdate is -1.
/// TerminationReason is semantic simulation state for the current local pipeline only; it is never a wire field.
/// </summary>
public readonly record struct ProjectileSimulationStepContext(
    ProjectileSnapshot Projectile,
    ProjectileLifecycleState Lifecycle,
    int SubupdateIndex,
    int SubupdatesPerWorldTick,
    ProjectileSimulationTerminationReason TerminationReason = ProjectileSimulationTerminationReason.None)
{
    public int VanillaNumUpdates => SubupdatesPerWorldTick - SubupdateIndex - 2;

    public bool IsFinalSubupdate => VanillaNumUpdates == -1;
}

/// <summary>
/// State produced after one complete local projectile subupdate. TimeLeft is the post-subupdate value after
/// any AI refresh/adjustment and the ordinary vanilla lifetime decrement. Liquid is an optional runtime-only
/// lifecycle override; null preserves the prior authoritative liquid history for steppers that do not own it.
/// A non-None TerminationReason must accompany a non-positive TimeLeft; a zero lifetime without an explicit
/// reason is normalized to LifetimeExpired by the runtime for compatibility with existing steppers.
/// </summary>
public readonly record struct ProjectileSimulationStepResult(
    ProjectileStateUpdate State,
    int TimeLeft,
    ProjectileLiquidState? Liquid = null,
    ProjectileSimulationTerminationReason TerminationReason = ProjectileSimulationTerminationReason.None);

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

/// <summary>
/// Observes only projectile simulation paths whose final generation-safe state commit succeeded. The span is
/// backed by executor-owned scratch storage and is valid only for the synchronous callback. This boundary is
/// suitable for side effects that must never escape a rejected speculative simulation. Effects whose mutation
/// must influence a later extraUpdate still require transactional/in-step modeling rather than deferred replay.
/// </summary>
public interface IProjectileSimulationCommitSink
{
    void ProjectileSimulationCommitted(
        in ProjectileSnapshot initialProjectile,
        in ProjectileLifecycleState initialLifecycle,
        ReadOnlySpan<ProjectileSimulationStepResult> subupdates,
        in ProjectileSnapshot finalProjectile,
        bool expired);
}

/// <summary>
/// Receives a semantic termination only after the authoritative generation-safe removal commit succeeds. Tile
/// collision is exposed here for observation/cleanup; behavior that must change the collision result should use
/// the synchronous projectile behavior pipeline, where the termination reason is visible before commit.
/// </summary>
public interface IProjectileTerminationCommitSink
{
    void ProjectileTerminated(
        in ProjectileSnapshot initialProjectile,
        in ProjectileSnapshot finalProjectile,
        ProjectileSimulationTerminationReason reason);
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
    private readonly IProjectileSimulationCommitSink? _commitSink;
    private readonly IProjectileTerminationCommitSink? _terminationSink;
    private readonly ProjectileSnapshot[] _snapshotBuffer;
    private readonly ProjectileSimulationStepResult[] _stepBuffer;

    public RuntimeProjectileStateExecutor(
        RuntimeProjectileStore projectiles,
        IProjectileSimulationCommitSink? commitSink = null,
        IProjectileTerminationCommitSink? terminationSink = null)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        _projectiles = projectiles;
        _commitSink = commitSink;
        _terminationSink = terminationSink;
        _snapshotBuffer = new ProjectileSnapshot[projectiles.Capacity];
        _stepBuffer = new ProjectileSimulationStepResult[VanillaProjectileUpdateFacts.MaximumExtraUpdates + 1];
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
            int recordedSubupdates = 0;
            bool hasProposal = false;
            bool invalid = false;

            for (int subupdate = 0; subupdate < subupdates; subupdate++)
            {
                var context = new ProjectileSimulationStepContext(
                    currentProjectile,
                    currentLifecycle,
                    subupdate,
                    subupdates);

                if (!stepper.TryStepState(in context, out ProjectileSimulationStepResult proposedResult))
                {
                    if (hasProposal)
                        invalid = true;
                    break;
                }

                if (!TryNormalizeTermination(in proposedResult, out ProjectileSimulationStepResult next))
                {
                    invalid = true;
                    break;
                }

                ProjectileStateUpdate nextState = next.State;
                if (!RuntimeProjectileStore.IsValidState(in nextState) ||
                    !TryProjectLifecycle(
                        currentProjectile.Type,
                        nextState.Type,
                        currentLifecycle,
                        next.TimeLeft,
                        next.Liquid,
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

                _stepBuffer[recordedSubupdates++] = next;
                finalResult = next;
                currentProjectile = Project(in currentProjectile, in nextState);
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
                    currentLifecycle.Liquid,
                    out ProjectileSnapshot committed,
                    out bool expired))
            {
                applied++;
                if (_commitSink is not null)
                {
                    _commitSink.ProjectileSimulationCommitted(
                        in projectile,
                        in lifecycle,
                        _stepBuffer.AsSpan(0, recordedSubupdates),
                        in committed,
                        expired);
                }

                if (expired && _terminationSink is not null)
                {
                    _terminationSink.ProjectileTerminated(
                        in projectile,
                        in committed,
                        finalResult.TerminationReason);
                }
            }
            else
            {
                rejected++;
            }
        }

        return new ProjectileStateTickSummary(examined, proposed, applied, rejected);
    }

    internal static bool TryNormalizeTermination(
        in ProjectileSimulationStepResult proposed,
        out ProjectileSimulationStepResult normalized)
    {
        ProjectileSimulationTerminationReason reason = proposed.TerminationReason;
        if (reason is not ProjectileSimulationTerminationReason.None and
            not ProjectileSimulationTerminationReason.LifetimeExpired and
            not ProjectileSimulationTerminationReason.TileCollision and
            not ProjectileSimulationTerminationReason.BehaviorKill and
            not ProjectileSimulationTerminationReason.WorldBounds)
        {
            normalized = default;
            return false;
        }

        if (proposed.TimeLeft > 0)
        {
            if (reason != ProjectileSimulationTerminationReason.None)
            {
                normalized = default;
                return false;
            }

            normalized = proposed;
            return true;
        }

        normalized = reason == ProjectileSimulationTerminationReason.None
            ? proposed with { TerminationReason = ProjectileSimulationTerminationReason.LifetimeExpired }
            : proposed;
        return true;
    }

    private static bool TryProjectLifecycle(
        ProjectileTypeId previousType,
        ProjectileTypeId nextType,
        ProjectileLifecycleState previous,
        int timeLeft,
        ProjectileLiquidState? liquid,
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

        next = new ProjectileLifecycleState(
            timeLeft,
            netImportant,
            liquid ?? previous.Liquid);
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
