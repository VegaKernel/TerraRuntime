using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.SurfaceChests</c>: the wooden chests buried under the
/// surface, and the living-wood chests inside the trees.
/// </summary>
/// <remarks>
/// <para>
/// One chest per two hundred tiles of width, each retried up to two thousand times. A retry samples a point in
/// the surface band, re-rolls it while it is over the ocean, and then asks a question that depends on what it
/// found. An EMPTY cell is admitted only if the wall behind it is dirt, flower or living wood; an OCCUPIED one
/// sends the attempt looking for a living tree instead.
/// </para>
/// <para>
/// That search is the expensive half. It sweeps a hundred-and-one-tile square at every other row and column and
/// reservoir-samples the living-wood cells it finds: the first candidate is always taken, the second replaces it
/// one time in two, the third one time in three, and so on. Every candidate costs a value whether or not it
/// wins, so a tree-filled square is worth hundreds of draws and a square with no living wood at all is worth
/// none.
/// </para>
/// <para>
/// A chest found that way is placed as a living-wood chest and any other as an ordinary one, and both refuse to
/// land within twenty-five tiles of another chest.
/// </para>
/// </remarks>
internal sealed class SurfaceChestsPass1458(
    BuriedChest1458 chests,
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double worldSurfaceLow,
    double oceanLevel,
    int beachDistance,
    CancellationToken cancellation)
{
    private const int Attempts = 2000;
    private const ushort LivingWoodWall = 244;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    public void Apply()
    {
        double wanted = width * 0.005;
        for (int index = 0; index < (int)wanted; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            bool done = false;
            int attempt = 0;
            while (!done)
            {
                int x = random.Next(200, width - 200);
                int y = random.Next((int)worldSurfaceLow, (int)worldSurface);
                // The re-roll narrows the horizontal band from two hundred to three hundred, so a sample that
                // started over the ocean can never come back to the same place.
                while (OceanDepths(x, y))
                {
                    x = random.Next(300, width - 300);
                    y = random.Next((int)worldSurfaceLow, (int)worldSurface);
                }

                bool livingWood = false;
                bool admitted = false;
                if (!At(x, y).IsActive)
                {
                    if (At(x, y).Wall is 2 or 59 or LivingWoodWall)
                    {
                        if (At(x, y).Wall == LivingWoodWall)
                            livingWood = true;
                        admitted = true;
                    }
                }
                else
                {
                    // The square is anchored on where the sample landed, NOT on the running result: the
                    // source snapshots the centre before the sweep, so a candidate that wins does not drag
                    // the remaining bounds along with it.
                    int centreX = x;
                    int centreY = y;
                    int weight = 1;
                    for (int scanX = centreX - 50; scanX <= centreX + 50; scanX += 2)
                    {
                        for (int scanY = centreY - 50; scanY <= centreY + 50; scanY += 2)
                        {
                            if (!Contains(scanX, scanY) || scanY >= worldSurface)
                                continue;
                            if (At(scanX, scanY).IsActive || At(scanX, scanY).Wall != LivingWoodWall)
                                continue;
                            if (random.Next(weight) != 0)
                                continue;

                            livingWood = true;
                            weight++;
                            admitted = true;
                            x = scanX;
                            y = scanY;
                        }
                    }
                }

                if (admitted && chests.TryAdd(x, y, out _, out _, 0, notNearOtherChests: true,
                        livingWood ? 12 : -1, trySlope: false, 0))
                {
                    done = true;
                }
                else
                {
                    attempt++;
                    if (attempt >= Attempts)
                        done = true;
                }
            }
        }
    }

    private bool OceanDepths(int x, int y) =>
        y <= oceanLevel && (x < beachDistance || x > width - beachDistance);

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private WorldTile At(int x, int y) => store.Get(x, y);
}
