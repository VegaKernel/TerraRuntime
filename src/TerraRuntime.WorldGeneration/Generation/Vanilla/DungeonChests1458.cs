using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonGlobalBiomeChests/BasicChests and their AddBuriedChest placement slice.</summary>
internal sealed class DungeonChests1458(Workspace workspace, IWorldGenerationVanillaRandom random,
    VanillaWorldGenerationBootstrapState1458 bootstrap, double worldSurface, double rockLayer, CancellationToken cancellationToken,
    IReadOnlyList<DungeonPitTraps1458.Pit>? pits = null)
{
    private static ReadOnlySpan<int> BasicLoot => [155, 156, 157, 163, 113, 3317, 327, 164];
    private int lootIndex;
    private int Width => workspace.WidthTiles;
    private int Height => workspace.HeightTiles;

    internal int PlaceBiome(DungeonBounds1458 bounds, DungeonBounds1458 entrance)
    {
        int placed = 0;
        int count = workspace.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld) ? 6 : 5;
        for (int biome = 0; biome < count; biome++)
        for (int attempt = 1; attempt < 1000; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(bounds.Left, bounds.Right), y = random.Next((int)worldSurface, bounds.Bottom);
            if (x >= entrance.Left && x <= entrance.Right && y >= entrance.Top && y <= entrance.Bottom ||
                !DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall) || At(x, y).IsActive) continue;
            (int item, int style, int type) = biome switch
            {
                0 => (1156, 23, 21),
                1 => bootstrap.EffectiveCrimson ? (1569, 25, 21) : (1571, 24, 21),
                2 => (1260, 26, 21), 3 => (1572, 27, 21), 4 => (4607, 13, 467),
                _ => bootstrap.EffectiveCrimson ? (1571, 24, 21) : (1569, 25, 21)
            };
            if (TryPlace(x, y, (ushort)type, style, item)) { placed++; break; }
        }
        return placed;
    }

    internal int PlaceBasic(IReadOnlyList<DungeonComponent1458> components)
    {
        int placed = 0;
        foreach (DungeonComponent1458 room in components)
        {
            if (room.Kind is not (DungeonComponentKind1458.Room or DungeonComponentKind1458.StartingRoom)) continue;
            int strength = room.RoomStrength ?? throw new InvalidOperationException("Dungeon room lacks its source chest-sampling strength.");
            int radius = (int)(strength * .4f);
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int x = (int)(room.End.X - radius + radius * 2 * random.NextDouble());
                int y = (int)(room.End.Y - radius + radius * 2 * random.NextDouble());
                if (!AllowsBasicChestArea(x, y)) continue;
                if (lootIndex >= 8) lootIndex = 0;
                int item = lootIndex == 2 && workspace.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.Remix) ? 2623 : BasicLoot[lootIndex];
                int style = lootIndex == 6 ? 0 : 2;
                if (y < worldSurface + 50d) { item = 327; style = 0; }
                if (!TryPlace(x, y, 21, style, item)) continue;
                lootIndex++; placed++; break;
            }
        }
        return placed;
    }

    private bool AllowsBasicChestArea(int x, int y)
    {
        // Ordinary room/style/entrance gates accept basic chests. DungeonData still tests the inclusive
        // clamped fluff rectangle for wall350 before advancing the cyclic primary item selection.
        int left = Math.Clamp(x - 1, 10, Width - 10), right = Math.Clamp(x + 1, 10, Width - 10);
        int top = Math.Clamp(y - 1, 10, Height - 10), bottom = Math.Clamp(y + 1, 10, Height - 10);
        if (right <= left) right = Math.Clamp(left + 1, 10, Width - 10);
        if (bottom <= top) bottom = Math.Clamp(top + 1, 10, Height - 10);
        for (int tx = left; tx <= right; tx++)
        for (int ty = top; ty <= bottom; ty++)
            if (At(tx, ty).Wall == 350 || DungeonPitTraps1458.Contains(pits, tx, ty)) return false;
        return true;
    }

    internal bool TryPlace(int x, int y, ushort type, int style, int item)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (x < 3 || x >= Width - 3 || y < 3 || y >= Height - 10)
            throw new InvalidOperationException("Dungeon chest seed escaped the admitted world margin.");
        BuriedChest1458.EnsureQualifiedScope(workspace.VanillaSeedProfile,style,type);
        var context=BuriedChestContext1458.GetOrCreate(workspace, bootstrap, worldSurface, rockLayer);
        bool prior=context.GeneratingDungeon;
        context.GeneratingDungeon=true;
        try
        {
            return BuriedChest1458.CreateOwned(workspace, random).TryAdd(x,y,out _,out _,item,
                notNearOtherChests:false,chestStyle:style,trySlope:false,chestTileType:type);
        }
        finally { context.GeneratingDungeon=prior; }
    }

    private ref WorldTile At(int x, int y) => ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];
}
