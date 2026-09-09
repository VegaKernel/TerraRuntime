using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary LegacyEntranceDungeonHall; the graph still selects its origin and component seed.</summary>
internal sealed class DungeonLegacyEntranceHall1458(WorldTileStore tiles, ushort brick, ushort crackedBrick, ushort wall,
    double worldSurface, int entranceStrengthX, int entranceStrengthX2, int entranceStrengthY2,
    IWorldGenerationVanillaRandom sharedRandom, CancellationToken cancellationToken)
{
    public (DungeonComponent1458 Component, DungeonPoint1458 Cursor, bool ReachedSurface)
        Generate(DungeonPoint1458 origin, int dungeonTopX, int seed, bool skewed)
        => GenerateCore(origin, dungeonTopX, seed, skewed, null, null);

    public (DungeonComponent1458 Component, DungeonPoint1458 Cursor, bool ReachedSurface)
        GeneratePrecalculated(DungeonEntranceSegment1458 segment, List<DungeonHallPlatform1458> platforms)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!segment.Start.IsFinite || !segment.Target.IsFinite || segment.Start == default || segment.Target == default ||
            segment.OverrideSteps <= 0 || segment.Start.X < 0 || segment.Start.Y < 0 ||
            segment.Start.X >= tiles.Dimensions.WidthTiles || segment.Start.Y >= tiles.Dimensions.HeightTiles ||
            segment.Target.X < 0 || segment.Target.Y < 0 || segment.Target.X >= tiles.Dimensions.WidthTiles || segment.Target.Y >= tiles.Dimensions.HeightTiles)
            throw new InvalidOperationException("Invalid precalculated entrance segment.");
        ArgumentNullException.ThrowIfNull(platforms);
        // CalculateHall and GenerateHall both invoke LegacyHall(0,0): their initial random velocity is discarded,
        // but its draws remain. Calculate has no tile/shared-RNG effects with UsePrecalculatedEntrance=true.
        return GenerateCore(default, 0, segment.Seed, false, segment, platforms);
    }

    private (DungeonComponent1458 Component, DungeonPoint1458 Cursor, bool ReachedSurface)
        GenerateCore(DungeonPoint1458 origin, int dungeonTopX, int seed, bool skewed,
            DungeonEntranceSegment1458? precalculated, List<DungeonHallPlatform1458>? platforms)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((brick, crackedBrick, wall) is not ((41, 481, 7) or (43, 482, 8) or (44, 483, 9)))
            throw new InvalidOperationException("Unsupported ordinary dungeon entrance palette.");
        var random = new DungeonUnifiedRandom1458(seed);
        int strength = random.Next(5, 9), steps = random.Next(10, 30);
        int direction = origin.X <= dungeonTopX ? 1 : -1;
        int width = tiles.Dimensions.WidthTiles, height = tiles.Dimensions.HeightTiles, midpoint = width / 2;
        if (origin.X > width - 400) direction = -1;
        else if (origin.X < 400) direction = 1;
        double vx = direction, vy = -1;
        if (random.Next(3) != 0) vx *= 1f + random.Next(0, 200) * .01f;
        else if (random.Next(3) == 0) vx *= random.Next(50, 76) * .01f;
        else if (random.Next(6) == 0) vy *= 2;
        // Only outward motion is capped/reversed. Inward motion retains the sampled full velocity.
        if (origin.X < midpoint && vx < -.5) vx = skewed ? .5 : -.5;
        if (origin.X > midpoint && vx > .5) vx = skewed ? -.5 : .5;

        double x = origin.X, y = origin.Y;
        if (precalculated is { } segment)
        {
            double dx = segment.Target.X - segment.Start.X, dy = segment.Target.Y - segment.Start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double inverse = length == 0 ? 0 : 1d / length;
            vx = length == 0 ? 1 : dx * inverse;
            vy = length == 0 ? 0 : dy * inverse;
            // Override endpoints take precedence over OverrideSteps, including the rounded-up final step.
            steps = (int)Math.Ceiling(length / Math.Sqrt(vx * vx + vy * vy));
            x = segment.Start.X; y = segment.Start.Y; origin = segment.Start.Tile;
        }
        int left = origin.X, right = origin.X, top = origin.Y, bottom = origin.Y;
        int platformCountdown = 0;
        bool updatedGenerationBounds = false;
        bool reached = false;
        while (steps > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            steps--;
            if ((int)x < 35 || (int)y < 35 || (int)x >= width - 35 || (int)y >= height - 35) break;
            updatedGenerationBounds = true;
            int x0 = ClampX((int)(x - strength - 4d - random.Next(6)));
            int x1 = ClampX((int)(x + strength + 4d + random.Next(6)));
            int y0 = ClampY((int)(y - strength - 4d));
            int y1 = ClampY((int)(y + strength + 4d + random.Next(6)));
            left = Math.Min(left, x0); right = Math.Max(right, x1);
            top = Math.Min(top, y0); bottom = Math.Max(bottom, y1);

            int inward = x > midpoint ? -1 : 1;
            int probeX = (int)(x + entranceStrengthX * .6 * inward + entranceStrengthX2 * inward);
            int probeY = (int)(y - strength - 6d + (int)(entranceStrengthY2 * .5));
            if (precalculated is null && y < worldSurface - 5)
            {
                if ((uint)probeX >= (uint)width || probeY < 2 || probeY >= height)
                    throw new InvalidOperationException("Dungeon entrance surface probe is outside the candidate world.");
                if (At(probeX, probeY).Wall == 0 && At(probeX, probeY - 1).Wall == 0 && At(probeX, probeY - 2).Wall == 0)
                {
                    reached = true;
                    // Strength/steps use the component RNG; TileRunner itself consumes the caller's world RNG.
                    // The admitted -1 slice never reads liquid-line inputs (unlike the -2 underworld cavity).
                    new SmallTerrainRunner1458(tiles, sharedRandom, worldSurface, default, cancellationToken)
                        .Run(probeX, probeY, random.Next(25, 35), random.Next(10, 20),
                            SmallTerrainRunner1458.AirCavity, speedX: 0, speedY: -1);
                }
            }

            for (int tx = x0; tx < x1; tx++)
            for (int ty = y0; ty < y1; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                tile.LiquidAmount = 0;
                if (DungeonGenerationTiles1458.CanPlaceHallBrick(in tile, crackedBrick))
                    DungeonGenerationTiles1458.SetBrick(ref tile, brick, true);
            }
            for (int tx = x0 + 1; tx < x1 - 1; tx++)
            for (int ty = y0 + 1; ty < y1 - 1; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                if (tile.Wall != 0) tile.WallColor = 0; // paintWall(0) does nothing when no wall exists.
                DungeonGenerationTiles1458.SetWall(ref tile, wall, false);
            }
            int widening = random.Next(strength) == 0 ? random.Next(1, 3) : 0;
            double radius = strength * .5;
            int ix0 = ClampX((int)(x - radius - widening)), ix1 = ClampX((int)(x + radius + widening));
            int iy0 = ClampY((int)(y - radius - widening)), iy1 = ClampY((int)(y + radius + widening));
            if (precalculated is not null && --platformCountdown <= 0)
            {
                platformCountdown = 10;
                platforms!.Add(new(new((int)x, (int)y), true, .25));
            }
            for (int tx = ix0; tx < ix1; tx++)
            for (int ty = iy0; ty < iy1; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                DungeonGenerationTiles1458.ClearTile(ref tile);
                DungeonGenerationTiles1458.SetWall(ref tile, wall, false);
            }
            if (reached) steps = 0;
            x += vx; y += vy;
            if (precalculated is null && y < worldSurface) vy *= .9800000190734863;
        }
        DungeonPoint1458 end = new((int)x, (int)y);
        DungeonBounds1458? generationBounds = updatedGenerationBounds ? new(left, top, right, bottom) : null;
        if (precalculated is not null)
        {
            // GenerateHall resets Bounds after CalculateHall, then Processed suppresses brush bounds updates.
            // Preserve this source quirk; do not invent a component rectangle from all painted cells.
            left = Math.Clamp(origin.X, 10, width - 10); top = Math.Clamp(origin.Y, 10, height - 10);
            right = left + 1; bottom = top + 1;
        }
        return (new(DungeonComponentKind1458.EntranceHall, origin, end, new(left, top, right, bottom), seed)
            { GenerationBounds = generationBounds }, end, reached);

        int ClampX(int value) => Math.Clamp(value, 30, width - 31);
        int ClampY(int value) => Math.Clamp(value, 30, height - 31);
    }

    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
