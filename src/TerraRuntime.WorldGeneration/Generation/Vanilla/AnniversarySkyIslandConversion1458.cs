using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-reachable slice of TerrariaServer 1.4.5.8 WorldGen.ConvertSkyIslands(2, true).
/// Admission covers the grass offers and overhead identities of the two committed
/// anniversary checkpoint97 worlds; other terrain profiles remain fail-closed.
/// </summary>
internal static class AnniversarySkyIslandConversion1458
{
    internal static int ConvertClearGrassOffers(Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        if (!workspace.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Sky-island conversion requires retained world surface.");
        WorldTileStore tiles = workspace.TileStore;
        int width = tiles.Dimensions.WidthTiles, height = tiles.Dimensions.HeightTiles;
        int lastCloudY = 0;
        for (int y = 20; y < layers.WorldSurface && y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 20; x < width - 20; x++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive || !IsCloud(tile.Type))
                    continue;
                lastCloudY = y;
                break;
            }
        }

        // The official conversion invokes WorldGen.Convert, optional KillTile
        // and TryGrowingTreeByType for every sky-grass offer. Reject an unsupported
        // image before the first mutation or RNG draw.
        for (int x = 20; x <= width - 20; x++)
            for (int y = 20; y < lastCloudY; y++)
            {
                WorldTile grass = tiles.Get(x, y);
                if (!grass.IsActive || grass.Type != 2)
                    continue;
                WorldTile above = tiles.Get(x, y - 1);
                bool clearAbove = !above.IsActive && above.Type is 0 or 189;
                bool source97Above = above.IsActive && above.Type is
                    2 or 3 or 27 or 73 or 82 or 185 or 187 or 189 or 207 or 596 or 616;
                if (grass.Wall is not (0 or 73) ||
                    !(clearAbove || source97Above))
                    throw new NotSupportedException($"Sky grass at ({x},{y}) has wall {grass.Wall} and overhead " +
                        $"{(above.IsActive ? "active" : "inactive")} type {above.Type}; source branch not admitted.");
            }

        var framing = new GenerationTileFraming1458(tiles, random, workspace,
            useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity);
        int converted = 0;
        for (int x = 20; x <= width - 20; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 20; y < lastCloudY; y++)
            {
                ref WorldTile grass = ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
                if (!grass.IsActive || grass.Type != 2)
                    continue;
                if (tiles.Get(x, y - 1).Type is 596 or 616)
                    framing.KillTile(x, y - 1);
                grass.Type = 109; // WorldGen.Convert(2): Conversion.Grass => Hallowed Grass.
                framing.SquareTileFrame(x, y);
                if (tiles.Get(x, y - 1).Type is 82 or 83 or 185 or 186 or 187 or 227)
                    framing.KillTile(x, y - 1);
                converted++;
                if (random.Next(3) == 0)
                    TreeGrower1458.TryGrow(tiles, x, y, random, framing: framing);
            }
        }
        return converted;
    }

    private static bool IsCloud(ushort type) => type is 189 or 196 or 460 or 717 or 718 or 719;
}
