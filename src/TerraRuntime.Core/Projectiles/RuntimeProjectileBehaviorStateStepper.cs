using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Composes host-registered projectile behaviors around the verified vanilla/default stepper for one local
/// projectile subupdate. Type-scoped decorators follow the vanilla presentation ID; an optional server-archetype
/// BehaviorId can select a distinct replacement for one exact projectile generation. The authoritative executor
/// still owns extraUpdates, validation, lifetime, generation safety and replication.
/// </summary>
public sealed class RuntimeProjectileBehaviorStateStepper : IProjectileStateStepper
{
    private readonly IProjectileStateStepper vanilla;
    private readonly RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper> behaviors;
    private readonly IGameplayBehaviorFaultSink? faultSink;
    private readonly RuntimeArchetypeBehaviorRegistry<IProjectileStateStepper>? archetypeBehaviors;
    private readonly RuntimeProjectileArchetypeRegistry? archetypes;
    private readonly RuntimeProjectileArchetypeIdentityStore? identities;

    public RuntimeProjectileBehaviorStateStepper(
        IProjectileStateStepper vanilla,
        RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper> behaviors,
        IGameplayBehaviorFaultSink? faultSink = null,
        RuntimeArchetypeBehaviorRegistry<IProjectileStateStepper>? archetypeBehaviors = null,
        RuntimeProjectileArchetypeRegistry? archetypes = null,
        RuntimeProjectileArchetypeIdentityStore? identities = null)
    {
        ArgumentNullException.ThrowIfNull(vanilla);
        ArgumentNullException.ThrowIfNull(behaviors);
        this.vanilla = vanilla;
        this.behaviors = behaviors;
        this.faultSink = faultSink;
        this.archetypeBehaviors = archetypeBehaviors;
        this.archetypes = archetypes;
        this.identities = identities;
    }

    public bool TryStepState(
        in ProjectileSimulationStepContext projectile,
        out ProjectileSimulationStepResult next)
    {
        RuntimeGameplayBehaviorSnapshot<ProjectileTypeId, IProjectileStateStepper> snapshot = behaviors.Snapshot;
        bool hasTypePlan = snapshot.TryGetPlan(
            projectile.Projectile.Type,
            out GameplayBehaviorDispatchPlan<IProjectileStateStepper>? plan) &&
            plan is not null;
        bool hasArchetypeReplacement = TryResolveArchetypeReplacement(
            in projectile,
            out GameplayBehaviorBinding<IProjectileStateStepper> archetypeReplacement);

        if (!hasTypePlan && !hasArchetypeReplacement)
            return vanilla.TryStepState(in projectile, out next);

        ProjectileSimulationStepContext current = projectile;
        bool changed = false;

        ReadOnlySpan<GameplayBehaviorBinding<IProjectileStateStepper>> pre = plan is null
            ? ReadOnlySpan<GameplayBehaviorBinding<IProjectileStateStepper>>.Empty
            : plan.Pre.Span;
        for (int index = 0; index < pre.Length; index++)
        {
            GameplayBehaviorBinding<IProjectileStateStepper> binding = pre[index];
            if (!TryRunExtension(in binding, GameplayBehaviorStage.Pre, in current, out ProjectileSimulationStepResult update))
                continue;

            if (TryProjectExtensionResult(in current, in update, out ProjectileSimulationStepContext projected))
            {
                current = projected;
                changed = true;
            }
            else
            {
                ReportInvalidResult(binding.Id, GameplayBehaviorStage.Pre);
            }
        }

        bool hasReplacement = hasArchetypeReplacement || (plan?.HasReplacement ?? false);
        if (hasReplacement)
        {
            GameplayBehaviorBinding<IProjectileStateStepper> replacement = hasArchetypeReplacement
                ? archetypeReplacement
                : plan!.Replacement;
            bool replacementReturned;
            bool replacementFaulted;
            ProjectileSimulationStepResult replacementResult;
            try
            {
                replacementReturned = replacement.Behavior.TryStepState(in current, out replacementResult);
                replacementFaulted = false;
            }
            catch (Exception exception)
            {
                replacementReturned = false;
                replacementFaulted = true;
                replacementResult = default;
                ReportFault(replacement.Id, GameplayBehaviorStage.Replacement, exception);
            }

            if (replacementReturned)
            {
                if (TryProjectExtensionResult(in current, in replacementResult, out ProjectileSimulationStepContext projected))
                {
                    current = projected;
                    changed = true;
                }
                else
                {
                    ReportInvalidResult(replacement.Id, GameplayBehaviorStage.Replacement);
                    if (TryRunVanilla(in current, out projected, out ProjectileSimulationStepResult vanillaResult, out bool vanillaInvalid))
                    {
                        current = projected;
                        changed = true;
                    }
                    else if (vanillaInvalid)
                    {
                        next = vanillaResult;
                        return true;
                    }
                }
            }
            else if (replacementFaulted)
            {
                if (TryRunVanilla(in current, out ProjectileSimulationStepContext projected, out ProjectileSimulationStepResult vanillaResult, out bool vanillaInvalid))
                {
                    current = projected;
                    changed = true;
                }
                else if (vanillaInvalid)
                {
                    next = vanillaResult;
                    return true;
                }
            }
        }
        else
        {
            if (TryRunVanilla(in current, out ProjectileSimulationStepContext projected, out ProjectileSimulationStepResult vanillaResult, out bool vanillaInvalid))
            {
                current = projected;
                changed = true;
            }
            else if (vanillaInvalid)
            {
                next = vanillaResult;
                return true;
            }
        }

        ReadOnlySpan<GameplayBehaviorBinding<IProjectileStateStepper>> post = plan is null
            ? ReadOnlySpan<GameplayBehaviorBinding<IProjectileStateStepper>>.Empty
            : plan.Post.Span;
        for (int index = 0; index < post.Length; index++)
        {
            GameplayBehaviorBinding<IProjectileStateStepper> binding = post[index];
            if (!TryRunExtension(in binding, GameplayBehaviorStage.Post, in current, out ProjectileSimulationStepResult update))
                continue;

            if (TryProjectExtensionResult(in current, in update, out ProjectileSimulationStepContext projected))
            {
                current = projected;
                changed = true;
            }
            else
            {
                ReportInvalidResult(binding.Id, GameplayBehaviorStage.Post);
            }
        }

        if (!changed)
        {
            next = default;
            return false;
        }

        next = ToResult(in current);
        return true;
    }

    private bool TryResolveArchetypeReplacement(
        in ProjectileSimulationStepContext projectile,
        out GameplayBehaviorBinding<IProjectileStateStepper> replacement)
    {
        if (archetypeBehaviors is not null &&
            archetypes is not null &&
            identities is not null &&
            identities.TryGet(projectile.Projectile.Handle, out GameplayArchetypeId archetypeId) &&
            archetypes.Snapshot.TryGet(archetypeId, out ProjectileArchetypeDescriptor descriptor) &&
            descriptor.BehaviorId.IsAssigned &&
            archetypeBehaviors.TryGetPublished(descriptor.BehaviorId, out replacement))
        {
            return true;
        }

        replacement = default;
        return false;
    }

    private bool TryRunExtension(
        in GameplayBehaviorBinding<IProjectileStateStepper> binding,
        GameplayBehaviorStage stage,
        in ProjectileSimulationStepContext current,
        out ProjectileSimulationStepResult update)
    {
        try
        {
            return binding.Behavior.TryStepState(in current, out update);
        }
        catch (Exception exception)
        {
            ReportFault(binding.Id, stage, exception);
            update = default;
            return false;
        }
    }

    private bool TryRunVanilla(
        in ProjectileSimulationStepContext current,
        out ProjectileSimulationStepContext projected,
        out ProjectileSimulationStepResult vanillaResult,
        out bool vanillaInvalid)
    {
        if (!vanilla.TryStepState(in current, out vanillaResult))
        {
            projected = default;
            vanillaInvalid = false;
            return false;
        }

        if (TryProjectResult(in current, in vanillaResult, out projected))
        {
            vanillaInvalid = false;
            return true;
        }

        projected = default;
        vanillaInvalid = true;
        return false;
    }

    private bool TryProjectExtensionResult(
        in ProjectileSimulationStepContext current,
        in ProjectileSimulationStepResult update,
        out ProjectileSimulationStepContext projected)
    {
        ProjectileStateUpdate state = update.State;
        if (state.Spawner != current.Projectile.Spawner ||
            !RuntimeProjectileStore.IsValidState(in state))
        {
            projected = default;
            return false;
        }

        return TryProjectResult(in current, in update, out projected);
    }

    private static bool TryProjectResult(
        in ProjectileSimulationStepContext current,
        in ProjectileSimulationStepResult update,
        out ProjectileSimulationStepContext projected)
    {
        if (!RuntimeProjectileStateExecutor.TryNormalizeTermination(in update, out ProjectileSimulationStepResult normalized))
        {
            projected = default;
            return false;
        }

        ProjectileStateUpdate state = normalized.State;
        if (!RuntimeProjectileStore.IsValidState(in state) ||
            !TryProjectLifecycle(
                current.Projectile.Type,
                state.Type,
                current.Lifecycle,
                normalized.TimeLeft,
                normalized.Liquid,
                out ProjectileLifecycleState lifecycle))
        {
            projected = default;
            return false;
        }

        var snapshot = new ProjectileSnapshot(
            current.Projectile.Handle,
            current.Projectile.Revision,
            state.Type,
            state.Spawner,
            state.PositionX,
            state.PositionY,
            state.VelocityX,
            state.VelocityY,
            state.Ai,
            state.BannerIdToRespondTo,
            state.Damage,
            state.KnockBack,
            state.OriginalDamage);

        projected = new ProjectileSimulationStepContext(
            snapshot,
            lifecycle,
            current.SubupdateIndex,
            current.SubupdatesPerWorldTick,
            normalized.TerminationReason);
        return true;
    }

    private void ReportInvalidResult(GameplayExtensionId id, GameplayBehaviorStage stage) =>
        ReportFault(id, stage, new InvalidOperationException("Gameplay extension proposed an invalid projectile state transition."));

    private void ReportFault(GameplayExtensionId id, GameplayBehaviorStage stage, Exception exception)
    {
        if (faultSink is null)
            return;

        try
        {
            faultSink.BehaviorFaulted(id, stage, exception);
        }
        catch
        {
            // Diagnostics are observational and must not amplify a contained extension fault.
        }
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
            if (!VanillaProjectileLifecycleFacts.TryGetDefaults(nextType, out VanillaProjectileLifecycleDefaults defaults))
            {
                next = default;
                return false;
            }

            netImportant = defaults.NetImportant;
        }

        next = new ProjectileLifecycleState(timeLeft, netImportant, liquid ?? previous.Liquid);
        return true;
    }

    private static ProjectileSimulationStepResult ToResult(in ProjectileSimulationStepContext current) =>
        new(
            new ProjectileStateUpdate(
                current.Projectile.Type,
                current.Projectile.Spawner,
                current.Projectile.PositionX,
                current.Projectile.PositionY,
                current.Projectile.VelocityX,
                current.Projectile.VelocityY,
                current.Projectile.Ai,
                current.Projectile.BannerIdToRespondTo,
                current.Projectile.Damage,
                current.Projectile.KnockBack,
                current.Projectile.OriginalDamage),
            current.Lifecycle.TimeLeft,
            current.Lifecycle.Liquid,
            current.TerminationReason);
}
