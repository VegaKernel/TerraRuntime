using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.MountainCaveOpenings</c>. For every anchor retained by the
/// earlier <c>MountainCaves</c> pass the source runs <c>WorldGen.CaveOpenater</c> and then
/// <c>WorldGen.Cavinator</c> with <c>genRand.Next(40, 50)</c> recursion steps.
/// </summary>
/// <remarks>
/// Both helpers only clear the vanilla active bit (<c>Tile.active(false)</c> touches nothing else), so type,
/// frames, walls, liquid, colors and shape survive the opening unchanged. That is why this pass deliberately does
/// not reuse the pipeline's normalizing clear helper.
/// </remarks>
internal sealed class MountainCaveOpenings1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double rockLayer,
    CancellationToken cancellation)
{
    /// <summary>Mountain openings never clear natural Sand; the source guards it separately from the clear set.</summary>
    private const ushort Sand = 53;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    public void Open(int x, int y)
    {
        cancellation.ThrowIfCancellationRequested();
        CaveOpenater(x, y);
        Cavinator(x, y, random.Next(40, 50));
    }

    private void CaveOpenater(int i, int j)
    {
        double size = random.Next(7, 12);
        int direction = 1;
        if (random.Next(2) == 0) direction = -1;
        // Nine of ten openings ignore the roll above and simply face the nearest map edge.
        if (random.Next(10) != 0) direction = i < width / 2 ? 1 : -1;
        double x = i, y = j, vx = direction, vy = 0;
        int remaining = 100;
        while (remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            int headX = (int)x, headY = (int)y;
            if ((uint)headX >= (uint)width || (uint)headY >= (uint)height)
                throw new InvalidOperationException("Mountain cave opening walked outside the world.");
            ref WorldTile head = ref At(headX, headY);
            // Reaching a wall-less cell or an unclearable block ends the opening after this final brush stamp.
            if (head.Wall == 0 || (head.IsActive && !CanBeCleared(head.Type))) remaining = 0;
            remaining--;
            int left = (int)(x - size * .5), right = (int)(x + size * .5);
            int top = (int)(y - size * .5), bottom = (int)(y + size * .5);
            if (left < 0) left = 0;
            if (right > width) right = width;
            if (top < 0) top = 0;
            if (bottom > height) bottom = height;
            double brush = size * random.Next(80, 120) * .01;
            for (int k = left; k < right; k++)
            for (int l = top; l < bottom; l++)
            {
                double dx = Math.Abs(k - x), dy = Math.Abs(l - y);
                if (Math.Sqrt(dx * dx + dy * dy) < brush * .4 && CanBeCleared(At(k, l).Type))
                    At(k, l).Flags &= ~WorldTileFlags.Active;
            }

            x += vx;
            y += vy;
            vx += random.Next(-10, 11) * .05;
            vy += random.Next(-10, 11) * .05;
            if (vx > direction + .5) vx = direction + .5;
            if (vx < direction - .5) vx = direction - .5;
            // The opening only ever rises: vertical velocity is clamped into [-0.5, 0].
            if (vy > 0) vy = 0;
            if (vy < -.5) vy = -.5;
        }
    }

    private void Cavinator(int i, int j, int steps)
    {
        double size = random.Next(7, 15);
        int direction = 1;
        if (random.Next(2) == 0) direction = -1;
        double x = i, y = j;
        // The source draws the step budget before the initial downward velocity.
        int remaining = random.Next(20, 40);
        double vy = random.Next(10, 20) * .01, vx = direction;
        while (remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            remaining--;
            int left = (int)(x - size * .5), right = (int)(x + size * .5);
            int top = (int)(y - size * .5), bottom = (int)(y + size * .5);
            if (left < 0) left = 0;
            if (right > width) right = width;
            if (top < 0) top = 0;
            if (bottom > height) bottom = height;
            double brush = size * random.Next(80, 120) * .01;
            for (int k = left; k < right; k++)
            {
                for (int l = top; l < bottom; l++)
                {
                    double dx = Math.Abs(k - x), dy = Math.Abs(l - y);
                    if (Math.Sqrt(dx * dx + dy * dy) < brush * .4)
                    {
                        ref WorldTile tile = ref At(k, l);
                        // Touching dungeon masonry abandons the whole descent, not just this cell.
                        if ((tile.IsActive && DungeonGenerationTiles1458.IsDungeonTile(tile.Type)) ||
                            DungeonGenerationTiles1458.IsDungeonWall(tile.Wall))
                        {
                            remaining = 0;
                            break;
                        }

                        if (tile.IsActive && (!CanBeCleared(tile.Type) || tile.Type == Sand)) continue;
                        tile.Flags &= ~WorldTileFlags.Active;
                    }

                    if (remaining <= 0) break;
                }

                if (remaining <= 0) break;
            }

            x += vx;
            y += vy;
            vx += random.Next(-10, 11) * .05;
            vy += random.Next(-10, 11) * .05;
            if (vx > direction + .5) vx = direction + .5;
            if (vx < direction - .5) vx = direction - .5;
            // Unlike the opening, the descent only ever falls: vertical velocity is clamped into [0, 2].
            if (vy > 2) vy = 2;
            if (vy < 0) vy = 0;
        }

        // The source recurses from the truncated end position while it is still above rockLayer + 50.
        if (steps > 0 && (int)y < rockLayer + 50d) Cavinator((int)x, (int)y, steps - 1);
    }

    private static bool CanBeCleared(ushort type)
    {
        // The source indexes TileID.Sets.CanBeClearedDuringGeneration directly, so an out-of-range identity is a
        // generation fault rather than a silent "not clearable" answer.
        if (type >= VanillaTileIds.Count)
            throw new InvalidOperationException("Unknown tile identity in mountain cave openings.");
        return WorldSmoothingCatalog1458.CanBeClearedDuringGeneration(new TileTypeId(type));
    }

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}

