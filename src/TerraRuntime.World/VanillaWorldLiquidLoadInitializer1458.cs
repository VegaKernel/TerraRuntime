namespace TerraRuntime.World;

/// <summary>
/// Result of the canonical TerrariaServer 1.4.5.8 post-load liquid preparation sequence.
/// </summary>
public enum VanillaWorldLiquidLoadPreparationResult1458 : byte
{
    Prepared = 0,
    AlreadyPrepared = 1,
    UnsupportedRemixLiquidMapping = 2,
    UnsupportedLiquidDeathTile = 3
}

/// <summary>
/// Source-backed post-load preparation diagnostic. Unsupported object death is fail-closed while the candidate
/// world is still unpublished; the caller may report the exact tile coordinate/type and refuse startup.
/// </summary>
public readonly record struct VanillaWorldLiquidLoadPreparationDiagnostic1458(
    VanillaWorldLiquidLoadPreparationResult1458 Result,
    int SettleIterations,
    bool ReachedVanillaIterationLimit,
    int X,
    int Y,
    TerraRuntime.Contracts.Gameplay.TileTypeId TileType)
{
    public bool IsPrepared => Result is
        VanillaWorldLiquidLoadPreparationResult1458.Prepared or
        VanillaWorldLiquidLoadPreparationResult1458.AlreadyPrepared;
}

/// <summary>
/// Replays the liquid preparation performed by TerrariaServer 1.4.5.8 <c>WorldFile.LoadWorld</c> after the
/// canonical .wld has been decoded and before the world becomes authoritative:
/// <c>QuickWater(2) -&gt; WaterCheck -&gt; quickSettle UpdateLiquid until empty/100000 -&gt; WaterCheck</c>.
/// In vanilla the argument 2 passed to QuickWater is verbosity, not a scan bound, so TerraRuntime always invokes
/// the complete load scan here. Remix/Zenith conversion is rejected until the generation-only lava-line/ocean
/// depth inputs consumed by <c>SettleWaterAt</c> are represented in the load context.
/// </summary>
public static class VanillaWorldLiquidLoadInitializer1458
{
    public const int MaximumSettleIterations1458 = 100000;

    public static VanillaWorldLiquidLoadPreparationDiagnostic1458 TryPrepare(WorldFileData world)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (world.Tiles.IsPostLoadLiquidPrepared)
        {
            return new VanillaWorldLiquidLoadPreparationDiagnostic1458(
                VanillaWorldLiquidLoadPreparationResult1458.AlreadyPrepared,
                0,
                false,
                0,
                0,
                default);
        }

        if (world.RuntimeMetadata.RemixWorld || world.RuntimeMetadata.ZenithWorld)
        {
            return new VanillaWorldLiquidLoadPreparationDiagnostic1458(
                VanillaWorldLiquidLoadPreparationResult1458.UnsupportedRemixLiquidMapping,
                0,
                false,
                0,
                0,
                default);
        }

        var simulator = new VanillaWorldLiquidSimulator1458(
            world.Tiles,
            VanillaWorldLiquidSimulator1458.LoadingWorkBudgetPerUpdate1458,
            VanillaWorldLiquidSimulator1458.DefaultDiscoveryBudgetPerTick);

        // WorldFile.LoadWorld sets GenVars.waterLine=maxTilesY and then calls Liquid.QuickWater(2).
        // The "2" is only console verbosity; for a normal non-remix loaded world it does not alter liquid type.
        simulator.QuickWater();

        VanillaWaterCheckDiagnostic1458 firstWaterCheck = simulator.WaterCheckLoading();
        if (!firstWaterCheck.IsApplied)
            return UnsupportedDeath(in firstWaterCheck, 0);

        var changes = new WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.LoadingWorkBudgetPerUpdate1458 *
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int iterations = 0;
        while (world.Tiles.LiquidUpdates.ActiveCount > 0 && iterations < MaximumSettleIterations1458)
        {
            iterations++;
            _ = simulator.TickQuickSettle(changes);
        }

        bool reachedLimit = world.Tiles.LiquidUpdates.ActiveCount > 0 &&
                            iterations >= MaximumSettleIterations1458;

        // Vanilla continues after the 100000 guard rather than failing the load, then performs a final WaterCheck.
        VanillaWaterCheckDiagnostic1458 secondWaterCheck = simulator.WaterCheckLoading();
        if (!secondWaterCheck.IsApplied)
            return UnsupportedDeath(in secondWaterCheck, iterations);

        world.Tiles.MarkPostLoadLiquidPrepared();
        return new VanillaWorldLiquidLoadPreparationDiagnostic1458(
            VanillaWorldLiquidLoadPreparationResult1458.Prepared,
            iterations,
            reachedLimit,
            0,
            0,
            default);
    }

    private static VanillaWorldLiquidLoadPreparationDiagnostic1458 UnsupportedDeath(
        in VanillaWaterCheckDiagnostic1458 diagnostic,
        int iterations) =>
        new(
            VanillaWorldLiquidLoadPreparationResult1458.UnsupportedLiquidDeathTile,
            iterations,
            false,
            diagnostic.X,
            diagnostic.Y,
            diagnostic.TileType);
}
