using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.PotsGraveyardsAndBoulderPiles</c>, whose ordinary
/// path is the pot scatter. The teleporters, the graveyards and the extra boulders that share the
/// registration all sit behind secret-seed and world flags an ordinary generation never sets.
/// </summary>
/// <remarks>
/// <para>
/// One pot per attempt, and the attempt is a column walk rather than a point test. The pass drops a random
/// column, starts at a random row, and walks DOWN until it meets a block with clear air over it; from the row
/// after that it offers a two-by-two pot on every row it passes, and stops the column the moment one lands.
/// A pot therefore ends up on the first ledge below where the walk happened to start, not at the sampled
/// point.
/// </para>
/// <para>
/// Where the walk starts is not uniform. Most of the run draws it, but once the pass is three quarters done it
/// starts every walk at the surface line, and past ninety-three percent it starts them all a hundred and fifty
/// rows off the bottom - so the last seventh of the pots are deliberately crowded into the underworld and the
/// last quarter onto the surface. That is a distribution, not a fallback.
/// </para>
/// <para>
/// The style is the part worth porting carefully. A base style is drawn FIRST, before the pass has even
/// checked that the cell below is solid, so a row that is walked past but refused still costs a draw. Then
/// eight independent tests each re-draw it from their own range - and they are separate statements rather than
/// a chain, so a pot on snow below the underworld line draws twice and keeps the second. The order of those
/// statements is the tie-break, and the underworld test is last, which is why it wins over every material.
/// </para>
/// <para>
/// The two-by-two footprint occupies the two rows ABOVE the row the offer names and stands on the row below
/// it, so the row a pot is offered at is one lower than the ledge it ends up sitting on. Its own horizontal
/// variant is drawn inside the placement and only once the footprint has been accepted, so a refused offer
/// costs the style draw but not the variant draw.
/// </para>
/// </remarks>
internal sealed class PotScatterPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double worldSurfaceHigh,
    double worldSurfaceLow,
    double rockLayer,
    int beachDistance,
    CancellationToken cancellation)
{
    private const ushort Pot = 28;
    private const int AttemptCap = 10000;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;
    private readonly double oceanLevel = (worldSurface + rockLayer) / 2.0 + 40.0;
    private readonly int underworldLayer = store.Dimensions.HeightTiles - 200;

    /// <summary>How many pots were placed.</summary>
    public long Placed { get; private set; }

    public void Apply()
    {
        double target = (double)(width * height) * 0.0008;
        for (int i = 0; i < target; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            double progress = i / target;
            bool done = false;
            int attempts = 0;
            while (!done)
            {
                int startY = random.Next((int)worldSurfaceHigh, height - 10);
                if (progress > 0.93)
                    startY = height - 150;
                else if (progress > 0.75)
                    startY = (int)worldSurfaceLow;

                int x = random.Next(20, width - 20);
                if (WalkColumn(x, startY))
                {
                    done = true;
                    break;
                }

                attempts++;
                if (attempts >= AttemptCap)
                    break;
            }
        }
    }

    /// <summary>
    /// Walks the column down from <paramref name="startY"/>, finds the first block with clear air over it, and
    /// offers a pot on every row after that until one lands.
    /// </summary>
    private bool WalkColumn(int x, int startY)
    {
        bool onFloor = false;
        for (int y = startY; y < height - 20; y++)
        {
            if (!onFloor)
            {
                // The block that starts the walk is refused if what sits on it is lava or shimmer, and the
                // walk then keeps looking further down for another one.
                WorldTile here = At(x, y);
                if (here.IsActive && VanillaTileCollisionCatalog.IsSolid(here.TileType) &&
                    !AnyLava(x, y - 1) && !AnyShimmer(x, y - 1))
                {
                    onFloor = true;
                }

                continue;
            }

            // Above the surface line a pot needs a wall behind it; below it, none is required.
            if (y < worldSurface && At(x, y).Wall == 0)
                continue;

            // Drawn before the cell below has even been looked at, so a row that is refused next still costs
            // this value.
            int style = random.Next(0, 4);
            WorldTile below = At(x, y + 1);
            if (!below.IsActive || OceanDepths(x, y) || AnyShimmer(x, y) || AnyLava(x, y))
                continue;

            style = Restyle(x, y, below, style);
            if (PlacePot(x, y, style))
            {
                Placed++;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The style cascade. Eight separate statements, not a chain: each one that matches spends a draw and
    /// overwrites what came before, so the last match wins and the earlier draws are paid for anyway.
    /// </summary>
    private int Restyle(int x, int y, WorldTile below, int style)
    {
        ushort floor = below.Type;
        ushort wall = At(x, y).Wall;

        if (floor is 147 or 161 or 162)
            style = random.Next(4, 7);
        if (floor == 60)
            style = random.Next(7, 10);
        if (IsDungeonWall(wall) || floor is 41 or 43 or 44 or 481 or 482 or 483 ||
            IsDungeonPlatformOrShelf(below))
        {
            style = random.Next(10, 13);
        }

        if (floor is 23 or 25 or 22 or 163)
            style = random.Next(16, 19);
        if (floor is 199 or 203 or 204 or 200)
            style = random.Next(22, 25);
        if (floor == 367)
            style = random.Next(31, 34);
        if (floor == 226)
            style = random.Next(28, 31);
        if (wall is 187 or 216 or 223)
            style = random.Next(34, 37);
        if (y > underworldLayer)
            style = random.Next(13, 16);

        return style;
    }

    /// <summary>
    /// Source <c>WorldGen.PlacePot</c>. The footprint is the two rows ABOVE the given row, which must both be
    /// clear, standing on the row below it, which must be an unactuated flat solid block. The horizontal
    /// variant is drawn only once that has been accepted.
    /// </summary>
    private bool PlacePot(int x, int y, int style)
    {
        for (int column = x; column < x + 2; column++)
        {
            for (int row = y - 1; row < y + 1; row++)
            {
                if (!Contains(column, row) || At(column, row).IsActive)
                    return false;
            }

            if (!Contains(column, y + 1))
                return false;

            WorldTile support = At(column, y + 1);
            if (!support.IsActive || support.IsActuated || support.Shape != 0 ||
                !VanillaTileCollisionCatalog.IsSolid(support.TileType))
            {
                return false;
            }
        }

        int variant = random.Next(3) * 36;
        for (int k = 0; k < 2; k++)
        for (int l = -1; l < 1; l++)
        {
            ref WorldTile cell = ref At(x + k, y + l);
            cell.Flags |= WorldTileFlags.Active;
            cell.FrameX = (short)(k * 18 + variant);
            cell.FrameY = (short)((l + 1) * 18 + style * 36);
            cell.Type = Pot;
            // Source clears the half brick alone and leaves a slope where it is; an accepted footprint is
            // inactive anyway, so neither can be set.
            if (cell.Shape == 1)
                cell.Shape = 0;
        }

        return true;
    }

    /// <summary>Source <c>WorldGen.IsDungeonPlatformOrShelf</c>: a platform in one of the dungeon frames.</summary>
    private static bool IsDungeonPlatformOrShelf(WorldTile tile)
    {
        if (!tile.IsActive || tile.Type != 19)
            return false;

        int frame = tile.FrameY / 18;
        return frame is 6 or 7 or 8 or (>= 9 and <= 12);
    }

    /// <summary>Source <c>Main.wallDungeon</c>.</summary>
    private static bool IsDungeonWall(ushort wall) =>
        wall is 7 or 8 or 9 or 94 or 95 or 96 or 97 or 98 or 99;

    private bool AnyLava(int x, int y) =>
        Contains(x, y) && At(x, y).LiquidAmount > 0 && At(x, y).LiquidKind == WorldLiquidKind.Lava;

    private bool AnyShimmer(int x, int y) =>
        Contains(x, y) && At(x, y).LiquidAmount > 0 && At(x, y).LiquidKind == WorldLiquidKind.Shimmer;

    /// <summary>Source <c>WorldGen.oceanDepths</c>: above the ocean's floor line and outside the beaches.</summary>
    private bool OceanDepths(int x, int y) =>
        y <= oceanLevel && (x < beachDistance || x > width - beachDistance);

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
