using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core.Extensions;

/// <summary>
/// Safe-boundary registry for behaviors addressed directly by stable extension ID rather than by vanilla content
/// type. This is the archetype-specific replacement lane: multiple server archetypes may share one vanilla
/// presentation while resolving to different behaviors without competing for the type-level replacement slot.
/// </summary>
public sealed class RuntimeArchetypeBehaviorRegistry<TBehavior>
    where TBehavior : class
{
    private readonly RuntimeGameplayBehaviorRegistry<GameplayExtensionId, TBehavior> inner = new();

    public bool HasPendingChanges => inner.HasPendingChanges;

    public GameplayBehaviorRegistrationResult TryRegister(
        GameplayExtensionId id,
        TBehavior behavior,
        out IGameplayBehaviorRegistrationLease? lease) =>
        inner.TryRegister(
            id,
            id,
            GameplayBehaviorStage.Replacement,
            order: 0,
            behavior,
            out lease);

    public void CommitPending() => inner.CommitPending();

    public bool TryGetPublished(
        GameplayExtensionId id,
        out GameplayBehaviorBinding<TBehavior> binding)
    {
        if (id.IsAssigned &&
            inner.Snapshot.TryGetPlan(id, out GameplayBehaviorDispatchPlan<TBehavior>? plan) &&
            plan is not null &&
            plan.HasReplacement)
        {
            binding = plan.Replacement;
            return true;
        }

        binding = default;
        return false;
    }
}
