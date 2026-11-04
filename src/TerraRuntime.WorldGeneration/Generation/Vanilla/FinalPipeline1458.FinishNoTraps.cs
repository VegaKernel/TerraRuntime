using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // WorldGen.FinishNoTraps, TerrariaServer 1.4.5.8. The first scan consumes one draw
    // for every interior cell, even where there is no crystal or trap chest.
    internal static void FinishNoTraps(
        Workspace workspace,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        var grid = new RuntimeGrid(workspace);
        var framing = new GenerationTileFraming1458(grid.Store, random, workspace, useWorldGenerationSolidity: true);
        FinishNoTraps(workspace, grid, random, framing, cancellationToken);
    }

    private static void FinishNoTraps(
        Workspace workspace,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random,
        GenerationTileFraming1458 framing,
        CancellationToken cancellationToken)
    {
        VanillaWorldSeedProfile1458 profile = workspace.VanillaSeedProfile;
        if (!profile.Has(VanillaSpecialWorldSeed1458.NoTraps))
            throw new InvalidOperationException("No Traps cleanup requires the source seed profile.");

        bool anniversary = profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10);
        bool remix = profile.Has(VanillaSpecialWorldSeed1458.Remix);
        int crystalDivisor = profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy) && !anniversary ? 2 : 5;
        workspace.SetVanillaBoulderSolidity(false);
        try
        {
            for (int x = 50; x < grid.Width - 50; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int y = 50; y < grid.Height - 50; y++)
                {
                    ref WorldTile tile = ref grid.At(x, y);
                    if (random.Next(crystalDivisor) == 0 && tile.IsActive &&
                        tile.Type == 12 && tile.FrameX == 0 && tile.FrameY == 0)
                    {
                        grid.At(x, y).Type = 665;
                        grid.At(x, y + 1).Type = 665;
                        grid.At(x + 1, y).Type = 665;
                        grid.At(x + 1, y + 1).Type = 665;
                    }

                    if ((x & 1) != 0 || (y & 1) != 0 || !tile.IsActive ||
                        tile.Type != 105 && !(tile.Type == 467 && tile.FrameX is 144 or 162))
                        continue;

                    if (!HasWireNearTrap(grid, x, y))
                    {
                        bool attached = TryAttachNearbyWire(grid, random, x, y);
                        if (!attached && tile.Type == 105)
                            TryAttachNewPlate(grid, random, framing, x, y);
                    }

                    if (tile.Type == 467 && (!anniversary || remix))
                        TryAttachExplosive(grid, random, framing, x, y);
                    else
                        y++;
                }
            }
        }
        finally
        {
            // The source restores Main.tileSolid boulders before its second scan and chest inventory loop.
            workspace.SetVanillaBoulderSolidity(true);
        }

        for (int x = 20; x < grid.Width - 20; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 20; y < grid.Height - 20; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (!tile.IsActive || tile.Type != 467 || tile.FrameX != 144 || tile.FrameY != 0 ||
                    HasRedWire(tile) || HasRedWire(grid.At(x + 1, y)) ||
                    HasRedWire(grid.At(x, y + 1)) || HasRedWire(grid.At(x + 1, y + 1)))
                    continue;

                for (int dx = 0; dx < 2; dx++)
                    for (int dy = 0; dy < 2; dy++)
                    {
                        ref WorldTile part = ref grid.At(x + dx, y + dy);
                        part.Type = 21;
                        part.FrameX = (short)(36 + 18 * dx);
                    }
            }
        }

        int nextSlot = 0;
        foreach (WorldChest chest in workspace.CaptureGeneratedChests())
        {
            // Main.chest is indexed and the source breaks at its first null slot. A destroyed
            // generated chest leaves that same hole until a later birth reuses it.
            if (nextSlot >= 8000 || chest.SlotId != nextSlot)
                break;
            nextSlot++;
            cancellationToken.ThrowIfCancellationRequested();
            if (random.Next(15) != 0 || chest.Items.Length < 2 || chest.Items[1].Stack == 0)
                continue;
            var items = new WorldGenerationChestItem[chest.Items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                WorldChestItem item = chest.Items[i];
                items[i] = new WorldGenerationChestItem(item.Stack, new ItemTypeId(item.ItemType), new PrefixId(item.Prefix));
            }
            for (int i = 1; i < items.Length; i++)
            {
                if (items[i].Stack != 0)
                    continue;
                items[i] = new WorldGenerationChestItem(1, new ItemTypeId(5346));
                break;
            }
            if (!workspace.TryReplaceGeneratedChestItems(chest.X, chest.Y, items))
                throw new InvalidOperationException("No Traps chest loot lost its canonical side table.");
        }
    }

    private static bool HasWireNearTrap(RuntimeGrid grid, int x, int y)
    {
        for (int xx = x - 1; xx <= x + 1; xx++)
            for (int yy = y - 1; yy <= y + 1; yy++)
                if (HasRedWire(grid.At(xx, yy)))
                    return true;
        return false;
    }

    private static bool TryAttachNearbyWire(RuntimeGrid grid, IWorldGenerationVanillaRandom random, int x, int y)
    {
        for (int attempt = 0; attempt < 625; attempt++)
        {
            int targetX = random.Next(x - 25, x + 26);
            int targetY = random.Next(y - 25, y + 26);
            if (!HasRedWire(grid.At(targetX, targetY)))
                continue;
            ConnectRedWire(grid, x, y, targetX, targetY);
            return true;
        }
        return false;
    }

    private static void TryAttachNewPlate(
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random,
        GenerationTileFraming1458 framing,
        int x,
        int y)
    {
        for (int attempt = 0; attempt < 225; attempt++)
        {
            int plateX = x + random.Next(-15, 16);
            int plateY = y + random.Next(-15, 16);
            int style = random.Next(2, 4);
            if (grid.At(plateX, plateY).IsActive && grid.At(plateX, plateY).Type == 488)
                continue;
            // Source PlaceTile(forced:true) reaches Place1x1 and SquareTileFrame. Its one-cell
            // validator needs a flat SolidTile2 support and leaves already active anchors alone.
            if (!grid.At(plateX, plateY).IsActive && framing.SolidTile2(plateX, plateY + 1) &&
                !GenerationObjectSupport1458.IsBoulder(grid.At(plateX, plateY + 1).Type))
            {
                ref WorldTile plate = ref grid.At(plateX, plateY);
                plate.Flags |= WorldTileFlags.Active;
                plate.Type = 135;
                plate.FrameY = checked((short)(style * 18));
            }
            framing.SquareTileFrame(plateX, plateY);
            if (grid.At(plateX, plateY).Type != 135)
                continue;
            ConnectRedWire(grid, x, y, plateX, plateY);
            return;
        }
    }

    private static void TryAttachExplosive(
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random,
        GenerationTileFraming1458 framing,
        int x,
        int y)
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            int targetX = x + random.Next(-8, 9);
            int targetY = y + random.Next(-8, 9);
            ushort type = grid.At(targetX, targetY).Type;
            if (type is not (0 or 1 or 59 or 151) && !StoneBiomeTiles1458.IsOre(type))
                continue;
            bool surrounded = true;
            for (int xx = targetX - 1; xx <= targetX + 1 && surrounded; xx++)
                for (int yy = targetY - 1; yy <= targetY + 1; yy++)
                    if (!framing.SolidTile(xx, yy))
                    {
                        surrounded = false;
                        break;
                    }
            if (!surrounded)
                continue;

            ref WorldTile explosive = ref grid.At(targetX, targetY);
            explosive.Type = 141;
            explosive.FrameX = 0;
            explosive.FrameY = 0;
            explosive.ClearSourceShape();
            framing.TileFrame(targetX, targetY, resetFrame: true);
            if (grid.At(targetX, targetY).Type != 141)
                continue;
            ConnectRedWire(grid, x, y, targetX, targetY);
            return;
        }
    }

    private static void ConnectRedWire(RuntimeGrid grid, int fromX, int fromY, int toX, int toY)
    {
        grid.At(fromX, fromY).Flags |= WorldTileFlags.WireRed;
        while (fromX != toX)
        {
            fromX += fromX < toX ? 1 : -1;
            grid.At(fromX, fromY).Flags |= WorldTileFlags.WireRed;
        }
        while (fromY != toY)
        {
            fromY += fromY < toY ? 1 : -1;
            grid.At(fromX, fromY).Flags |= WorldTileFlags.WireRed;
        }
    }

    private static bool HasRedWire(in WorldTile tile) =>
        (tile.Flags & WorldTileFlags.WireRed) != 0;
}
