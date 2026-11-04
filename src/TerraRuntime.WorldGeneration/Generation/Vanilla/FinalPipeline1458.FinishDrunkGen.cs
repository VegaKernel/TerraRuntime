using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // WorldGen.FinishDrunkGen, TerrariaServer 1.4.5.8. This consumes the shared
    // stream after FinishTenthAnniversaryWorld and before FinishNotTheBees.
    internal static void FinishDrunkGen(Workspace workspace, IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        if (!workspace.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld))
            throw new InvalidOperationException("Drunk-world finalization requires its source seed profile.");

        var grid = new RuntimeGrid(workspace);
        byte dungeonColor = (byte)random.Next(13, 25);
        for (int x = 0; x < grid.Width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < grid.Height; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (!tile.IsActive || !DungeonGenerationTiles1458.IsDungeonTile(tile.Type))
                    continue;
                if (tile.Type == 44)
                {
                    dungeonColor = (byte)random.Next(13, 15);
                    if (random.Next(2) == 0)
                        dungeonColor = (byte)random.Next(23, 25);
                }
                if (tile.Type == 43)
                    dungeonColor = (byte)random.Next(15, 19);
                if (tile.Type == 41)
                    dungeonColor = (byte)random.Next(19, 23);
            }
        }

        bool notTheBees = workspace.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.NotTheBees);
        for (int x = 10; x < grid.Width - 10; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 10; y < grid.Height - 10; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive &&
                    (DungeonGenerationTiles1458.IsDungeonTile(tile.Type) || tile.Type is 481 or 482 or 483))
                    tile.TileColor = dungeonColor;
                if (DungeonGenerationTiles1458.IsDungeonWall(tile.Wall))
                    tile.WallColor = 25;

                if (tile.IsActive)
                {
                    if (tile.Type == 60)
                        for (int neighborX = x - 1; neighborX <= x + 1; neighborX++)
                            for (int neighborY = y - 1; neighborY <= y + 1; neighborY++)
                            {
                                ref WorldTile neighbor = ref grid.At(neighborX, neighborY);
                                if (neighbor.Type is 147 or 161)
                                    neighbor.Type = 59;
                            }

                    bool paintHive = tile.Type == 226 ||
                        tile.Type == 60 && grid.At(x, y + 1).Type == 226 ||
                        tile.Type == 137 && tile.FrameY / 18 is >= 1 and <= 4;
                    if (paintHive && !notTheBees)
                        tile.TileColor = 16;
                }
                if (tile.Wall == 87)
                    tile.WallColor = 16;
            }
        }

        int nextSlot = 0;
        foreach (WorldChest chest in workspace.CaptureGeneratedChests())
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Source stops at the first null Main.chest slot; later occupied
            // slots must not consume draws or receive bonus items.
            if (chest.SlotId != nextSlot++)
                break;
            var items = new WorldGenerationChestItem[chest.Items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                WorldChestItem item = chest.Items[i];
                items[i] = new WorldGenerationChestItem(item.Stack,
                    new ItemTypeId(item.ItemType), new PrefixId(item.Prefix));
            }

            bool changed = false;
            if (random.Next(15) == 0 && items.Length > 1 && items[1].Stack != 0)
                changed |= TryAddBonusItem(items, 5001);
            if (random.Next(30) == 0 && items.Length > 1 && items[1].Stack != 0)
                changed |= TryAddBonusItem(items, 678);
            if (changed && !workspace.TryReplaceGeneratedChestItems(chest.X, chest.Y, items))
                throw new InvalidOperationException("Drunk-world chest bonus lost its canonical side table.");
        }
    }

    private static bool TryAddBonusItem(WorldGenerationChestItem[] items, int itemType)
    {
        for (int slot = 1; slot < items.Length; slot++)
        {
            if (items[slot].Stack != 0)
                continue;
            items[slot] = new WorldGenerationChestItem(1, new ItemTypeId(itemType));
            return true;
        }
        return false;
    }
}
