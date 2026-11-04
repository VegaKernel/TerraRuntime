namespace TerraRuntime.WorldGeneration.Runtime;

public sealed partial class Workspace
{
    // Drunk Shinies rewrites these GenVars choices per vein. SavedOreTiers and
    // bar choices remain the immutable Reset facts retained in VanillaBootstrapState.
    private VanillaGenerationOreSelections1458? vanillaGenerationOres;

    internal VanillaGenerationOreSelections1458 VanillaGenerationOres => vanillaGenerationOres ??
        (VanillaBootstrapState is { } bootstrap ? new(bootstrap.CopperOre, bootstrap.IronOre, bootstrap.SilverOre, bootstrap.GoldOre) :
        throw new InvalidOperationException("Generation ore choices require Reset state."));

    internal void ResetVanillaGenerationOres() => vanillaGenerationOres = null;
    internal void SetVanillaGenerationOres(VanillaGenerationOreSelections1458 value) => vanillaGenerationOres = value;
}

internal readonly record struct VanillaGenerationOreSelections1458(int Copper, int Iron, int Silver, int Gold);
