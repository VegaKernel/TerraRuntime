using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.Tests;

public sealed class StartupWorldGeneratorSourceTests
{
    [Fact]
    public void Source_exposes_builtin_and_host_generators_in_stable_order()
    {
        var hostProvider = new StubProvider(new WorldGeneratorId("fixture:custom"));
        var source = new StartupWorldGeneratorSource(new StubSource(hostProvider));

        WorldGeneratorId[] ids = source.CaptureWorldGeneratorIds().ToArray();

        Assert.Equal([new WorldGeneratorId("fixture:custom"), new WorldGeneratorId("terraruntime:flat")], ids);
        Assert.True(source.TryResolveWorldGenerator(new WorldGeneratorId("fixture:custom"), out IWorldGenerationProvider? custom));
        Assert.Same(hostProvider, custom);
        Assert.True(source.TryResolveWorldGenerator(new WorldGeneratorId("terraruntime:flat"), out IWorldGenerationProvider? builtIn));
        Assert.NotNull(builtIn);
    }

    [Fact]
    public void Source_does_not_allow_host_to_shadow_builtin_id()
    {
        var shadow = new StubProvider(new WorldGeneratorId("terraruntime:flat"));
        var source = new StartupWorldGeneratorSource(new StubSource(shadow));

        Assert.True(source.TryResolveWorldGenerator(new WorldGeneratorId("terraruntime:flat"), out IWorldGenerationProvider? resolved));
        Assert.NotNull(resolved);
        Assert.NotSame(shadow, resolved);
        Assert.Single(source.CaptureWorldGeneratorIds().ToArray());
    }

    private sealed class StubSource(IWorldGenerationProvider provider) : ITerraRuntimeWorldGeneratorSource
    {
        public ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds() => new[] { provider.Id };

        public bool TryResolveWorldGenerator(WorldGeneratorId id, out IWorldGenerationProvider? resolved)
        {
            resolved = id == provider.Id ? provider : null;
            return resolved is not null;
        }
    }

    private sealed class StubProvider(WorldGeneratorId id) : IWorldGenerationProvider
    {
        public WorldGeneratorId Id { get; } = id;

        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            throw new NotSupportedException();
    }
}
