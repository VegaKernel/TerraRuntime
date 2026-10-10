using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

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
    ProjectileSimulationTerminationReason TerminationReason = ProjectileSimulationTerminationReason.None,
    ProjectilePlayerBuffApplication? PlayerBuff = null,
    ProjectileKillOrigin? KillOrigin = null,
    ProjectileNpcHealingApplication? NpcHealing = null)
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
    ProjectileSimulationTerminationReason TerminationReason = ProjectileSimulationTerminationReason.None,
    ProjectileLocalAiState? LocalAi = null,
    ProjectilePlayerBuffApplication? PlayerBuff = null,
    ProjectileKillOrigin? KillOrigin = null,
    ProjectileNpcHealingApplication? NpcHealing = null,
    int? PenetrateOverride = null,
    ProjectileCollisionTileCutOffer? CollisionTileCutOffer = null);

/// <summary>Runtime-only top-left at the source Kill boundary before a remaining position-update tail.</summary>
public readonly record struct ProjectileKillOrigin(float X, float Y)
{
    public bool IsValid => float.IsFinite(X) && float.IsFinite(Y);
}

/// <summary>One player buff proposed during a local subupdate, applied only after its projectile commit.</summary>
public readonly record struct ProjectilePlayerBuffApplication(PlayerHandle Target, BuffTypeId Type, int DurationTicks);

/// <summary>Healing proposed against the exact NPC state observed in a local projectile AI phase.</summary>
public readonly record struct ProjectileNpcHealingApplication(NpcHandle Target, NpcRevision Revision, int Amount);

/// <summary>A genuine HandleMovement collision offer, evaluated only after the simulation is accepted.</summary>
public readonly record struct ProjectileCollisionTileCutOffer;

/// <summary>
/// Projectile state and deferred-effect proposal stepper. Returning false on the first subupdate means the stepper does
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

/// <summary>Trusted owned source offers run after state adoption and before its network publication.</summary>
public interface IProjectileSimulationPrePublicationCommitSink
{
    bool OwnsCollisionTileCutOffers { get; }
    void ProjectileSimulationCommittedBeforePublication(ReadOnlySpan<ProjectileSimulationStepResult> subupdates);
}

/// <summary>
/// Receives a semantic termination only after the authoritative generation-safe removal commit succeeds. Tile
/// collision is exposed here for observation/cleanup; behavior that must change the collision result should use
/// the synchronous projectile behavior pipeline, where the termination reason is visible before commit.
/// </summary>
public readonly record struct ProjectileTerminationCommit(
    ProjectileSnapshot InitialProjectile,
    ProjectileSnapshot FinalProjectile,
    ProjectileSimulationTerminationReason Reason,
    bool CombatTrusted,
    PlayerHandle TrustedOwner,
    NpcHandle SourceNpc = default,
    ProjectileKillOrigin? KillOrigin = null);

public interface IProjectileTerminationCommitSink
{
    void ProjectileTerminated(in ProjectileTerminationCommit termination);
}

/// <summary>Bounded accounting for one projectile state-transition pass.</summary>
public readonly record struct ProjectileStateTickSummary(
    int Examined,
    int Proposed,
    int Applied,
    int Rejected);

internal enum ProjectileActorSimulationResult : byte
{
    NotApplicable = 0,
    Applied = 1,
    Rejected = 2,
}

/// <summary>
/// Runs projectile simulation against a bounded pre-pass snapshot of the live table. The generic lane uses
/// allocation-stable local subupdates and commits only the final state. An admitted concrete actor can instead
/// own its complete state transition and source-ordered publication journal at the same physical slot.
/// Generation checks prevent reentrant despawn/slot reuse from mutating a replacement.
/// TerrariaServer 1.4.5.8 updates only slots 0..999; physical overflow slot 1000 is intentionally excluded.
/// </summary>
public sealed class RuntimeProjectileStateExecutor
{
    private readonly RuntimeProjectileStore _projectiles;
    private readonly IProjectileSimulationCommitSink? _commitSink;
    private readonly IProjectileTerminationCommitSink? _terminationSink;
    private readonly RuntimeNpcStore? _npcs;
    private readonly ProjectileSnapshot[] _snapshotBuffer;
    private readonly ProjectileSimulationStepResult[] _stepBuffer;

    public RuntimeProjectileStateExecutor(
        RuntimeProjectileStore projectiles,
        IProjectileSimulationCommitSink? commitSink = null,
        IProjectileTerminationCommitSink? terminationSink = null,
        RuntimeNpcStore? npcs = null)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        _projectiles = projectiles;
        _commitSink = commitSink;
        _terminationSink = terminationSink;
        _npcs = npcs;
        _snapshotBuffer = new ProjectileSnapshot[projectiles.Capacity];
        _stepBuffer = new ProjectileSimulationStepResult[VanillaProjectileUpdateFacts.MaximumExtraUpdates + 1];
    }

    public ProjectileStateTickSummary Tick(IProjectileStateStepper stepper) => Tick(stepper, null);

    // The concrete Application arrow owner may prepare/adopt this actor before the generic motion lane.
    // A handled refusal is final for this pass; it never falls through to speculative legacy motion.
    internal ProjectileStateTickSummary Tick(IProjectileStateStepper stepper,
        Func<ProjectileSnapshot, ProjectileActorSimulationResult>? simulateActor)
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

            if (simulateActor is not null)
            {
                ProjectileActorSimulationResult actorResult = simulateActor(projectile);
                if (actorResult != ProjectileActorSimulationResult.NotApplicable)
                {
                    proposed++;
                    if (actorResult == ProjectileActorSimulationResult.Applied)
                        applied++;
                    else
                        rejected++;
                    continue;
                }
            }

            int subupdates = VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(projectile.Type, projectile.Ai);
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
                if (!RuntimeProjectileStore.IsValidState(in nextState) || !IsCurrentHealing(in next) ||
                    next.CollisionTileCutOffer.HasValue &&
                        _commitSink is not IProjectileSimulationPrePublicationCommitSink { OwnsCollisionTileCutOffers: true } ||
                    !TryProjectLifecycle(
                        currentProjectile.Type,
                        nextState.Type,
                        currentLifecycle,
                        next.TimeLeft,
                        next.Liquid,
                        next.LocalAi,
                        next.PenetrateOverride,
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

            bool wasCombatTrusted = _projectiles.IsCombatTrusted(projectile.Handle);
            PlayerHandle trustedOwner = default;
            if (wasCombatTrusted)
                _projectiles.TryGetCombatTrustedOwner(projectile.Handle, out trustedOwner);
            NpcHandle sourceNpc = default;
            _projectiles.TryGetServerNpcSource(projectile.Handle, out sourceNpc);

            ProjectileStateUpdate finalState = finalResult.State;
            if (IsCurrentHealing(in finalResult) && _projectiles.TryGet(projectile.Handle, out var retainedProjectile) &&
                retainedProjectile.Revision == projectile.Revision &&
                _projectiles.TryGetLifecycle(projectile.Handle, out var retainedLifecycle) && retainedLifecycle == lifecycle &&
                _projectiles.TryCommitSimulationStepUnpublished(
                    projectile.Handle,
                    in finalState,
                    finalResult.TimeLeft,
                    currentLifecycle.Liquid,
                    currentLifecycle.LocalAi,
                    out ProjectileSnapshot committed,
                    out bool expired,
                    currentLifecycle.PenetrateOverride))
            {
                applied++;
                if (_commitSink is IProjectileSimulationPrePublicationCommitSink beforePublication)
                    beforePublication.ProjectileSimulationCommittedBeforePublication(_stepBuffer.AsSpan(0, recordedSubupdates));
                _projectiles.TryPublishSimulationCommit(in committed, expired);
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
                    var termination = new ProjectileTerminationCommit(
                        projectile,
                        committed,
                        finalResult.TerminationReason,
                        wasCombatTrusted,
                        trustedOwner,
                        sourceNpc,
                        finalResult.KillOrigin);
                    _terminationSink.ProjectileTerminated(in termination);
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
        if (proposed.CollisionTileCutOffer.HasValue &&
            (proposed.State.Type != VanillaProjectileIds.NurseSyringeHeal || proposed.State.Damage != 0))
        {
            normalized = default;
            return false;
        }
        if (proposed.PenetrateOverride is < -1 || proposed.NpcHealing is { } heal &&
            (!heal.Target.IsAssigned || !heal.Revision.IsAssigned || heal.Amount <= 0 ||
             proposed.TimeLeft > 0 || proposed.TerminationReason != ProjectileSimulationTerminationReason.BehaviorKill))
        {
            normalized = default;
            return false;
        }
        if (proposed.KillOrigin is { } killOrigin &&
            (!killOrigin.IsValid || proposed.TimeLeft > 0 || proposed.TerminationReason == ProjectileSimulationTerminationReason.WorldBounds))
        {
            normalized = default;
            return false;
        }
        if (proposed.PlayerBuff is { } buff &&
            (!buff.Target.IsAssigned || buff.Type == VanillaBuffIds.None ||
             !VanillaBuffIds.TryCreate(buff.Type.Value, out _) || buff.DurationTicks <= 0))
        {
            normalized = default;
            return false;
        }
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

    private bool IsCurrentHealing(in ProjectileSimulationStepResult result) =>
        result.NpcHealing is not { } heal || _npcs is not null &&
        _npcs.TryGet(heal.Target, out var target) && target.Revision == heal.Revision &&
        (long)target.Simulation.Life + heal.Amount <= target.Simulation.LifeMax;

    private static bool TryProjectLifecycle(
        ProjectileTypeId previousType,
        ProjectileTypeId nextType,
        ProjectileLifecycleState previous,
        int timeLeft,
        ProjectileLiquidState? liquid,
        ProjectileLocalAiState? localAi,
        int? penetrateOverride,
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
            liquid ?? previous.Liquid)
        {
            OldVelocityX = previous.OldVelocityX,
            OldVelocityY = previous.OldVelocityY,
            Reflected = previous.Reflected,
            PenetrateOverride = penetrateOverride ?? previous.PenetrateOverride,
            LocalAi = localAi ?? previous.LocalAi
        };
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
