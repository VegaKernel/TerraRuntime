using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // WorldGen.FinalCleanup invokes the same WorldGen.NotTheBees helper previously reached
    // from MudCavesToGrass. Retain one implementation and supply the current workspace state.
    // The immediately following FinishNotTheBees helper remains a separate, unqualified tail.
    internal static void ApplyFinalNotTheBeesFirstHelper(
        Workspace workspace,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        VanillaWorldSeedProfile1458 profile = workspace.VanillaSeedProfile;
        if (!profile.Has(VanillaSpecialWorldSeed1458.NotTheBees) ||
            profile.Has(VanillaSpecialWorldSeed1458.Skyblock))
            return;
        TerrainGenerationState1458 terrain = workspace.VanillaTerrainState ??
            throw new InvalidOperationException("Final bee cleanup requires source-backed terrain state.");
        VanillaLiquidLines1458 liquids = workspace.VanillaLiquidLines ??
            throw new InvalidOperationException("Final bee cleanup requires source liquid lines.");
        VanillaWorldGenerationBootstrapState1458 bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Final bee cleanup requires Reset bootstrap state.");
        NotTheBeesEarly1458.Apply(workspace, random, profile, terrain.WorldSurface, terrain.RockLayer,
            liquids.LavaLine, bootstrap.DungeonSide, cancellationToken);
    }
}
