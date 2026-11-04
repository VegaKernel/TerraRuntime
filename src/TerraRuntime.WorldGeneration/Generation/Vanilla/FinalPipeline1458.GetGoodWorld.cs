using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // TerrariaServer.exe 1.4.5.8 WorldGen.FinishGetGoodWorld. Called after the other
    // FinalCleanup secret-seed tails and before FinishRemixWorld.
    private void FinishGetGoodWorld(IWorldGenerationContext context, RuntimeGrid grid,
        IWorldGenerationVanillaRandom random, GenerationTileFraming1458 framing)
    {
        Workspace workspace = (Workspace)context.Workspace;
        VanillaWorldSeedProfile1458 profile = workspace.VanillaSeedProfile;
        bool bees = profile.Has(VanillaSpecialWorldSeed1458.NotTheBees);
        bool drunk = profile.Has(VanillaSpecialWorldSeed1458.DrunkWorld);
        bool remix = profile.Has(VanillaSpecialWorldSeed1458.Remix);
        bool anniversary = profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10);
        // WorldGen.crimson is the retained bootstrap choice; Drunk/combined profiles can
        // choose it independently of the host's requested evil option.
        bool crimson = RequireBootstrap().EffectiveCrimson;

        int lastCloudRow = 0;
        for (int y = 20; y < state.Layers.WorldSurface; y++)
            for (int x = 20; x < grid.Width - 20; x++)
                if (IsActiveCloud(grid.At(x, y)))
                {
                    lastCloudRow = y;
                    break;
                }

        byte dungeonPaint = (byte)random.Next(13, 25);
        for (int x = 0; x < grid.Width; x++)
        {
            bool found = false;
            for (int y = 0; y < grid.Height; y++)
            {
                WorldTile cell = grid.At(x, y);
                if (!cell.IsActive || !DungeonGenerationTiles1458.IsDungeonTile(cell.Type))
                    continue;
                if (cell.TileColor == 14)
                {
                    found = true;
                    dungeonPaint = 14;
                    continue;
                }
                if (cell.Type == 44)
                {
                    found = true;
                    dungeonPaint = (byte)random.Next(13, 15);
                    if (random.Next(2) == 0)
                        dungeonPaint = (byte)random.Next(23, 25);
                }
                if (cell.Type == 43)
                {
                    found = true;
                    dungeonPaint = (byte)random.Next(15, 19);
                }
                if (cell.Type == 41)
                {
                    found = true;
                    dungeonPaint = (byte)random.Next(19, 23);
                }
            }
            if (found)
                break;
        }

        for (int x = 10; x < grid.Width - 10; x++)
            for (int y = 10; y < grid.Height - 10; y++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                ref WorldTile cell = ref grid.At(x, y);
                if (cell.IsActive && (DungeonGenerationTiles1458.IsDungeonTile(cell.Type) ||
                    cell.Type is 481 or 482 or 483))
                    cell.TileColor = dungeonPaint;
                if (!drunk && !bees && DungeonGenerationTiles1458.IsDungeonWall(cell.Wall))
                    cell.WallColor = dungeonPaint;
                if (!bees && cell.IsActive &&
                    (cell.Type == 226 || cell.Type == 60 && grid.At(x, y + 1).Type == 226 ||
                     cell.Type == 137 && cell.FrameY / 18 is >= 1 and <= 4))
                    cell.TileColor = 17;
                if (cell.Wall == 87)
                    cell.WallColor = 25;
                if (!cell.IsActive)
                    continue;
                if (!remix && cell.Type == 57 && random.Next(15) == 0)
                {
                    if (grid.At(x, y - 1).Type == 57)
                        cell.Flags &= ~WorldTileFlags.Active;
                    cell.LiquidAmount = byte.MaxValue;
                    cell.LiquidKind = WorldLiquidKind.Lava;
                }
                if (!anniversary && !bees && y < lastCloudRow && cell.Type == 2)
                {
                    cell.Type = crimson ? (ushort)199 : (ushort)23;
                    ref WorldTile above = ref grid.At(x, y - 1);
                    if (above.Type is 3 or 73)
                        above.Flags &= ~WorldTileFlags.Active;
                    if (above.Type is 27 or 596 or 616 or 82 or 83 or 186 or 187 or 185 or 227)
                        framing.KillTile(x, y - 1);
                }
            }

        // The source stops at the first null Main.chest slot. CaptureGeneratedChests
        // preserves source slot IDs, including holes, while giving us detached item arrays.
        int expectedSlot = 0;
        foreach (WorldChest chest in workspace.CaptureGeneratedChests())
        {
            if (chest.SlotId != expectedSlot++)
                break;
            if (random.Next(10) != 0 || chest.Items.Length <= 1 || chest.Items[1].Stack == 0)
                continue;
            int empty = -1;
            for (int index = 1; index < chest.Items.Length; index++)
                if (chest.Items[index].Stack == 0)
                {
                    empty = index;
                    break;
                }
            if (empty < 0)
                continue;
            WorldGenerationChestItem[] items = new WorldGenerationChestItem[chest.Items.Length];
            for (int index = 0; index < items.Length; index++)
            {
                WorldChestItem held = chest.Items[index];
                items[index] = held.Stack > 0
                    ? new WorldGenerationChestItem(held.Stack, new ItemTypeId(held.ItemType), new PrefixId(held.Prefix))
                    : default;
            }
            items[empty] = new WorldGenerationChestItem(1, new ItemTypeId(678));
            if (!workspace.TryReplaceGeneratedChestItems(chest.X, chest.Y, items))
                throw new InvalidOperationException("Could not publish the worthy seed chest reward.");
        }
    }
}
