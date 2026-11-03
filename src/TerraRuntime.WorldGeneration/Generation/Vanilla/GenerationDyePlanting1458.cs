using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;
/// <summary>Ordinary dye offerings, herb maturity, glow tulips and Chillet eggs on the same physical stream.</summary>
internal sealed class GenerationDyePlanting1458(Workspace workspace, IWorldGenerationVanillaRandom random, double worldSurface, double rockLayer, GenerationTileFraming1458 framing)
{
    private readonly WorldTileStore store = workspace.TileStore;
    private readonly int width = workspace.WidthTiles, height = workspace.HeightTiles;
    internal void PlantDye(int x, int y)
    {
        if (!At(x, y).IsActive || x < 95 || x > width - 95 || y < 95 || y > height - 95) return;
        WorldTile ground = At(x, y), above = At(x, y - 1);
        if (y < worldSurface && (!above.IsActive || CallerReplaceable(above.Type)))
        {
            if (Crowded(x, y, filterStyles: true)) return;
            if (ground.Type is 2 or 109) framing.TryPlaceDyePlant(x, y - 1, random.Next(4) == 0 ? 4 : 3);
            else if (ground.Type == 60) framing.TryPlaceDyePlant(x, y - 1, random.Next(2) == 0 ? 2 : 4);
            else if (ground.Type == 53 && above.LiquidAmount == 255 && At(x, y - 2).LiquidAmount == 255) framing.TryPlaceDyePlant(x, y - 1, 2);
            else if (ground.Type == 80 && !At(x - 1, y - 1).IsActive && !At(x + 1, y - 1).IsActive)
            {
                bool pure = true; for (int column = x - 5; column <= x + 5; column++) for (int row = y - 5; row <= y + 15; row++) if (At(column, row) is { IsActive: true, Type: 112 or 234 }) pure = false;
                if (pure) framing.TryPlaceDyePlant(x, y - 1, 6);
            }
        }
        if (y < worldSurface || y >= height - 200) return;
        above = At(x, y - 1);
        if (!above.IsActive || CallerReplaceable(above.Type))
        {
            if (Crowded(x, y, filterStyles: true)) return;
            if (ground.Type == 60)
            {
                int style = random.Next(2) == 0 ? 2 : random.Next(2) == 0 ? 0 : 1; framing.TryPlaceDyePlant(x, y - 1, style);
            }
            else if (ground.Type is 0 or 1 or 59) framing.TryPlaceDyePlant(x, y - 1, random.Next(2) == 0 ? 0 : 1);
            else if (above.LiquidAmount == 255 && At(x, y - 2).LiquidAmount == 255) framing.TryPlaceDyePlant(x, y - 1, 5);
        }
        if (At(x, y + 1).IsActive || Crowded(x, y, filterStyles: false, clamp: false)) return;
        if (ground.Type == 0) framing.TryPlaceDyePlant(x, y + 1, 7);
    }
    internal void MatureHerbs()
    {
        for (int x = 10; x < width - 10; x++) for (int y = 10; y < height - 10; y++)
        {
            ref WorldTile tile = ref At(x, y);
            if (y > rockLayer && At(x, y + 1).Type is 59 or 0 && framing.SolidTile(x, y + 1) && !tile.IsActive && tile.LiquidAmount == 0 && random.Next(25) == 0)
            { tile.Flags |= WorldTileFlags.Active; tile.Type = 82; tile.FrameX = 36; tile.FrameY = 0; }
            if (tile.Type == 82 && random.Next(3) == 0)
            {
                tile.Type = 83; if (tile.FrameX == 36 && random.Next(2) == 0) tile.Type = 84;
                if (tile.FrameX == 108 && random.Next(3) == 0) tile.Type = 84;
            }
        }
    }
    internal void GrowTulips()
    {
        int wanted = width <= 4200 ? 2 : width <= 6400 ? 4 : 6, placed = 0, remaining = 10000;
        while (placed < wanted && --remaining > 0)
        {
            int x = placed >= wanted / 2 && (remaining <= 7500 || random.Next(2) != 0) ? random.Next(width - 300, width - 100) : random.Next(100, 300);
            if (TryNaturalPlant(x, random.Next((int)worldSurface, height - 200), false)) placed++;
        }
    }
    internal void PlaceEggs()
    {
        int wanted = width <= 4200 ? 6 : width <= 6400 ? 9 : 12, placed = 0, remaining = 10000;
        while (placed < wanted && --remaining > 0) if (TryNaturalPlant(random.Next(100, width - 100), random.Next((int)rockLayer, height - 200), true)) placed++;
    }
    internal bool TryNaturalPlant(int x, int y, bool egg)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int column = random.Next(Math.Max(10, x - 10), Math.Min(width - 10, x + 10)), row = random.Next(Math.Max(10, y - 10), Math.Min(height - 10, y + 10));
            if (!framing.NaturalPlantGround(column, row) || NearbyNatural(column, row, egg ? 752 : 656, egg ? 20 : 120)) continue;
            if (egg) framing.TryPlaceChilletEgg(column, row); else framing.TryPlaceGlowTulip(column, row);
            if (At(column, row) is { IsActive: true } tile && tile.Type == (egg ? 752 : 656)) return true;
        }
        return false;
    }
    private bool NearbyNatural(int x, int y, int type, int distance)
    {
        for (int column = Math.Clamp(x - distance, 10, width - 11); column <= Math.Clamp(x + distance, 10, width - 11); column++)
            for (int row = Math.Clamp(y - distance, 10, height - 11); row <= Math.Clamp(y + distance, 10, height - 11); row++)
                if (At(column, row) is { IsActive: true } tile && tile.Type == type) return true;
        return false;
    }
    private bool Crowded(int x, int y, bool filterStyles, bool clamp = true)
    {
        int left = clamp ? Math.Clamp(x - 90, 1, width - 2) : x - 90, right = clamp ? Math.Clamp(x + 90, 1, width - 2) : x + 90,
            top = clamp ? Math.Clamp(y - 90, 1, height - 2) : y - 90, bottom = clamp ? Math.Clamp(y + 90, 1, height - 2) : y + 90;
        for (int column = left; column < right; column++) for (int row = top; row < bottom; row++)
            if (At(column, row) is { IsActive: true, Type: 227 } tile && (!filterStyles || tile.FrameX < 272)) return true;
        return false;
    }
    private static bool CallerReplaceable(ushort type) => type is 3 or 51 or 61 or 73 or 74 or 184;
    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
