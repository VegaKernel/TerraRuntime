using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary WorldGen.ShroomPatch brush and roots, before pass-wide grass and cleanup.</summary>
internal sealed class MushroomPatch1458(WorldTileStore store, IWorldGenerationVanillaRandom random,
    double worldSurface, VanillaLiquidLines1458 lines, WorldTileRegion undergroundDesert,
    CancellationToken cancellation)
{
    private readonly SmallTerrainRunner1458 roots = new(store, random, worldSurface, lines, cancellation, undergroundDesert);

    public void Place(int originX, int originY)
    {
        double widthScale = store.Dimensions.WidthTiles / 4200d;
        double radius = random.Next(80, 100) * widthScale;
        double remaining = random.Next(20, 26) * widthScale;
        double first = remaining - 1;
        double x = originX, y = originY - remaining * .3;
        double vx = random.Next(-100, 101) * .005, vy = random.Next(-200, -100) * .005;
        while (radius > 0 && remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            radius -= random.Next(3);
            remaining--;
            int left = Math.Max(0, (int)(x - radius * .5)), right = Math.Min(store.Dimensions.WidthTiles, (int)(x + radius * .5));
            int top = Math.Max(0, (int)(y - radius * .5)), bottom = Math.Min(store.Dimensions.HeightTiles, (int)(y + radius * .5));
            double brush = radius * random.Next(80, 120) * .01;
            for (int tx = left; tx < right; tx++)
            for (int ty = top; ty < bottom; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                double dx = Math.Abs(tx - x), dy = Math.Abs((ty - y) * 2.3);
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < brush * .8 && tile.LiquidKind == WorldLiquidKind.Lava) tile.LiquidAmount = 0;
                if (distance < brush * .2 && ty < y)
                {
                    tile.Flags &= ~WorldTileFlags.Active;
                    if (tile.Wall > 0) tile.Wall = 80;
                }
                else if (distance < brush * .4 * (.95 + random.NextDouble() * .1))
                {
                    tile.Type = SmallTerrainRunner1458.Mud;
                    if (remaining == first && ty > y) tile.Flags |= WorldTileFlags.Active;
                    if (tile.Wall > 0) tile.Wall = 80;
                }
            }
            x += vx; y += vy;
            x += vx; // Source moves horizontally twice before changing the velocity.
            vx += random.Next(-100, 110) * .005;
            vy -= random.Next(110) * .005;
            vx = vx < 0 ? -.5 : .5;
            vy = Math.Clamp(vy, -.5, .5);
            for (int root = 0; root < 2; root++)
            {
                int tx = 0, ty = 0;
                bool found = false;
                for (int attempt = 0; attempt < 100000; attempt++)
                {
                    if ((attempt & 255) == 0) cancellation.ThrowIfCancellationRequested();
                    tx = (int)x + random.Next(-20, 20);
                    ty = (int)y + random.Next(0, 20);
                    if ((uint)tx >= store.Dimensions.WidthTiles || (uint)ty >= store.Dimensions.HeightTiles)
                        throw new InvalidOperationException("Mushroom root search escaped its admitted world bounds.");
                    WorldTile candidate = At(tx, ty);
                    if (candidate.IsActive || candidate.Type == SmallTerrainRunner1458.Mud) { found = true; break; }
                }
                if (!found) throw new InvalidOperationException("Mushroom root search exhausted its safety budget.");
                int strength = random.Next(10, 20), steps = random.Next(10, 20);
                roots.Run(tx, ty, strength, steps, SmallTerrainRunner1458.Mud, false, 0, 2, true);
            }
        }
    }

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
