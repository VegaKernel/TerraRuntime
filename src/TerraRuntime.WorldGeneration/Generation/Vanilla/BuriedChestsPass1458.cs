using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// The two chest loops of TerrariaServer 1.4.5.8 <c>GenPassNameID.UndergroundHousesAndBuriedChests</c>: the
/// cavern chests and the underworld chests.
/// </summary>
/// <remarks>
/// <para>
/// The pass is four loops, and only these two are chests. The other two build cave houses, which are a
/// separate subsystem with its own row, so this class owns the chests and hands the two house counts back to
/// the caller rather than pretending they do not exist. It still draws all four counts, in the source's order,
/// because they come off the shared stream before any of the loops run.
/// </para>
/// <para>
/// Both loops are retry loops with a shared shape that is easy to mistake for a simple <c>for</c>: a failure
/// decrements the loop counter as well as a budget of ten thousand, so the loop keeps going until it has the
/// chests it wanted or the budget runs out. The cavern loop refuses a sample behind a dungeon wall, behind wall
/// 87, or over the ocean; the underworld loop refuses only the dungeon wall.
/// </para>
/// <para>
/// Measured against the whole official delegate on ten fixtures: the chests these two loops produce are an
/// exact prefix of the chests the whole pass produces, so the cave houses only ever append to them.
/// </para>
/// </remarks>
internal sealed class BuriedChestsPass1458(
    BuriedChest1458 chests,
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurfaceHigh,
    double rockLayer,
    double oceanLevel,
    int beachDistance,
    CancellationToken cancellation)
{
    private const int Budget = 10000;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>How many cave houses the pass asked for. Owned by the cave-house row, not by this one.</summary>
    public int CaveHouseCount { get; private set; }

    /// <summary>How many extra desert houses the pass asked for. Also the cave-house row's.</summary>
    public int AdditionalDesertHouseCount { get; private set; }

    public void Apply()
    {
        // Source Configuration.json, in the order the pass reads them. All four come off the stream before
        // any loop runs, so a port that skipped the two house counts would desynchronise immediately.
        CaveHouseCount = Range(35, 40, area: true);
        int underworldChestCount = Range(10, 15, area: false);
        int caveChestCount = Range(35, 40, area: true);
        AdditionalDesertHouseCount = Range(2, 2, area: true);

        int budget = Budget;
        for (int index = 0; index < caveChestCount; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (budget <= 0)
                break;

            int x = random.Next(20, width - 20);
            int y = random.Next((int)((worldSurfaceHigh + 20.0 + rockLayer) / 2.0), height - 230);
            ushort wall = At(x, y).Wall;
            if (DungeonGenerationTiles1458.IsDungeonWall(wall) || wall == 87 || OceanDepths(x, y))
            {
                budget--;
                index--;
            }
            else if (!chests.TryAdd(x, y, out _, out _))
            {
                budget--;
                index--;
            }
        }

        budget = Budget;
        for (int index = 0; index < underworldChestCount; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (budget <= 0)
                break;

            int x = random.Next(20, width - 20);
            int y = random.Next(height - 200, height - 50);
            if (DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall))
            {
                budget--;
                index--;
            }
            else if (!chests.TryAdd(x, y, out _, out _))
            {
                budget--;
                index--;
            }
        }
    }

    /// <summary>
    /// Source <c>WorldGenRange.GetRandom</c>: the bounds are scaled by the world's area or its width against
    /// a large world, truncated, and then sampled inclusively.
    /// </summary>
    private int Range(int minimum, int maximum, bool area)
    {
        double scale = area ? width * (double)height / 5040000.0 : width / 4200.0;
        return random.Next((int)(scale * minimum), (int)(scale * maximum) + 1);
    }

    private bool OceanDepths(int x, int y) =>
        y <= oceanLevel && (x < beachDistance || x > width - beachDistance);

    private WorldTile At(int x, int y) => store.Get(x, y);
}
