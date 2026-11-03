using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    private void ApplyCactusPalmTreesCoral(IWorldGenerationContext context, Workspace workspace, IWorldGenerationVanillaRandom random)
    {
        WorldTileStore tiles = workspace.TileStore;
        int width = workspace.WidthTiles;
        int leftRate = random.Next(3, 13), rightRate = random.Next(3, 13);
        _ = random.Next(2, 6); _ = random.Next(2, 6);
        var framing = new GenerationTileFraming1458(tiles, random, workspace: workspace, useWorldGenerationSolidity: true);
        foreach (VanillaOasisAnchor1458 oasis in workspace.VanillaOasisAnchors)
        {
            int reach = (int)(oasis.Width * 1.5);
            for (int x = oasis.X - reach; x <= oasis.X + reach; x++)
                for (int y = oasis.Y - OasisBasin1458.OasisHeight1458; y <= oasis.Y + OasisBasin1458.OasisHeight1458; y++)
                {
                    double chance = 1;
                    for (int nearX = x - 8; nearX <= x + 8; nearX++)
                        for (int nearY = y - 8; nearY <= y + 8; nearY++)
                            if (InWorld(tiles, nearX, nearY) && tiles.Get(nearX, nearY) is { IsActive: true, Type: 323 }) chance = .13;
                    if (random.NextDouble() < chance) PalmTreeGrower1458.TryGrow(tiles, x, y, random, framing);
                    AddCoastalGroundCover(workspace, framing, x, y, random);
                }
        }
        for (int band = 0; band < 3; band++)
        {
            int left = band == 0 ? 5 : band == 1 ? 400 : width - 380;
            int right = band == 0 ? 380 : band == 1 ? width - 400 : width - 5;
            int rate = band == 0 ? leftRate : band == 1 ? 8 : rightRate;
            for (int x = left; x < right; x++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (random.Next(rate) != 0)
                {
                    for (int y = 0; y < state.Layers.WorldSurface - 1; y++) AddCoastalGroundCover(workspace, framing, x, y, random);
                    continue;
                }
                for (int y = 0; y < state.Layers.WorldSurface - 1; y++)
                {
                    WorldTile floor = tiles.Get(x, y);
                    if (!floor.IsActive || floor.Type is not (53 or 112 or 234)) continue;
                    WorldTile target = tiles.Get(x, y - 1);
                    if (target.IsActive || target.Wall != 0) continue;
                    if (band == 1)
                    {
                        int water = 0;
                        for (int wx = x - 50; wx < x + 50; wx++)
                            for (int wy = y - 25; wy < y + 25; wy++)
                                if (InWorld(tiles, wx, wy)) water += tiles.Get(wx, wy).LiquidAmount;
                        if (water / 255 > 25)
                        {
                            if (random.Next(4) == 0) PalmTreeGrower1458.TryGrow(tiles, x, y, random, framing);
                        }
                        else CactusGrower1458.Plant(tiles, x, y, random, framing);
                    }
                    else
                    {
                        if (tiles.Get(x, y - 2).LiquidAmount == 255 && tiles.Get(x, y - 3).LiquidAmount == 255 && tiles.Get(x, y - 4).LiquidAmount == 255)
                        {
                            if (random.Next(2) == 0) PlaceCoastalDecoration(workspace, framing, x, y - 1, 81, 0, random);
                            else PlaceCoastalDecoration(workspace, framing, x, y - 1, 324, RollShellStyle(random), random);
                            break;
                        }
                        if (tiles.Get(x, y - 2).LiquidAmount == 0 && y < state.Layers.WorldSurface)
                        {
                            PlaceCoastalDecoration(workspace, framing, x, y - 1, 324, RollShellStyle(random), random);
                            break;
                        }
                    }
                }
            }
        }
    }

    private static int RollShellStyle(IWorldGenerationVanillaRandom random)
    {
        int style = random.Next(2);
        if (random.Next(10) == 0) style = 2;
        if (random.Next(10) == 0) style = 3;
        if (random.Next(50) == 0) style = 4;
        return style;
    }

    private static bool InWorld(WorldTileStore tiles, int x, int y) => x >= 0 && y >= 0 && x < tiles.Dimensions.WidthTiles && y < tiles.Dimensions.HeightTiles;
    private static bool CoastalSolid(Workspace workspace, int x, int y, bool bottomSlopes = false)
    {
        if (!InWorld(workspace.TileStore, x, y)) return false;
        WorldTile tile = workspace.TileStore.Get(x, y);
        return tile.IsActive && !tile.IsActuated && !tile.IsHalfBrick && workspace.IsVanillaGenerationSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && (tile.Shape == 0 || bottomSlopes && tile.SourceSlope is 3 or 4);
    }

    private static bool HasCoastalWater(Workspace workspace, int x, int y, bool seaOat)
    {
        bool beach = x <= 380 || x >= workspace.WidthTiles - 380;
        int reach = seaOat && beach ? 65 : 45, vertical = seaOat && beach ? 25 : 20;
        int amount = 0;
        if (!seaOat && beach) return false;
        for (int wx = x - reach; wx <= x + reach; wx++)
            for (int wy = y - vertical; wy <= y + vertical; wy++)
                if (InWorld(workspace.TileStore, wx, wy) && !CoastalSolid(workspace, wx, wy)) amount += workspace.TileStore.Get(wx, wy).LiquidAmount;
        return beach ? amount / 255 < 20 : amount / 255 >= 20;
    }

    private static void AddCoastalGroundCover(Workspace workspace, GenerationTileFraming1458 framing, int x, int y, IWorldGenerationVanillaRandom random)
    {
        WorldTileStore tiles = workspace.TileStore;
        WorldTile target = tiles.Get(x, y), floor = tiles.Get(x, y + 1);
        if (target.Wall == 0 && !target.IsActive && target.LiquidAmount == 0 && CoastalSolid(workspace, x, y + 1, true) && WorldSmoothingCatalog1458.IsSandConversion(floor.TileType) && HasCoastalWater(workspace, x, y, true))
        {
            ref WorldTile oat = ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
            oat.Flags |= WorldTileFlags.Active; oat.Shape = 0; oat.Type = 529; oat.TileColor = floor.TileColor;
            const WorldTileFlags coat = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
            oat.Flags = (oat.Flags & ~coat) | (floor.Flags & coat); oat.FrameX = (short)(random.Next(5) * 18); oat.FrameY = 0;
            if (random.Next(2) == 0) oat.FrameX += 90;
            if (random.Next(2) == 0) oat.FrameX += 90;
        }
        int style = random.Next(9);
        if (x < 380 || x > workspace.WidthTiles - 380 || y < 5 || y > workspace.HeightTiles - 5) return;
        for (int wx = x - 1; wx <= x + 1; wx++)
        {
            for (int wy = y - 1; wy <= y; wy++)
            {
                WorldTile member = tiles.Get(wx, wy);
                if (member.IsActive && member.Type != 529 || member.LiquidAmount > 0) return;
            }
            if (!CoastalSolid(workspace, wx, y + 1) || !WorldSmoothingCatalog1458.IsSandConversion(tiles.Get(wx, y + 1).TileType)) return;
        }
        if (!HasCoastalWater(workspace, x, y, false)) return;
        for (int wx = x - 1; wx <= x + 1; wx++)
            for (int wy = y - 1; wy <= y; wy++) if (tiles.Get(wx, wy).IsActive) framing.KillTile(wx, wy);
        for (int wx = x - 1; wx <= x + 1; wx++)
            for (int wy = y - 1; wy <= y; wy++)
            {
                ref WorldTile member = ref tiles.Tiles[tiles.GetUncheckedIndex(wx, wy)]; member.Flags |= WorldTileFlags.Active; member.Type = 530; member.FrameX = (short)(style * 54 + (wx - x + 1) * 18); member.FrameY = (short)((wy - y + 1) * 18);
            }
    }

    private static void PlaceCoastalDecoration(Workspace workspace, GenerationTileFraming1458 framing, int x, int y, ushort type, int style, IWorldGenerationVanillaRandom random)
    {
        // Placement itself owns its validation and the final local framing halo.
        WorldTileStore tiles = workspace.TileStore;
        ref WorldTile tile = ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
        if (type == 81 && (tiles.Get(x, y - 1).IsActive || !CoastalSolid(workspace, x, y + 1))) return;
        if (!tile.IsActive)
        {
            tile.Type = 0; tile.FrameX = 0; tile.FrameY = 0; tile.ClearSourceShape(); tile.TileColor = 0;
            tile.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        }
        if (type == 324)
        {
            if (CoastalSolid(workspace, x, y + 1))
            { tile.Flags |= WorldTileFlags.Active; tile.Type = type; tile.FrameX = (short)(22 * random.Next(3)); tile.FrameY = (short)(22 * style); }
        }
        else if (!tile.IsActive)
        { tile.FrameX = (short)(26 * random.Next(6)); tile.Flags |= WorldTileFlags.Active; tile.Type = type; }
        if (type == 324) framing.SquareTileFrame(x, y);
        framing.SquareTileFrame(x, y);
    }
}
