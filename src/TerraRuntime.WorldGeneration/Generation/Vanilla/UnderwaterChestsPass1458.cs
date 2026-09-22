using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.UnderwaterChests</c>: the water chests, both the ones
/// the ocean caves asked for and the ones scattered through every other body of water in the world.
/// </summary>
/// <remarks>
/// <para>
/// Two parts that share nothing but the chest style. The first walks the ocean-cave treasure points the earlier
/// ocean pass left behind and spirals outward from each: a radius that starts at two and grows by a tenth every
/// attempt, sampling a box that widens with it, and then shifting the sample sideways by half the radius -
/// away from the map's edge on the right, toward it everywhere else. It gives up at radius fifty, which is
/// four hundred and eighty attempts.
/// </para>
/// <para>
/// The second places two chests per iteration of a nine-per-standard-width loop, and the two differ only in the
/// vertical band they sample: the first may land anywhere from the first row down to the underworld, the second
/// only below the surface. Each one re-rolls its sample until it finds a cell with more than 250 water in it -
/// and the re-roll for the first uses a DIFFERENT lower bound than its opening sample, so a chest that started
/// its search above row fifty can never return there.
/// </para>
/// <para>
/// The signature item walks a four-step cycle - Trident, Sea Shell, Breathing Reed, Flipper - except that one
/// iteration in ten replaces it with a Water Walking Boots and leaves the cycle where it was. Both of the
/// iteration's chests get the same item.
/// </para>
/// </remarks>
internal sealed class UnderwaterChestsPass1458(
    BuriedChest1458 chests,
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    int beachDistance,
    IReadOnlyList<(int X, int Y)> oceanCaveTreasure,
    CancellationToken cancellation)
{
    private static readonly int[] CaveTreasure = [863, 186, 277, 187, 4404];

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    private int UnderworldLayer => height - 200;

    public void Apply()
    {
        foreach ((int treasureX, int treasureY) in oceanCaveTreasure)
        {
            cancellation.ThrowIfCancellationRequested();
            int primary = CaveTreasure[random.Next(CaveTreasure.Length)];
            bool placed = false;
            double radius = 2.0;
            while (!placed && radius < 50.0)
            {
                radius += 0.1;
                int x = random.Next(treasureX - (int)radius, treasureX + (int)radius + 1);
                int y = random.Next(treasureY - (int)radius / 2, treasureY + (int)radius / 2 + 1);
                // The shift is away from the edge only when the sample already ran off the right side of the
                // map, which cannot happen for a treasure point the ocean pass placed; everywhere else it
                // pulls the sample left.
                x = x >= width ? (int)(x + radius / 2.0) : (int)(x - radius / 2.0);
                if (InWorld(x, y) && At(x, y).LiquidAmount > 250 && At(x, y).LiquidKind == WorldLiquidKind.Water)
                    placed = chests.TryAdd(x, y, out _, out _, primary, notNearOtherChests: false, 17,
                        trySlope: true, 0);
            }
        }

        int cycle = 0;
        double scale = width / 4200.0;
        for (int index = 0; index < 9.0 * scale; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            cycle++;
            int primary;
            if (random.Next(10) == 0)
            {
                primary = 863;
            }
            else
            {
                switch (cycle)
                {
                    case 1: primary = 186; break;
                    case 2: primary = 4404; break;
                    case 3: primary = 277; break;
                    default: primary = 187; cycle = 0; break;
                }
            }

            // The first of the pair opens at row one and re-rolls from fifty; the second uses the surface
            // for both. That asymmetry is in the source, not a simplification of it.
            Scatter(primary, openMinimumY: 1, rerollMinimumY: 50);
            Scatter(primary, openMinimumY: (int)worldSurface, rerollMinimumY: (int)worldSurface);
        }
    }

    /// <summary>One scattered water chest, re-rolled until it finds water.</summary>
    private void Scatter(int primary, int openMinimumY, int rerollMinimumY)
    {
        bool placed = false;
        int attempts = 0;
        while (!placed)
        {
            cancellation.ThrowIfCancellationRequested();
            int x = random.Next(50, width - 50);
            int y = random.Next(openMinimumY, UnderworldLayer);
            while (At(x, y).LiquidAmount < 250 || At(x, y).LiquidKind != WorldLiquidKind.Water)
            {
                cancellation.ThrowIfCancellationRequested();
                x = random.Next(50, width - 50);
                y = random.Next(rerollMinimumY, UnderworldLayer);
            }

            placed = chests.TryAdd(x, y, out _, out _, primary, notNearOtherChests: false, 17,
                x < beachDistance || x > width - beachDistance, 0);
            attempts++;
            if (attempts > 10000)
                break;
        }
    }

    private bool InWorld(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

    private WorldTile At(int x, int y) => InWorld(x, y) ? store.Get(x, y) : default;
}
