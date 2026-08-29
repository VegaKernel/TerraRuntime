using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class StartupWorldGeneratorSourceTests
{
    [Fact]
    public void Source_exposes_builtin_and_host_generators_in_stable_order()
    {
        var hostProvider = new StubProvider(new WorldGeneratorId("fixture:custom"));
        var source = new StartupWorldGeneratorSource(new StubSource(hostProvider));

        WorldGeneratorId[] ids = source.CaptureWorldGeneratorIds().ToArray();

        Assert.Equal(
            [
                new WorldGeneratorId("fixture:custom"),
                FlatWorldGenerationProvider.GeneratorId,
                VanillaWorldGenerationProvider1458.GeneratorId
            ],
            ids);
        Assert.True(source.TryResolveWorldGenerator(new WorldGeneratorId("fixture:custom"), out IWorldGenerationProvider? custom));
        Assert.Same(hostProvider, custom);
        Assert.True(source.TryResolveWorldGenerator(FlatWorldGenerationProvider.GeneratorId, out IWorldGenerationProvider? flat));
        Assert.NotNull(flat);
        Assert.True(source.TryResolveWorldGenerator(VanillaWorldGenerationProvider1458.GeneratorId, out IWorldGenerationProvider? vanilla));
        Assert.NotNull(vanilla);
    }

    [Theory]
    [InlineData("terraruntime:flat")]
    [InlineData("terraruntime:vanilla")]
    public void Source_does_not_allow_host_to_shadow_builtin_id(string builtInId)
    {
        var id = new WorldGeneratorId(builtInId);
        var shadow = new StubProvider(id);
        var source = new StartupWorldGeneratorSource(new StubSource(shadow));

        Assert.True(source.TryResolveWorldGenerator(id, out IWorldGenerationProvider? resolved));
        Assert.NotNull(resolved);
        Assert.NotSame(shadow, resolved);
        Assert.Equal(2, source.CaptureWorldGeneratorIds().Length);
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
