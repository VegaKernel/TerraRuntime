using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.Piles</c>: the small and large piles that cover
/// almost every floor in the world.
/// </summary>
/// <remarks>
/// <para>
/// Seven stages, each an independent sample-and-cascade loop with its own band, its own style table and its
/// own budget, and each retrying up to half the map's width before it gives up on one pile. They are not
/// variations on one another: the bands overlap, the tables disagree, and the same floor material can give a
/// different style in two of them.
/// </para>
/// <para>
/// Every stage samples a point, and if the cell is empty walks DOWN until it finds ground - so a pile lands on
/// the first floor below where the sample happened to fall, not at the sample. The style is then decided by a
/// cascade of separate <c>if</c> statements over the floor's material, the wall behind it and the depth, and
/// because they are separate rather than chained, a floor matching two of them pays for both draws and keeps
/// the last.
/// </para>
/// <para>
/// The pass also clears eleven identities out of <c>Main.tileSolid</c> for its own duration, so a pile offered
/// onto a cloud, a sunplate or a bubble is refused while it runs. That is measured: a world whose every floor
/// is one of those eleven grows not one pile of any size. The epilogue then restores a DIFFERENT set, and the
/// net effect is that exactly one identity - 229 - is left non-solid for every pass that follows.
/// </para>
/// </remarks>
internal sealed class PilePass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double rockLayer,
    int beachDistance,
    CancellationToken cancellation)
{
    private const ushort LargePiles = 186;
    private const ushort LargePiles2 = 187;
    private const ushort SmallPiles = 185;

    /// <summary>
    /// Source's prologue clears these out of <c>Main.tileSolid</c> for the pass's duration, which is what a
    /// pile's floor test reads.
    /// </summary>
    private static readonly HashSet<ushort> NotSolid =
        [379, 229, 190, 196, 189, 717, 718, 719, 202, 460, 484];

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;
    private readonly double oceanLevel = (worldSurface + rockLayer) / 2.0 + 40.0;

    /// <summary>How many piles of any size were taken.</summary>
    public long Placed { get; private set; }

    public void Apply()
    {
        LargeOnGround();
        LargeInUnderworld();
        LargeOnSurface();
        LargeBehindWalls();
        SmallEverywhere();
        SmallOnSurface();
        SmallBehindDirtWalls();
    }

    /// <summary>
    /// Every stage's budget is a FLOAT compared against the loop counter, not an integer count, and the
    /// difference is real: six tenths of 1400 is 840.0000333 in single precision and fifteen hundredths of it
    /// is 210.0000083, so those two stages run one more time than an integer cast would give them.
    /// Source <c>GetPileGenerationAttempts</c> gives every stage the same retry budget.
    /// </summary>
    private int Attempts() => width / 2;

    /// <summary>Stage one: large piles from the surface down to the underworld's ceiling.</summary>
    private void LargeOnGround()
    {
        float wanted = width * 0.06f;
        for (int i = 0; i < wanted; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = Attempts();
            bool done = false;
            while (!done && attempts > 0)
            {
                attempts--;
                int x = random.Next(25, width - 25);
                int y = random.Next((int)worldSurface, height - 300);
                while (OceanDepths(x, y))
                {
                    x = random.Next(25, width - 25);
                    y = random.Next((int)worldSurface, height - 300);
                }

                if (At(x, y).IsActive)
                    continue;

                ushort type = LargePiles;
                y = Descend(x, y);
                ushort wall = At(x, y).Wall;
                ushort floor = At(x, y + 1).Type;
                if (!At(x, y + 1).IsActive)
                    continue;

                int style = random.Next(22);
                if (style is >= 16 and <= 22)
                    style = random.Next(22);
                if ((floor is 0 or 1 || IsMoss(floor)) && random.Next(5) == 0)
                {
                    style = random.Next(23, 29);
                    type = LargePiles2;
                }

                if (y > height - 300 || IsDungeonWall(wall) || floor is 30 or 19 or 25 or 203)
                {
                    style = random.Next(7);
                    type = LargePiles;
                }

                if (floor is 147 or 161 or 162)
                {
                    style = random.Next(26, 32);
                    type = LargePiles;
                }

                if (floor == 60)
                {
                    type = LargePiles2;
                    style = random.Next(6);
                }

                if (floor is 57 or 58 && random.Next(3) < 2)
                {
                    type = LargePiles2;
                    style = random.Next(6, 9);
                }

                if (floor == 226)
                {
                    type = LargePiles2;
                    style = random.Next(18, 23);
                }

                if (floor == 70)
                {
                    style = random.Next(32, 35);
                    type = LargePiles;
                }

                if (floor is 396 or 397 or 404)
                {
                    style = random.Next(29, 35);
                    type = LargePiles2;
                }

                if (floor == 368)
                {
                    style = random.Next(35, 41);
                    type = LargePiles2;
                }

                if (floor == 367)
                {
                    style = random.Next(41, 47);
                    type = LargePiles2;
                }

                // One in seventy-five ordinary piles becomes the single detritus style 17 instead.
                if (type == LargePiles && style is >= 7 and <= 15 && random.Next(75) == 0)
                {
                    type = LargePiles2;
                    style = 17;
                }

                if (IsDungeonWall(wall) && random.Next(3) != 0)
                {
                    // A dungeon wall usually ends the attempt without placing anything at all, and the pile
                    // is counted as done anyway.
                    done = true;
                    continue;
                }

                if (!AnyShimmer(x, y))
                    PlaceLarge(x, y, type, style);
                if (At(x, y).Type is LargePiles or LargePiles2)
                    done = true;
                if (done && type == LargePiles && style <= 7)
                    Scatter(x, y);
            }
        }
    }

    /// <summary>Stage two: large piles in the underworld, with a much shorter cascade.</summary>
    private void LargeInUnderworld()
    {
        float wanted = width * 0.01f;
        for (int i = 0; i < wanted; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = Attempts();
            bool done = false;
            while (!done && attempts > 0)
            {
                attempts--;
                int x = random.Next(25, width - 25);
                int y = random.Next(height - 300, height - 10);
                if (At(x, y).IsActive)
                    continue;

                ushort type = LargePiles;
                y = Descend(x, y);
                ushort wall = At(x, y).Wall;
                if (!At(x, y + 1).IsActive)
                    continue;

                ushort floor = At(x, y + 1).Type;
                int style = random.Next(22);
                if (style is >= 16 and <= 22)
                    style = random.Next(22);
                if (y > height - 300 || IsDungeonWall(wall) || floor is 30 or 19)
                    style = random.Next(7);
                if (floor is 57 or 58 && random.Next(3) < 2)
                {
                    type = LargePiles2;
                    style = random.Next(6, 9);
                }

                if (floor is 147 or 161 or 162)
                    style = random.Next(26, 32);

                PlaceLarge(x, y, type, style);
                if (At(x, y).Type is LargePiles or LargePiles2)
                    done = true;
                if (done && type == LargePiles && style <= 7)
                    Scatter(x, y);
            }
        }
    }

    /// <summary>Stage three: large piles above the surface line. A style of minus one means place nothing.</summary>
    private void LargeOnSurface()
    {
        float wanted = width * 0.03f;
        for (int i = 0; i < wanted; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = Attempts();
            bool done = false;
            while (!done && attempts > 0)
            {
                attempts--;
                ushort type = LargePiles;
                int x = random.Next(25, width - 25);
                int y = random.Next(10, (int)worldSurface);
                while (OceanDepths(x, y))
                {
                    x = random.Next(25, width - 25);
                    y = random.Next(10, (int)worldSurface);
                }

                if (At(x, y).IsActive)
                    continue;

                y = Descend(x, y);
                ushort wall = At(x, y).Wall;
                if (!At(x, y + 1).IsActive)
                    continue;

                ushort floor = At(x, y + 1).Type;
                int style = random.Next(7, 13);
                if (y > height - 300 || IsDungeonWall(wall) ||
                    floor is 30 or 19 or 25 or 204 or 234 or 112 || IsDungeonTile(floor))
                {
                    style = -1;
                }

                if (floor is 147 or 161 or 162)
                    style = random.Next(26, 32);
                if (floor == 53)
                {
                    type = LargePiles2;
                    style = random.Next(52, 55);
                }

                if (floor == 2 || Grass(x - 1, y + 1) || Grass(x + 1, y + 1))
                {
                    type = LargePiles2;
                    style = random.Next(14, 17);
                }

                if (floor is 151 or 274)
                {
                    type = LargePiles;
                    style = random.Next(7);
                }

                if (style >= 0)
                    PlaceLarge(x, y, type, style);
                if (At(x, y).Type == type)
                    done = true;
            }
        }
    }

    /// <summary>Stage four: the same band again, but only where a wall stands behind the cell.</summary>
    private void LargeBehindWalls()
    {
        float wanted = width * 0.0035f;
        for (int i = 0; i < wanted; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = Attempts();
            bool done = false;
            while (!done && attempts > 0)
            {
                attempts--;
                int x = random.Next(25, width - 25);
                int y = random.Next(10, (int)worldSurface);
                if (At(x, y).IsActive || At(x, y).Wall == 0)
                    continue;

                ushort type = LargePiles;
                y = Descend(x, y);
                ushort wall = At(x, y).Wall;
                if (!At(x, y + 1).IsActive)
                    continue;

                ushort floor = At(x, y + 1).Type;
                int style = random.Next(7, 13);
                if (y > height - 300 || IsDungeonWall(wall) || floor is 30 or 19 || IsDungeonTile(floor))
                    style = -1;
                if (floor == 25)
                    style = random.Next(7);
                if (floor is 147 or 161 or 162)
                    style = random.Next(26, 32);
                if (floor == 2 || Grass(x - 1, y + 1) || Grass(x + 1, y + 1))
                {
                    type = LargePiles2;
                    style = random.Next(14, 17);
                }

                if (floor is 151 or 274)
                {
                    type = LargePiles;
                    style = random.Next(7);
                }

                if (style >= 0)
                    PlaceLarge(x, y, type, style);
                if (At(x, y).Type == type)
                    done = true;
                if (done && style <= 7)
                    Scatter(x, y);
            }
        }
    }

    /// <summary>
    /// Stage five, and by far the largest: six tenths of the map's width in attempts at SMALL piles, anywhere
    /// from the surface to the very bottom. This stage is the small pile population on its own.
    /// </summary>
    private void SmallEverywhere()
    {
        float wanted = width * 0.6f;
        for (int i = 0; i < wanted; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = Attempts();
            bool done = false;
            while (!done && attempts > 0)
            {
                attempts--;
                int x = random.Next(25, width - 25);
                int y = random.Next((int)worldSurface, height - 20);

                // A single re-roll, not a loop, and only half the time: a mud wall gets one more chance to
                // land somewhere else and is then accepted wherever it falls.
                if (At(x, y).Wall == 87 && random.Next(2) == 0)
                {
                    x = random.Next(25, width - 25);
                    y = random.Next((int)worldSurface, height - 20);
                }

                while (OceanDepths(x, y))
                {
                    x = random.Next(25, width - 25);
                    y = random.Next((int)worldSurface, height - 20);
                }

                if (At(x, y).IsActive)
                    continue;

                y = Descend(x, y);
                WorldTile anchor = At(x, y);
                if (!At(x, y + 1).IsActive)
                    continue;

                ushort floor = At(x, y + 1).Type;
                int size = random.Next(2);
                int style = random.Next(36);
                if (style is >= 28 and <= 35)
                    style = random.Next(36);
                if (size == 1)
                {
                    style = random.Next(25);
                    if (style is >= 16 and <= 24)
                        style = random.Next(25);
                }

                if (y > height - 300)
                {
                    if (size == 0)
                        style = random.Next(12, 28);
                    if (size == 1)
                        style = random.Next(6, 16);
                }

                // These shift the style within its table rather than replacing it, which is why the order of
                // the tests and the exact bounds both matter.
                if (IsDungeonWall(anchor.Wall) || anchor.Wall == 87 || floor is 30 or 19 or 25 or 203)
                {
                    if (size == 0 && style < 12)
                        style += 12;
                    if (size == 1 && style < 6)
                        style += 6;
                    if (size == 1 && style >= 17)
                        style -= 10;
                }

                if (floor is 147 or 161 or 162)
                {
                    if (size == 0 && style < 12)
                        style += 36;
                    if (size == 1 && style >= 20)
                        style += 6;
                    if (size == 1 && style < 6)
                        style += 25;
                }

                if (anchor.LiquidAmount <= 0 && floor is 53 or 397 or 396)
                {
                    if (size == 0)
                        style = random.Next(73, 78);
                    if (size == 1)
                        style = random.Next(62, 65);
                }

                if (floor is 151 or 274)
                {
                    if (size == 0)
                        style = random.Next(12, 28);
                    if (size == 1)
                        style = random.Next(12, 19);
                }

                if (floor == 368)
                {
                    if (size == 0)
                        style = random.Next(60, 66);
                    if (size == 1)
                        style = random.Next(47, 53);
                }

                if (floor == 367)
                {
                    if (size == 0)
                        style = random.Next(66, 72);
                    if (size == 1)
                        style = random.Next(53, 59);
                }

                if (IsDungeonTile(floor))
                    done = false;
                else if (IsDungeonWall(anchor.Wall) && random.Next(3) != 0)
                    done = true;
                else if (!AnyShimmer(x, y))
                    done = PlaceSmall(x, y, style, size);

                if (done && size == 1 && style is >= 6 and <= 15)
                    Scatter(x, y);
            }
        }
    }

    /// <summary>Stage six: small piles above the surface line, with a long list of floors it refuses.</summary>
    private void SmallOnSurface()
    {
        float wanted = width * 0.02f;
        for (int i = 0; i < wanted; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = Attempts();
            bool done = false;
            while (!done && attempts > 0)
            {
                attempts--;
                int x = random.Next(25, width - 25);
                int y = random.Next(15, (int)worldSurface);
                while (OceanDepths(x, y))
                {
                    x = random.Next(25, width - 25);
                    y = random.Next(15, (int)worldSurface);
                }

                if (At(x, y).IsActive)
                    continue;

                y = Descend(x, y);
                WorldTile anchor = At(x, y);
                if (!At(x, y + 1).IsActive)
                    continue;

                ushort floor = At(x, y + 1).Type;
                int size = random.Next(2);
                int style = random.Next(11);
                if (size == 1)
                    style = random.Next(5);
                style = SurfaceStyle(in anchor, floor, size, style);
                if (!RefusedOnSurface(in anchor, floor))
                    done = PlaceSmall(x, y, style, size);
            }
        }
    }

    /// <summary>Stage seven: the same again, but only behind an ordinary dirt or mud wall.</summary>
    private void SmallBehindDirtWalls()
    {
        float wanted = width * 0.15f;
        for (int i = 0; i < wanted; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = Attempts();
            bool done = false;
            while (!done && attempts > 0)
            {
                attempts--;
                int x = random.Next(25, width - 25);
                int y = random.Next(15, (int)worldSurface);
                if (At(x, y).IsActive || At(x, y).Wall is not (2 or 40))
                    continue;

                y = Descend(x, y);
                WorldTile anchor = At(x, y);
                if (!At(x, y + 1).IsActive)
                    continue;

                ushort floor = At(x, y + 1).Type;
                int size = random.Next(2);
                int style = random.Next(11);
                if (size == 1)
                    style = random.Next(5);
                style = SurfaceStyle(in anchor, floor, size, style);

                // One extra refusal this stage has and the previous one does not: a fully flooded unpapered
                // cell over sand.
                bool drowned = anchor.LiquidAmount == byte.MaxValue && floor == 53 && anchor.Wall == 0;
                if (!drowned && !RefusedOnSurface(in anchor, floor))
                    done = PlaceSmall(x, y, style, size);
            }
        }
    }

    /// <summary>The style cascade the last two stages share.</summary>
    private int SurfaceStyle(in WorldTile anchor, ushort floor, int size, int style)
    {
        if (floor is 147 or 161 or 162)
        {
            if (size == 0 && style < 12)
                style += 36;
            if (size == 1 && style >= 20)
                style += 6;
            if (size == 1 && style < 6)
                style += 25;
        }

        if (anchor.LiquidAmount <= 0 && floor is 53 or 397 or 396)
        {
            if (size == 0)
                style = random.Next(73, 77);
            if (size == 1)
                style = random.Next(62, 65);
        }

        if (floor == 2 && size == 1)
            style = random.Next(38, 41);

        if (floor is 151 or 274)
        {
            if (size == 0)
                style = random.Next(12, 28);
            if (size == 1)
                style = random.Next(12, 19);
        }

        return style;
    }

    private bool RefusedOnSurface(in WorldTile anchor, ushort floor) =>
        IsDungeonWall(anchor.Wall) ||
        floor is 30 or 19 or 41 or 43 or 44 or 481 or 482 or 483 or 45 or 46 or 47 or 175 or 176 or 177 or
            25 or 203 ||
        IsDungeonTile(floor);

    /// <summary>
    /// The burst of one to four extra single-cell piles a large pile throws around itself. Each one samples
    /// its own column and row offset, walks down to ground and is offered whatever it lands on.
    /// </summary>
    private void Scatter(int x, int y)
    {
        int count = random.Next(1, 5);
        for (int i = 0; i < count; i++)
        {
            int px = x + random.Next(-10, 11);
            int py = y - random.Next(5);
            if (!Contains(px, py) || At(px, py).IsActive)
                continue;

            py = Descend(px, py);
            PlaceSmall(px, py, random.Next(12, 36), 0);
        }
    }

    /// <summary>Source's descent: fall until the cell below is occupied, or the world runs out.</summary>
    private int Descend(int x, int y)
    {
        while (y < height - 5 && !At(x, y + 1).IsActive)
            y++;
        return y;
    }

    private void PlaceLarge(int x, int y, ushort type, int style)
    {
        if (!Contains(x, y))
            return;

        if (GenerationDecorationPlacement1458.TryPlaceTile3x2(store, random, x, y, type, style, NotSolid))
            Placed++;
    }

    private bool PlaceSmall(int x, int y, int style, int size)
    {
        if (!Contains(x, y))
            return false;

        bool placed = GenerationDecorationPlacement1458.TryPlaceSmallPile(store, x, y, style, size, NotSolid);
        if (placed)
            Placed++;
        return placed;
    }

    private bool Grass(int x, int y) => Contains(x, y) && At(x, y).IsActive && At(x, y).Type == 2;

    private bool AnyShimmer(int x, int y)
    {
        WorldTile cell = At(x, y);
        return cell.LiquidAmount > 0 && cell.LiquidKind == WorldLiquidKind.Shimmer;
    }

    /// <summary>Source <c>Main.tileMoss</c>.</summary>
    private static bool IsMoss(ushort type) =>
        type is 179 or 180 or 181 or 182 or 183 or 381 or 534 or 536 or 539 or 625 or 627;

    private static bool IsDungeonWall(ushort wall) => DungeonGenerationTiles1458.IsDungeonWall(wall);

    private static bool IsDungeonTile(ushort type) => DungeonGenerationTiles1458.IsDungeonTile(type);

    /// <summary>Source <c>WorldGen.oceanDepths</c>: above the ocean's floor line and outside the beaches.</summary>
    private bool OceanDepths(int x, int y) =>
        y <= oceanLevel && (x < beachDistance || x > width - beachDistance);

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
