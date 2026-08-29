using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldGenerationPlannerTests
{
    [Fact]
    public void Plan_order_is_deterministic_and_independent_of_registration_order()
    {
        string[] first = BuildPlan(registerReverse: false);
        string[] second = BuildPlan(registerReverse: true);

        Assert.Equal(new[] { "test:terrain", "test:ores", "test:decorate" }, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Missing_required_dependency_rejects_commit_and_preserves_last_published_plan()
    {
        var registry = new RuntimeWorldGenerationPassRegistry<object>();
        var baseDescriptor = new WorldGenerationPassDescriptor(new WorldGenerationPassId("test:base"));
        Assert.Equal(WorldGenerationPassRegistrationResult.Registered, registry.TryRegister(baseDescriptor, new object(), out _));
        Assert.Equal(WorldGenerationPlanCommitStatus.Published, registry.CommitPending().Status);
        Assert.Equal(1, registry.Plan.Count);

        var broken = new WorldGenerationPassDescriptor(
            new WorldGenerationPassId("test:broken"),
            requiredAfter: [new WorldGenerationPassId("test:missing")]);
        Assert.Equal(WorldGenerationPassRegistrationResult.Registered, registry.TryRegister(broken, new object(), out _));

        WorldGenerationPlanCommitResult result = registry.CommitPending();

        Assert.Equal(WorldGenerationPlanCommitStatus.MissingRequiredDependency, result.Status);
        Assert.Equal(new WorldGenerationPassId("test:broken"), result.PassId);
        Assert.Equal(new WorldGenerationPassId("test:missing"), result.DependencyId);
        Assert.Equal(1, registry.Plan.Count);
        Assert.Equal(new WorldGenerationPassId("test:base"), registry.Plan.Entries.Span[0].Descriptor.Id);
        Assert.True(registry.HasPendingChanges);
    }

    [Fact]
    public void Optional_missing_hint_is_ignored_but_cycle_is_rejected()
    {
        var registry = new RuntimeWorldGenerationPassRegistry<object>();
        var a = new WorldGenerationPassDescriptor(
            new WorldGenerationPassId("test:a"),
            optionalAfter: [new WorldGenerationPassId("test:not-installed")],
            optionalBefore: [new WorldGenerationPassId("test:b")]);
        var b = new WorldGenerationPassDescriptor(
            new WorldGenerationPassId("test:b"),
            optionalBefore: [new WorldGenerationPassId("test:a")]);
        Assert.Equal(WorldGenerationPassRegistrationResult.Registered, registry.TryRegister(a, new object(), out _));
        Assert.Equal(WorldGenerationPassRegistrationResult.Registered, registry.TryRegister(b, new object(), out _));

        WorldGenerationPlanCommitResult result = registry.CommitPending();

        Assert.Equal(WorldGenerationPlanCommitStatus.DependencyCycle, result.Status);
        Assert.Equal(0, registry.Plan.Count);
    }

    [Fact]
    public void Removing_required_pass_is_transactional_until_dependent_is_removed_too()
    {
        var registry = new RuntimeWorldGenerationPassRegistry<object>();
        WorldGenerationPassId terrainId = new("test:terrain");
        WorldGenerationPassId oresId = new("test:ores");
        Assert.Equal(WorldGenerationPassRegistrationResult.Registered,
            registry.TryRegister(new WorldGenerationPassDescriptor(terrainId), new object(), out IWorldGenerationPassRegistrationLease? terrain));
        Assert.Equal(WorldGenerationPassRegistrationResult.Registered,
            registry.TryRegister(new WorldGenerationPassDescriptor(oresId, requiredAfter: [terrainId]), new object(), out IWorldGenerationPassRegistrationLease? ores));
        Assert.NotNull(terrain);
        Assert.NotNull(ores);
        Assert.Equal(WorldGenerationPlanCommitStatus.Published, registry.CommitPending().Status);

        terrain.Dispose();
        Assert.Equal(WorldGenerationPlanCommitStatus.MissingRequiredDependency, registry.CommitPending().Status);
        Assert.Equal(2, registry.Plan.Count);
        Assert.True(terrain.IsRetirementPending);

        ores.Dispose();
        Assert.Equal(WorldGenerationPlanCommitStatus.Published, registry.CommitPending().Status);
        Assert.Equal(0, registry.Plan.Count);
        Assert.True(terrain.IsRetired);
        Assert.True(ores.IsRetired);
    }

    [Fact]
    public void Isolated_pass_rng_is_stable_and_scoped_by_pass_id()
    {
        WorldGenerationPassRandom a1 = WorldGenerationPassRandom.Create(123UL, new WorldGenerationPassId("test:a"));
        WorldGenerationPassRandom a2 = WorldGenerationPassRandom.Create(123UL, new WorldGenerationPassId("test:a"));
        WorldGenerationPassRandom b = WorldGenerationPassRandom.Create(123UL, new WorldGenerationPassId("test:b"));

        ulong first = a1.NextUInt64();
        Assert.Equal(first, a2.NextUInt64());
        Assert.NotEqual(first, b.NextUInt64());
    }

    private static string[] BuildPlan(bool registerReverse)
    {
        var registry = new RuntimeWorldGenerationPassRegistry<object>();
        WorldGenerationPassId terrain = new("test:terrain");
        WorldGenerationPassId ores = new("test:ores");
        WorldGenerationPassId decorate = new("test:decorate");
        var descriptors = new[]
        {
            new WorldGenerationPassDescriptor(decorate, requiredAfter: [ores]),
            new WorldGenerationPassDescriptor(terrain),
            new WorldGenerationPassDescriptor(ores, requiredAfter: [terrain])
        };

        IEnumerable<WorldGenerationPassDescriptor> sequence = registerReverse ? descriptors.Reverse() : descriptors;
        foreach (WorldGenerationPassDescriptor descriptor in sequence)
            Assert.Equal(WorldGenerationPassRegistrationResult.Registered, registry.TryRegister(descriptor, new object(), out _));

        Assert.Equal(WorldGenerationPlanCommitStatus.Published, registry.CommitPending().Status);
        return registry.Plan.Entries.Span.ToArray().Select(static entry => entry.Descriptor.Id.Value).ToArray();
    }
}
