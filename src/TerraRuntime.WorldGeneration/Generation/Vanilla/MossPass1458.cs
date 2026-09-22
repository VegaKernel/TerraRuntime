using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.MossAndMossCaves</c>, which is six stages under one
/// registration and the largest single contributor of moss in an ordinary world.
/// </summary>
/// <remarks>
/// <para>
/// The world does not have one moss. A roll at the top of the pass picks three DIFFERENT ordinary mosses and
/// one neon moss, and the three ordinary ones are then assigned to the left, middle and right thirds of the
/// map by position, so which moss a cave takes is decided by where it is, not by what it is. The rejection
/// loops that keep the three distinct are draws, and a port that picks one moss spends the wrong number.
/// </para>
/// <para>
/// The stages, in order and all of them spending from the same stream: a pair of neon moss biomes that are
/// drunken walks refusing any site whose hundred-and-one-square box carries a foreign material; a run of
/// flood-measured caves that take moss wall and moss stone together; a run of single stone cells; a run of
/// EXPOSED stone cells whose budget only counts a success; and a lava moss budget whose three different
/// decrements are the subtlest thing in the pass. A whole-world scan then spreads every moss tile into its
/// four neighbours, which is where most of the final moss actually comes from.
/// </para>
/// <para>
/// That lava budget is worth spelling out. Every iteration takes a thousandth off it whatever happens; an
/// exposed stone cell that fails the lava census takes a further two thousandths; and only a cell that passes
/// takes a whole unit. So on a map with no lava the loop still terminates, just slowly, and the count of lava
/// moss is not the count of iterations.
/// </para>
/// <para>
/// The shimmer refusal guards the three middle stages and neither the neon biomes nor the lava moss, and
/// inside the flood stage it guards only the FIRST sample - the retries that follow are not re-checked.
/// </para>
/// </remarks>
internal sealed class MossPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double rockLayer,
    int waterLine,
    int lavaLine,
    double shimmerX,
    double shimmerY,
    CancellationToken cancellation)
{
    private const int ShimmerSafetyDistance = 150;
    private const int CaveTileCeiling = 2500;
    private const int CaveRetryCap = 1000;
    private const ushort Stone = 1;
    private const ushort LavaMoss = 381;

    /// <summary>Source <c>randMoss</c>'s neon candidates, in its order - the draw indexes this list.</summary>
    private static readonly ushort[] NeonCandidates = [539, 536, 534, 625];

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;
    private readonly int underworldLayer = store.Dimensions.HeightTiles - 200;
    private readonly GenerationTileCount1458 counter = new(store);
    private readonly GenerationMossSpread1458 spread = new(store);
    private readonly int[] mossType = new int[3];
    private ushort neonMossType;
    private ushort mossWall;
    private ushort mossTile;

    /// <summary>How many cells took moss, counted where the pass assigns it rather than after the spread.</summary>
    public long Placed { get; private set; }

    public void Apply(GenerationGrass1458 grass)
    {
        RandMoss();
        NeonBiomes(grass);
        FloodedCaves();
        SingleCells();
        ExposedCells();
        LavaCells();
        SpreadEverything(grass);
    }

    /// <summary>
    /// Source <c>randMoss</c>. One draw for the neon moss, then three for the ordinary ones, each re-rolled
    /// until it differs from the ones already taken - so the number of draws depends on the rolls themselves.
    /// </summary>
    private void RandMoss()
    {
        neonMossType = NeonCandidates[random.Next(NeonCandidates.Length)];
        mossType[0] = random.Next(5);
        mossType[1] = random.Next(5);
        while (mossType[1] == mossType[0])
            mossType[1] = random.Next(5);
        mossType[2] = random.Next(5);
        while (mossType[2] == mossType[0] || mossType[2] == mossType[1])
            mossType[2] = random.Next(5);
    }

    /// <summary>Source <c>setMoss</c>: the map's thirds each keep one of the three mosses.</summary>
    private void SetMoss(int x)
    {
        int third = x < width * 0.334 ? 0 : x < width * 0.667 ? 1 : 2;
        mossWall = (ushort)(54 + mossType[third]);
        mossTile = (ushort)(179 + mossType[third]);
    }

    /// <summary>
    /// Stage two. One neon biome per 2100 columns, each placed by a sampler that refuses any site whose
    /// hundred-and-one-square box carries a foreign material.
    /// </summary>
    private void NeonBiomes(GenerationGrass1458 grass)
    {
        int wanted = width / 2100;
        int refusals = 0;
        int placed = 0;
        while (placed < wanted)
        {
            cancellation.ThrowIfCancellationRequested();
            int x = random.Next(100, width - 100);
            while (x > width * 0.38 && x < width * 0.62)
                x = random.Next(100, width - 100);

            int y = random.Next((int)rockLayer + 40, lavaLine - 40);
            if (Foreign(x, y))
            {
                // A refusal is nearly free, and only after a world's width of them in a row does the pass
                // give up on one biome. The counter is NOT reset when that happens, so every refusal after
                // the first such run gives up on another.
                refusals++;
                if (refusals > width)
                    placed++;

                continue;
            }

            refusals = 0;
            placed++;
            NeonMossBiome(grass, x, y, lavaLine);
        }
    }

    private bool Foreign(int x, int y)
    {
        const int Reach = 50;
        for (int cx = x - Reach; cx <= x + Reach; cx++)
        for (int cy = y - Reach; cy <= y + Reach; cy++)
        {
            if (!Contains(cx, cy))
                continue;

            WorldTile cell = At(cx, cy);
            if (!cell.IsActive)
                continue;

            if (cell.Type is 70 or 60 or 367 or 368 or 161 or 147 or 396 or 397 ||
                DungeonGenerationTiles1458.IsDungeonTile(cell.Type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Source <c>neonMossBiome</c>. A drunken walk whose step is forced to at least four tiles before it
    /// starts, whose radius shrinks two percent a step, and which is turned back by the rock layer above and
    /// by the caller's floor below.
    /// </summary>
    private void NeonMossBiome(GenerationGrass1458 grass, int i, int j, int maxY)
    {
        double px = i;
        double py = j;
        double vx = random.NextDouble() * 4.0 - 2.0;
        double vy = random.NextDouble() * 4.0 - 2.0;
        if (vx == 0.0)
            vx = 1.0;
        while (Math.Sqrt(vx * vx + vy * vy) < 4.0)
        {
            vx *= 1.5;
            vy *= 1.5;
        }

        double scale = width / 4200.0;
        double radius = random.Next(60, 80) * scale;
        double steps = random.Next(30, 40) * scale;
        while (steps > 0.0)
        {
            cancellation.ThrowIfCancellationRequested();
            radius *= 0.98;
            steps -= 1.0;
            int left = Math.Max((int)(px - radius), 1);
            int right = Math.Min((int)(px + radius), width - 1);
            int top = Math.Max((int)(py - radius), 1);
            int bottom = Math.Min((int)(py + radius), height - 1);
            if (top < rockLayer)
            {
                top = (int)rockLayer;
                if (vy < 5.0)
                    vy = 5.0;
            }

            if (bottom > maxY)
            {
                bottom = maxY;
                if (vy > -5.0)
                    vy = -5.0;
            }

            double reach = radius * (1.0 + random.NextDouble() * 0.4 - 0.2);
            for (int x = left; x < right; x++)
            for (int y = top; y < bottom; y++)
            {
                double dx = Math.Abs(x - px);
                double dy = Math.Abs(y - py);
                if (Math.Sqrt(dx * dx + dy * dy) >= reach * 0.8 || TileType(x, y) != Stone || !Exposed(x, y))
                    continue;

                // Source offers the cell to its LEFT, not the cell it just tested.
                grass.Apply(x - 1, y, Stone, neonMossType);
            }

            px += vx;
            py += vy;
            vx = Math.Clamp(vx + random.NextDouble() * 4.0 - 2.0, -10.0, 10.0);
            vy = Math.Clamp(vy + random.NextDouble() * 4.0 - 2.0, -10.0, 10.0);
        }
    }

    /// <summary>
    /// Stage three. A hundredth of the map's width in caves, each measured by the flood counter and taken only
    /// when it is the right size and made of the right things.
    /// </summary>
    private void FloodedCaves()
    {
        counter.MaxTileCount = CaveTileCeiling;
        int wanted = (int)(width * 0.01);
        for (int i = 0; i < wanted; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = 0;
            int x = random.Next(200, width - 200);
            int y = random.Next((int)(worldSurface + rockLayer) / 2, waterLine);
            if (NearShimmer(x, y))
                continue;

            int size = counter.Count(x, y);
            while (Unsuitable(size) && attempts < CaveRetryCap)
            {
                attempts++;
                x = random.Next(200, width - 200);
                // The retry samples a DEEPER band than the first offer did, and is not shimmer-checked.
                y = random.Next((int)rockLayer + 30, height - 230);
                size = counter.Count(x, y);
            }

            if (attempts >= CaveRetryCap)
                continue;

            SetMoss(x);
            spread.Apply(x, y, mossWall, mossTile);
            Placed++;
        }
    }

    private bool Unsuitable(int size) =>
        size >= CaveTileCeiling || size < 10 || counter.LavaCount > 0 || counter.IceCount > 0 ||
        counter.RockCount == 0 || counter.ShroomCount > 0;

    /// <summary>Stage four: one offer per column of the map, taken wherever it lands on bare stone.</summary>
    private void SingleCells()
    {
        for (int i = 0; i < width; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int x = random.Next(50, width - 50);
            int y = random.Next((int)(worldSurface + rockLayer) / 2, lavaLine);
            if (NearShimmer(x, y) || TileType(x, y) != Stone)
                continue;

            SetMoss(x);
            At(x, y).Type = mossTile;
            Placed++;
        }
    }

    /// <summary>
    /// Stage five: a twentieth of the map's width in EXPOSED stone. The budget only moves on a success, so an
    /// enclosed map spins here rather than giving up.
    /// </summary>
    private void ExposedCells()
    {
        double budget = width * 0.05;
        while (budget > 0.0)
        {
            cancellation.ThrowIfCancellationRequested();
            int x = random.Next(50, width - 50);
            int y = random.Next((int)(worldSurface + rockLayer) / 2, lavaLine);
            if (NearShimmer(x, y) || TileType(x, y) != Stone || !Exposed(x, y))
                continue;

            SetMoss(x);
            At(x, y).Type = mossTile;
            Placed++;
            budget -= 1.0;
        }
    }

    /// <summary>
    /// Stage six, the lava moss. Three different decrements: a thousandth every iteration whatever happens, a
    /// further two thousandths for an exposed stone cell that fails the lava census, and a whole unit only for
    /// one that passes. It takes no shimmer check and does not consult the map's thirds.
    /// </summary>
    private void LavaCells()
    {
        double budget = width * 0.065;
        while (budget > 0.0)
        {
            cancellation.ThrowIfCancellationRequested();
            int x = random.Next(50, width - 50);
            int y = random.Next(waterLine, underworldLayer);
            if (TileType(x, y) == Stone && Exposed(x, y))
            {
                if (LavaNear(x, y) > 20)
                {
                    At(x, y).Type = LavaMoss;
                    Placed++;
                    budget -= 1.0;
                }
                else
                {
                    budget -= 0.002;
                }
            }

            budget -= 0.001;
        }
    }

    private int LavaNear(int x, int y)
    {
        const int Reach = 25;
        int found = 0;
        for (int cx = x - Reach; cx < x + Reach; cx++)
        for (int cy = y - Reach; cy < y + Reach; cy++)
        {
            if (!Contains(cx, cy))
                continue;

            WorldTile cell = At(cx, cy);
            if (cell.LiquidAmount > 0 && cell.LiquidKind == WorldLiquidKind.Lava)
                found++;
        }

        return found;
    }

    /// <summary>
    /// Stage seven: every moss tile in the world offers its own identity to its four neighbours. This is where
    /// most of the moss a finished world carries actually comes from - the stages above only seed it.
    /// </summary>
    private void SpreadEverything(GenerationGrass1458 grass)
    {
        for (int x = 0; x < width; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 0; y < height; y++)
            {
                WorldTile cell = At(x, y);
                if (!cell.IsActive || !IsMoss(cell.Type))
                    continue;

                ushort moss = cell.Type;
                grass.Apply(x - 1, y, Stone, moss);
                grass.Apply(x + 1, y, Stone, moss);
                grass.Apply(x, y - 1, Stone, moss);
                grass.Apply(x, y + 1, Stone, moss);
            }
        }
    }

    private bool NearShimmer(int x, int y)
    {
        double dx = x - shimmerX;
        double dy = y - shimmerY;
        return Math.Sqrt(dx * dx + dy * dy) < ShimmerSafetyDistance;
    }

    /// <summary>At least one of the four orthogonal neighbours is not a block.</summary>
    private bool Exposed(int x, int y) =>
        !At(x - 1, y).IsActive || !At(x + 1, y).IsActive || !At(x, y - 1).IsActive || !At(x, y + 1).IsActive;

    /// <summary>Source <c>Main.tileMoss</c>.</summary>
    private static bool IsMoss(ushort type) =>
        type is 179 or 180 or 181 or 182 or 183 or 381 or 534 or 536 or 539 or 625 or 627;

    /// <summary>Source <c>WorldGen.TileType</c>: the identity, or minus one where nothing stands.</summary>
    private int TileType(int x, int y)
    {
        if (!Contains(x, y))
            return -1;

        WorldTile cell = At(x, y);
        return cell.IsActive ? cell.Type : -1;
    }

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
