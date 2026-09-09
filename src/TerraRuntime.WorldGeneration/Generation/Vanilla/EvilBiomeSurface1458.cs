using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary evil surface strips and Dirt/Mud SpreadGrass, pinned to WorldGen 1.4.5.8.</summary>
internal sealed class EvilBiomeSurface1458(
    WorldTileStore store, IWorldGenerationVanillaRandom random,
    double surfaceLow, double worldSurface, CancellationToken cancellationToken)
{
    private readonly GenerationGrass1458 grassSpread = new(store, random, cancellationToken);

    public void ConvertJungleColumn(int x, int left, int right, bool crimson)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (int y = (int)surfaceLow; y < worldSurface - 1; y++)
        {
            if (!At(x,y).IsActive) continue;
            int bottom = y + random.Next(10,14);
            for (int row = y; row < bottom; row++)
            {
                ref WorldTile cell = ref At(x,row);
                if (cell.IsActive && cell.Type == 60 && x >= left + random.Next(5) && x < right - random.Next(5))
                    cell.Type = (ushort)(crimson ? 662 : 661);
            }
            break;
        }
    }

    public void ConvertRegion(int left, int right, bool crimson)
    {
        double bottom = worldSurface + 40;
        for (int x = left; x < right; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bottom = Math.Clamp(bottom + random.Next(-2,3), worldSurface + 30, worldSurface + 50);
            bool foundActive = false;
            for (int y = (int)surfaceLow; y < bottom; y++)
            {
                // Preserve short-circuit RNG order even for air cells and at fractional layer boundaries.
                if (!(x > left + 1 && x < right - 2) && random.Next(2) == 0) continue;
                if (!(y > surfaceLow + 1 && y < bottom - 2) && random.Next(2) == 0) continue;
                ref WorldTile cell = ref At(x,y);
                if (!cell.IsActive) continue;
                if (cell.Type == 53 && x >= left + random.Next(5) && x <= right - random.Next(5))
                    cell.Type = (ushort)(crimson ? 234 : 112);
                if (y < worldSurface - 1 && !foundActive && cell.Type is 0 or 59)
                    SpreadGrass(x,y,cell.Type,crimson);
                foundActive = true;
                cell.Wall = cell.Wall switch { 216 => (ushort)(crimson ? 218 : 217), 187 => (ushort)(crimson ? 221 : 220), _ => cell.Wall };
                if (cell.Type == 1)
                {
                    if (x >= left + random.Next(5) && x <= right - random.Next(5))
                        cell.Type = (ushort)(crimson ? 203 : 25);
                }
                else cell.Type = cell.Type switch
                {
                    2 => (ushort)(crimson ? 199 : 23),
                    60 => (ushort)(crimson ? 662 : 661),
                    161 => (ushort)(crimson ? 200 : 163),
                    396 => (ushort)(crimson ? 401 : 400),
                    397 => (ushort)(crimson ? 399 : 398),
                    _ => cell.Type
                };
            }
        }
    }

    internal void SpreadGrass(int x, int y, ushort dirt, bool crimson)
    {
        if (dirt is not (0 or 59)) throw new InvalidOperationException("Unverified evil grass substrate.");
        ushort grass = (ushort)(dirt == 0 ? (crimson ? 199 : 23) : (crimson ? 662 : 661));
        grassSpread.Apply(x,y,dirt,grass);
    }


    private ref WorldTile At(int x, int y)
    {
        if ((uint)x >= (uint)store.Dimensions.WidthTiles || (uint)y >= (uint)store.Dimensions.HeightTiles)
            throw new InvalidOperationException("Evil surface conversion reached outside the candidate terrain.");
        return ref store.Tiles[store.GetUncheckedIndex(x,y)];
    }
}
