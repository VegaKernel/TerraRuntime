namespace TerraRuntime.World;

/// <summary>
/// Source-backed Terraria 1.4.5.8 runtime state for the Skyblock low-tile rules. Since 1.4.5.4 the low-tile
/// behavior is gated by the persisted vanilla Skyblock world flag; sparse ordinary worlds must not inherit it.
/// </summary>
public readonly record struct VanillaSkyblockRuntimeState1458(
    bool SkyblockWorld,
    bool LowTiles,
    bool NoHellstone,
    bool NoFossils,
    int ActiveTileCount,
    int TotalTileCount)
{
    public int SnowTileThreshold => LowTiles
        ? VanillaSkyblockRuntimePolicy1458.LowTileBiomeThreshold
        : VanillaSkyblockRuntimePolicy1458.DefaultBiomeThreshold;

    public int DesertTileThreshold => LowTiles
        ? VanillaSkyblockRuntimePolicy1458.LowTileBiomeThreshold
        : VanillaSkyblockRuntimePolicy1458.DefaultBiomeThreshold;

    public bool SkipHardmodeConversion => LowTiles;
}

/// <summary>
/// Evaluates the vanilla Skyblock rules from persisted world semantics plus the current tile population. It does not
/// know or care which generator created the world. The 10% check is strict: exactly 10% filled is not lowTiles.
/// </summary>
public static class VanillaSkyblockRuntimePolicy1458
{
    public const int DefaultBiomeThreshold = 1500;
    public const int LowTileBiomeThreshold = 300;
    public const int LowTilePercent = 10;

    public static VanillaSkyblockRuntimeState1458 Evaluate(WorldFileData world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Evaluate(world.RuntimeMetadata, world.Tiles);
    }

    public static VanillaSkyblockRuntimeState1458 Evaluate(
        WorldFileRuntimeMetadata metadata,
        WorldTileStore tiles)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(tiles);

        int activeTileCount = 0;
        bool noHellstone = metadata.SkyblockWorld;
        bool noFossils = metadata.SkyblockWorld;
        foreach (ref readonly WorldTile tile in tiles.Tiles)
        {
            if (tile.IsActive)
                activeTileCount++;
            // WorldGen.Skyblock records this once while inspecting the generated world. It does not
            // become true again when players later mine the fossil block.
            if (tile.IsActive && tile.Type == 404)
                noFossils = false;
            if (tile.IsActive && tile.Type == 58)
                noHellstone = false;
        }

        return Create(metadata.SkyblockWorld, activeTileCount, tiles.Count, noHellstone, noFossils);
    }

    public static VanillaSkyblockRuntimeState1458 Create(
        bool skyblockWorld,
        int activeTileCount,
        int totalTileCount,
        bool? noHellstone = null,
        bool? noFossils = null)
    {
        bool lowTiles = IsLowTiles(skyblockWorld, activeTileCount, totalTileCount);
        return new VanillaSkyblockRuntimeState1458(
            skyblockWorld,
            lowTiles,
            skyblockWorld && noHellstone.GetValueOrDefault(),
            skyblockWorld && noFossils.GetValueOrDefault(),
            activeTileCount,
            totalTileCount);
    }

    public static bool IsLowTiles(bool skyblockWorld, int activeTileCount, int totalTileCount)
    {
        if (!skyblockWorld || totalTileCount <= 0 || activeTileCount < 0 || activeTileCount > totalTileCount)
            return false;

        return (long)activeTileCount * 100L < (long)totalTileCount * LowTilePercent;
    }
}
