using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;
/// <summary>Ordinary PlantAlch offers and the shared suitable-herb/raw herb placement rules.</summary>
internal sealed class GenerationHerbPlanting1458(WorldTileStore store, IWorldGenerationVanillaRandom random, double worldSurface, double rockLayer, int beachDistance)
{
    private readonly int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
    internal void Offer()
    {
        int x = random.Next(20, width - 20), y;
        if (random.Next(40) == 0) y = random.Next((int)(rockLayer + height) / 2, height - 20);
        else if (random.Next(10) != 0) y = random.Next((int)worldSurface, height - 20);
        else y = random.Next(20, height - 20);
        while (y < height - 20 && !At(x, y).IsActive) y++;
        WorldTile ground = At(x, y), above = At(x, y - 1);
        if (!ground.IsActive || ground.IsActuated || above.IsActive || above.LiquidAmount != 0) return;
        int radius = (int)(15d * (width / 4200d)), count = 0;
        for (int column = Math.Clamp(x - radius, 4, width - 4); column <= Math.Clamp(x + radius, 4, width - 4); column++)
            for (int row = Math.Clamp(y - radius, 4, height - 4); row <= Math.Clamp(y + radius, 4, height - 4); row++)
                if (At(column, row).Type is 82 or 83 or 84) count++;
        if (count < 5) PlaceSuitable(x, y);
    }
    internal void PlaceSuitable(int x, int y)
    {
        int style = At(x, y).Type switch
        {
            2 or 109 => 0,
            60 => 1,
            0 or 59 => 2,
            23 or 661 or 25 or 203 or 199 or 662 => 3,
            53 or 116 when x >= beachDistance && x <= width - beachDistance => 4,
            57 or 633 => 5,
            147 or 163 or 164 or 161 or 200 => 6,
            _ => -1
        };
        if (style >= 0) Place(x, y - 1, style);
    }
    internal bool Place(int x, int y, int style)
    {
        ref WorldTile tile = ref At(x, y); WorldTile floor = At(x, y + 1);
        if (tile.IsActive || !floor.IsActive || floor.IsActuated || floor.IsHalfBrick || floor.SourceSlope != 0) return false;
        bool pot = floor.Type is 78 or 380;
        bool valid = style switch
        {
            0 => (pot || floor.Type is 2 or 477 or 492 or 109) && tile.LiquidAmount == 0,
            1 => (pot || floor.Type == 60) && tile.LiquidAmount == 0,
            2 => (pot || floor.Type is 0 or 59) && tile.LiquidAmount == 0,
            3 => (pot || floor.Type is 661 or 662 or 203 or 199 or 23 or 25) && tile.LiquidAmount == 0,
            4 => (pot || floor.Type is 53 or 116) && (tile.LiquidAmount == 0 || tile.LiquidKind != WorldLiquidKind.Lava),
            5 => (pot || floor.Type is 57 or 633) && (tile.LiquidAmount == 0 || tile.LiquidKind == WorldLiquidKind.Lava),
            6 => (pot || floor.Type is 147 or 161 or 163 or 164 or 200) && (tile.LiquidAmount == 0 || tile.LiquidKind != WorldLiquidKind.Lava),
            _ => true
        };
        if (!valid) return false;
        tile.Flags |= WorldTileFlags.Active; tile.Type = 82; tile.FrameX = unchecked((short)(18 * style)); tile.FrameY = 0;
        tile.TileColor = floor.TileColor;
        tile.Flags = (tile.Flags & ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock)) | (floor.Flags & (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock));
        return true;
    }
    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
