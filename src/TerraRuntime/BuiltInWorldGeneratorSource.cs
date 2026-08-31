using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Runtime-owned generators are registered explicitly rather than discovered. Flat remains the minimal deterministic
/// baseline; vanilla is the Terraria 1.4.5.8-compatible built-in profile whose passes advance independently toward
/// source-backed parity; optimized is the progression-validated production-oriented custom profile; skyblock is a
/// deterministic runtime-owned void/island profile.
/// </summary>
internal sealed class BuiltInWorldGeneratorSource : ITerraRuntimeWorldGeneratorSource
{
    public static BuiltInWorldGeneratorSource Instance { get; } = new();

    private readonly FlatWorldGenerationProvider flat = new();
    private readonly SourceBackedVanillaWorldGenerationCanonical1458 vanilla = new();
    private readonly OptimizedPlayableWorldGenerationProvider optimized = new();
    private readonly SkyblockWorldGenerationProvider skyblock = new();
    private readonly WorldGeneratorId[] ids =
        [
            FlatWorldGenerationProvider.GeneratorId,
            VanillaWorldGenerationProvider1458.GeneratorId,
            OptimizedPlayableWorldGenerationProvider.GeneratorId,
            SkyblockWorldGenerationProvider.GeneratorId
        ];

    private BuiltInWorldGeneratorSource()
    {
    }

    public ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds() => ids;

    public bool TryResolveWorldGenerator(WorldGeneratorId id, out IWorldGenerationProvider? provider)
    {
        if (id == flat.Id)
        {
            provider = flat;
            return true;
        }

        if (id == vanilla.Id)
        {
            provider = vanilla;
            return true;
        }

        if (id == optimized.Id)
        {
            provider = optimized;
            return true;
        }

        if (id == skyblock.Id)
        {
            provider = skyblock;
            return true;
        }

        provider = null;
        return false;
    }
}
