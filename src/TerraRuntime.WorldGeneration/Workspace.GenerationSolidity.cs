using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Runtime;

public sealed partial class Workspace
{
    // Main.tileSolid is mutated by source generation passes and liquid helpers. Retain only
    // those writes beside the candidate, leaving the immutable live collision catalog intact.
    private readonly Dictionary<int, bool> vanillaGenerationSolidity = [];
    internal bool HasVanillaGenerationSolidity { get; private set; }

    internal void InitializeVanillaGenerationSolidity()
    {
        vanillaGenerationSolidity.Clear();
        HasVanillaGenerationSolidity = true;
        // WorldGen.Reset, TerrariaServer 1.4.5.8.
        SetVanillaGenerationSolid(VanillaTileIds.ShimmerBlock, false);
    }

    internal bool IsVanillaGenerationSolid(TileTypeId type) =>
        vanillaGenerationSolidity.TryGetValue(type.Value, out bool solid)
            ? solid : VanillaTileCollisionCatalog.IsSolid(type);

    internal void SetVanillaGenerationSolid(TileTypeId type, bool solid)
    {
        if (!HasVanillaGenerationSolidity)
            throw new InvalidOperationException("Generation solidity must be initialized by the candidate pipeline.");
        vanillaGenerationSolidity[type.Value] = solid;
    }
}
