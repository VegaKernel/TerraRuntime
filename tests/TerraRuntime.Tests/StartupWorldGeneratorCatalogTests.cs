using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.Tests;

public sealed class StartupWorldGeneratorCatalogTests
{
    [Fact]
    public void Captures_registered_generator_ids_in_deterministic_order()
    {
        var source = new FakeSource(
            new WorldGeneratorId("zeta:world"),
            new WorldGeneratorId("alpha:world"),
            new WorldGeneratorId("middle:world"));

        WorldGeneratorId[] ids = StartupWorldGeneratorCatalog.Capture(source);

        Assert.Equal(
            new[] { "alpha:world", "middle:world", "zeta:world" },
            ids.Select(static id => id.Value));
    }

    [Fact]
    public void Missing_host_source_exposes_no_custom_generators()
    {
        Assert.Empty(StartupWorldGeneratorCatalog.Capture(source: null));
    }

    private sealed class FakeSource : ITerraRuntimeWorldGeneratorSource
    {
        private readonly WorldGeneratorId[] ids;

        public FakeSource(params WorldGeneratorId[] ids) => this.ids = ids;

        public ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds() => ids;

        public bool TryResolveWorldGenerator(
            WorldGeneratorId id,
            out IWorldGenerationProvider? provider)
        {
            provider = null;
            return false;
        }
    }
}
