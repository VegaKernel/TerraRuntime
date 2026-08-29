using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeGameplayBehaviorRegistryTests
{
    [Fact]
    public void Registration_is_invisible_until_safe_boundary_commit()
    {
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, ProbeBehavior>();
        GameplayBehaviorRegistrationResult result = registry.TryRegister(
            new GameplayExtensionId("test:npc.pre"),
            new NpcTypeId(1),
            GameplayBehaviorStage.Pre,
            order: 0,
            new ProbeBehavior("pre"),
            out IGameplayBehaviorRegistrationLease? lease);

        Assert.Equal(GameplayBehaviorRegistrationResult.Registered, result);
        Assert.NotNull(lease);
        Assert.True(registry.HasPendingChanges);
        Assert.False(registry.Snapshot.TryGetPlan(new NpcTypeId(1), out _));

        RuntimeGameplayBehaviorSnapshot<NpcTypeId, ProbeBehavior> published = registry.CommitPending();

        Assert.Equal((ulong)1, published.Revision);
        Assert.Equal(1, published.TargetCount);
        Assert.Equal(1, published.RegistrationCount);
        Assert.True(published.TryGetPlan(new NpcTypeId(1), out GameplayBehaviorDispatchPlan<ProbeBehavior>? plan));
        Assert.NotNull(plan);
        Assert.Equal("pre", Assert.Single(plan.Pre.ToArray()).Behavior.Name);
        Assert.False(registry.HasPendingChanges);
    }

    [Fact]
    public void Decorators_are_ordered_by_explicit_order_then_stable_id()
    {
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, ProbeBehavior>();
        NpcTypeId target = new(7);

        Register(registry, "test:z", target, GameplayBehaviorStage.Pre, 5, "z");
        Register(registry, "test:b", target, GameplayBehaviorStage.Pre, 1, "b");
        Register(registry, "test:a", target, GameplayBehaviorStage.Pre, 1, "a");
        Register(registry, "test:post-b", target, GameplayBehaviorStage.Post, 2, "post-b");
        Register(registry, "test:post-a", target, GameplayBehaviorStage.Post, 2, "post-a");

        RuntimeGameplayBehaviorSnapshot<NpcTypeId, ProbeBehavior> published = registry.CommitPending();
        Assert.True(published.TryGetPlan(target, out GameplayBehaviorDispatchPlan<ProbeBehavior>? plan));
        Assert.NotNull(plan);

        Assert.Equal(
            ["a", "b", "z"],
            plan.Pre.ToArray().Select(binding => binding.Behavior.Name).ToArray());
        Assert.Equal(
            ["post-a", "post-b"],
            plan.Post.ToArray().Select(binding => binding.Behavior.Name).ToArray());
    }

    [Fact]
    public void Only_one_exclusive_replacement_is_allowed_per_target()
    {
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, ProbeBehavior>();
        ProjectileTypeId target = new(3);
        Register(registry, "test:first", target, GameplayBehaviorStage.Replacement, 0, "first");

        GameplayBehaviorRegistrationResult second = registry.TryRegister(
            new GameplayExtensionId("test:second"),
            target,
            GameplayBehaviorStage.Replacement,
            order: 0,
            new ProbeBehavior("second"),
            out IGameplayBehaviorRegistrationLease? lease);

        Assert.Equal(GameplayBehaviorRegistrationResult.ReplacementConflict, second);
        Assert.Null(lease);

        RuntimeGameplayBehaviorSnapshot<ProjectileTypeId, ProbeBehavior> published = registry.CommitPending();
        Assert.True(published.TryGetPlan(target, out GameplayBehaviorDispatchPlan<ProbeBehavior>? plan));
        Assert.NotNull(plan);
        Assert.True(plan.HasReplacement);
        Assert.Equal("first", plan.Replacement.Behavior.Name);
    }

    [Fact]
    public void Duplicate_registration_id_is_rejected_even_for_another_target()
    {
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, ProbeBehavior>();
        GameplayExtensionId id = new("test:shared-id");
        Register(registry, id.Value, new NpcTypeId(1), GameplayBehaviorStage.Pre, 0, "one");

        GameplayBehaviorRegistrationResult duplicate = registry.TryRegister(
            id,
            new NpcTypeId(2),
            GameplayBehaviorStage.Post,
            order: 0,
            new ProbeBehavior("two"),
            out IGameplayBehaviorRegistrationLease? lease);

        Assert.Equal(GameplayBehaviorRegistrationResult.DuplicateId, duplicate);
        Assert.Null(lease);
    }

    [Fact]
    public void Lease_retirement_is_published_only_at_safe_boundary()
    {
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, ProbeBehavior>();
        GameplayExtensionId id = new("test:retire");
        NpcTypeId target = new(4);
        IGameplayBehaviorRegistrationLease lease = Register(
            registry,
            id.Value,
            target,
            GameplayBehaviorStage.Pre,
            0,
            "behavior");
        registry.CommitPending();

        lease.Dispose();

        Assert.True(lease.IsRetirementPending);
        Assert.False(lease.IsRetired);
        Assert.True(registry.Snapshot.TryGetPlan(target, out _));

        GameplayBehaviorRegistrationResult earlyReuse = registry.TryRegister(
            id,
            target,
            GameplayBehaviorStage.Pre,
            0,
            new ProbeBehavior("replacement"),
            out _);
        Assert.Equal(GameplayBehaviorRegistrationResult.DuplicateId, earlyReuse);

        RuntimeGameplayBehaviorSnapshot<NpcTypeId, ProbeBehavior> published = registry.CommitPending();

        Assert.True(lease.IsRetired);
        Assert.False(lease.IsRetirementPending);
        Assert.False(published.TryGetPlan(target, out _));
        Assert.Equal(0, published.RegistrationCount);

        GameplayBehaviorRegistrationResult reuse = registry.TryRegister(
            id,
            target,
            GameplayBehaviorStage.Pre,
            0,
            new ProbeBehavior("replacement"),
            out IGameplayBehaviorRegistrationLease? replacementLease);
        Assert.Equal(GameplayBehaviorRegistrationResult.Registered, reuse);
        Assert.NotNull(replacementLease);
    }

    [Fact]
    public void Disposing_unpublished_registration_never_leaks_into_snapshot()
    {
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, ProbeBehavior>();
        IGameplayBehaviorRegistrationLease lease = Register(
            registry,
            "test:cancel-before-publish",
            new NpcTypeId(5),
            GameplayBehaviorStage.Post,
            0,
            "post");

        lease.Dispose();
        RuntimeGameplayBehaviorSnapshot<NpcTypeId, ProbeBehavior> published = registry.CommitPending();

        Assert.True(lease.IsRetired);
        Assert.Equal(0, published.RegistrationCount);
        Assert.Equal(0, published.TargetCount);
    }

    [Fact]
    public void Commit_without_changes_keeps_same_snapshot_and_revision()
    {
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, ProbeBehavior>();

        RuntimeGameplayBehaviorSnapshot<NpcTypeId, ProbeBehavior> first = registry.CommitPending();
        RuntimeGameplayBehaviorSnapshot<NpcTypeId, ProbeBehavior> second = registry.CommitPending();

        Assert.Same(first, second);
        Assert.Equal((ulong)0, second.Revision);
    }

    private static IGameplayBehaviorRegistrationLease Register<TTarget>(
        RuntimeGameplayBehaviorRegistry<TTarget, ProbeBehavior> registry,
        string id,
        TTarget target,
        GameplayBehaviorStage stage,
        int order,
        string name)
        where TTarget : notnull
    {
        GameplayBehaviorRegistrationResult result = registry.TryRegister(
            new GameplayExtensionId(id),
            target,
            stage,
            order,
            new ProbeBehavior(name),
            out IGameplayBehaviorRegistrationLease? lease);

        Assert.Equal(GameplayBehaviorRegistrationResult.Registered, result);
        return Assert.IsAssignableFrom<IGameplayBehaviorRegistrationLease>(lease);
    }

    private sealed record ProbeBehavior(string Name);
}
