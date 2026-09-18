using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>Terraria.GameContent.Biomes.HiveBiome</c> and the
/// <c>GenPassNameID.Beehives</c> pass that drives it.
/// </summary>
/// <remarks>
/// <para>
/// A hive is not a carved pocket. <c>CreateHiveTunnel</c> drifts a point through the mud on a wandering
/// velocity and, at every step, writes three concentric bands around it: inside four tenths of the radius the
/// cell becomes open honey with a hive wall, out to three quarters it becomes Hive block, and out to six tenths
/// it takes the hive wall regardless. The bands overlap on purpose, so the result is a honey core inside a
/// hive shell rather than a cavity with a lining. Each of two to four lobes runs two to four tunnels from the
/// SAME origin and keeps only the last one's end point, which is why a hive is a cluster rather than a chain.
/// </para>
/// <para>
/// The site test is a disc of radius fifteen: at least three quarters of its solid cells must be mud or jungle
/// grass and at least two must be jungle grass. After the tunnels, every hive block and hive wall in the
/// hundred-cell square is re-framed, and the wall half of that consumes one draw per walled cell - thousands
/// per hive, more than every other consumer in the biome put together.
/// </para>
/// <para>
/// Bounded slice: the four honey patches the source scatters around each accepted hive are NOT ported.
/// <c>HoneyPatchBiome</c> is written against vanilla's shape-generation DSL - blotches, radial dither, inner
/// outlines and rectangle masks - which this runtime does not have, and inventing it would put made-up draws
/// into the shared stream. The pass therefore consumes the hive's draws and not the patches'.
/// </para>
/// </remarks>
internal sealed class HiveBiome1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double rockLayer,
    CancellationToken cancellation)
{
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Hive = 225;
    private const ushort LihzahrdBrick = 226;
    private const ushort HiveWall = 86;
    private const ushort LihzahrdBrickWall = 87;
    private const ushort LivingWoodWall = 244;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;
    private readonly List<(int X, int Y, int Width, int Height)> protectedAreas = [];

    /// <summary>Where a Bee Larva should later be framed, one per accepted hive.</summary>
    public List<(int X, int Y)> LarvaAnchors { get; } = [];

    /// <summary>
    /// Source <c>GenPassNameID.Beehives</c>: a budget of five to eight hives per 4200 tiles of width, each
    /// offered a uniformly random point of the world between the surface-rock midpoint and three hundred above
    /// the bottom, with ten thousand attempts to spend it.
    /// </summary>
    public int Apply()
    {
        double scale = width / 4200.0;
        double budget = 1 + random.Next((int)(5.0 * scale), (int)(8.0 * scale));
        int attempts = 10000;
        int placed = 0;

        while (budget > 0.0 && attempts > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            attempts--;

            // RandomWorldPoint draws the column first and the row second; swapping them moves every hive.
            int originX = random.Next(20, width - 20);
            int originY = random.Next((int)(worldSurface + rockLayer) >> 1, height - 300);

            if (!TryPlace(originX, originY))
                continue;

            budget -= 1.0;
            placed++;

            // The source rolls how many honey patches to scatter even though this runtime does not place them,
            // because the draw belongs to the shared stream whether or not the patch is built.
            _ = random.Next(5);
        }

        return placed;
    }

    private bool TryPlace(int originX, int originY)
    {
        if (!CanPlace(originX - 50, originY - 50, 100, 100))
            return false;
        if (TooCloseToImportantLocations(originX, originY))
            return false;

        CountDisc(originX, originY, 15, out int solid, out int mudOrGrass, out int grass);
        if (solid == 0 || (double)mudOrGrass / solid < 0.75 || grass < 2)
            return false;

        var lobeX = new int[1000];
        var lobeY = new int[1000];
        int lobes = 0;

        double cursorX = originX;
        double cursorY = originY;
        int lobeCount = random.Next(2, 5);
        for (int lobe = 0; lobe < lobeCount; lobe++)
        {
            cancellation.ThrowIfCancellationRequested();
            double endX = cursorX;
            double endY = cursorY;
            int tunnels = random.Next(2, 5);
            for (int tunnel = 0; tunnel < tunnels; tunnel++)
            {
                // Every tunnel of a lobe starts from the lobe's own origin, not from the previous tunnel's
                // end, and only the last end point survives.
                CreateHiveTunnel((int)cursorX, (int)cursorY, out endX, out endY);
            }

            cursorX = endX;
            cursorY = endY;
            lobeX[lobes] = (int)cursorX;
            lobeY[lobes] = (int)cursorY;
            lobes++;
        }

        FrameOutAllHiveContents(originX, originY, 50);

        for (int lobe = 0; lobe < lobes; lobe++)
        {
            int x = lobeX[lobe];
            int y = lobeY[lobe];
            int step = random.Next(2) == 0 ? -1 : 1;

            bool abandoned = false;
            while (InWorld(x, y, 10) && BadSpotForHoneyFall(x, y))
            {
                x += step;
                if (Math.Abs(x - lobeX[lobe]) > 50)
                {
                    abandoned = true;
                    break;
                }
            }

            if (abandoned)
                continue;

            x += step;
            if (SpotActuallyNotInHive(x, y))
                continue;

            CreateBlockedHoneyCube(x, y);
            CreateDentForHoneyFall(x, y, step);
        }

        CreateStandForLarva(cursorX, cursorY);
        protectedAreas.Add((originX - 50 - 5, originY - 50 - 5, 110, 110));
        return true;
    }

    /// <summary>
    /// Source <c>HiveBiome.CreateHiveTunnel</c>. The three radius bands and the order they are written in are
    /// what make a hive: honey core, hive shell, then the wall band that reaches back inside the shell.
    /// </summary>
    private void CreateHiveTunnel(int i, int j, out double endX, out double endY)
    {
        double radius = random.Next(12, 21);
        double life = random.Next(10, 21);
        double baseRadius = radius;

        double x = i;
        double y = j;
        double velocityX = random.Next(-10, 11) * 0.2;
        double velocityY = random.Next(-10, 11) * 0.2;

        while (radius > 0.0 && life > 0.0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (y > height - 250)
                life = 0.0;

            radius = baseRadius * (1.0 + random.Next(-20, 20) * 0.01);
            life -= 1.0;

            int left = (int)(x - radius);
            int right = (int)(x + radius);
            int top = (int)(y - radius);
            int bottom = (int)(y + radius);
            if (left < 1)
                left = 1;
            if (right > width - 1)
                right = width - 1;
            if (top < 1)
                top = 1;
            if (bottom > height - 1)
                bottom = height - 1;

            for (int column = left; column < right; column++)
            {
                for (int row = top; row < bottom; row++)
                {
                    if (!InWorld(column, row, 50))
                    {
                        life = 0.0;
                    }
                    else
                    {
                        if (WallAt(column - 10, row) == LihzahrdBrickWall)
                            life = 0.0;
                        if (WallAt(column + 10, row) == LihzahrdBrickWall)
                            life = 0.0;
                        if (WallAt(column, row - 10) == LihzahrdBrickWall)
                            life = 0.0;
                        if (WallAt(column, row + 10) == LihzahrdBrickWall)
                            life = 0.0;
                    }

                    if (row < worldSurface && WallAt(column, row - 5) == 0)
                        life = 0.0;

                    double dx = Math.Abs(column - x);
                    double dy = Math.Abs(row - y);
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance < baseRadius * 0.4 * (1.0 + random.Next(-10, 11) * 0.005))
                    {
                        ref WorldTile cell = ref At(column, row);
                        if (random.Next(3) == 0)
                            cell.LiquidAmount = byte.MaxValue;
                        cell.LiquidKind = WorldLiquidKind.Honey;
                        cell.Wall = HiveWall;
                        cell.Flags &= ~WorldTileFlags.Active;
                        cell.Shape = 0;
                    }
                    else if (distance < baseRadius * 0.75 * (1.0 + random.Next(-10, 11) * 0.005))
                    {
                        ref WorldTile cell = ref At(column, row);
                        cell.LiquidAmount = 0;
                        if (cell.Wall != HiveWall && cell.Wall != LivingWoodWall)
                        {
                            cell.Flags |= WorldTileFlags.Active;
                            cell.Shape = 0;
                            cell.Type = Hive;
                        }
                    }

                    if (distance < baseRadius * 0.6 * (1.0 + random.Next(-10, 11) * 0.005))
                        At(column, row).Wall = HiveWall;
                }
            }

            x += velocityX;
            y += velocityY;
            life -= 1.0;
            velocityY += random.Next(-10, 11) * 0.05;
            velocityX += random.Next(-10, 11) * 0.05;
        }

        endX = x;
        endY = y;
    }

    /// <summary>
    /// Source <c>HiveBiome.FrameOutAllHiveContents</c>. The wall half draws once per walled cell, which is by
    /// far the biggest RNG consumer in the biome.
    /// </summary>
    private void FrameOutAllHiveContents(int originX, int originY, int halfWidth)
    {
        int left = Math.Max(10, originX - halfWidth);
        int right = Math.Min(width - 10, originX + halfWidth);
        int top = Math.Max(10, originY - halfWidth);
        int bottom = Math.Min(height - 10, originY + halfWidth);

        for (int column = left; column < right; column++)
        {
            for (int row = top; row < bottom; row++)
            {
                WorldTile tile = At(column, row);
                if (tile.IsActive && tile.Type == Hive)
                {
                    // Hive is not frame-important, so the source's SquareTileFrame does nothing during
                    // generation beyond the inactive-cell cleanup the framing slice already carries.
                    new GenerationTileFraming1458(store).SquareTileFrame(column, row);
                }

                if (tile.Wall == HiveWall)
                    SquareWallFrame(column, row);
            }
        }
    }

    /// <summary>
    /// Source <c>WorldGen.SquareWallFrame</c> reduced to its generation-time effect: the centre cell's frame is
    /// reset, which costs one draw when it carries a wall, and an empty neighbour loses its wall paint.
    /// </summary>
    private void SquareWallFrame(int x, int y)
    {
        if (x <= 1 || y <= 1 || x >= width - 2 || y >= height - 2)
            return;

        for (int column = x - 1; column <= x + 1; column++)
        {
            for (int row = y - 1; row <= y + 1; row++)
            {
                ref WorldTile cell = ref At(column, row);
                if (cell.Wall == 0)
                {
                    cell.WallColor = 0;
                    cell.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
                }
                else if (column == x && row == y)
                {
                    _ = random.Next(0, 3);
                }
            }
        }
    }

    /// <summary>Source <c>HiveBiome.CreateBlockedHoneyCube</c>: a two by two of honey inside a ring of hive.</summary>
    private void CreateBlockedHoneyCube(int x, int y)
    {
        for (int column = x - 1; column <= x + 2; column++)
        {
            for (int row = y - 1; row <= y + 2; row++)
            {
                if (!Contains(column, row))
                    continue;

                ref WorldTile cell = ref At(column, row);
                if (column >= x && column <= x + 1 && row >= y && row <= y + 1)
                {
                    cell.Flags &= ~WorldTileFlags.Active;
                    cell.LiquidAmount = byte.MaxValue;
                    cell.LiquidKind = WorldLiquidKind.Honey;
                }
                else
                {
                    cell.Flags |= WorldTileFlags.Active;
                    cell.Type = Hive;
                }
            }
        }
    }

    /// <summary>Source <c>HiveBiome.CreateDentForHoneyFall</c>, which pounds the lip the honey pours over.</summary>
    private void CreateDentForHoneyFall(int x, int y, int direction)
    {
        direction *= -1;
        y++;
        int steps = 0;
        while ((steps < 4 || IsSolid(x, y)) && x > 10 && x < width - 10)
        {
            steps++;
            x += direction;
            if (!IsSolid(x, y) || !CanPoundTile(x, y))
                continue;

            // PoundTile toggles the half brick rather than setting it.
            At(x, y).Shape = At(x, y).Shape == 1 ? (byte)0 : (byte)1;
            if (Contains(x, y + 1) && !At(x, y + 1).IsActive)
            {
                ref WorldTile below = ref At(x, y + 1);
                below.Flags |= WorldTileFlags.Active;
                below.Type = Hive;
            }
        }
    }

    /// <summary>
    /// Source <c>WorldGen.CanPoundTile</c> for what the dent can reach. Its two remaining tests,
    /// <c>ForbidsSloping</c> on the cell above and <c>CanKillTile</c>, are not ported: the dent only ever pounds
    /// mud, jungle grass and Hive with Hive or open air above, none of which either test refuses.
    /// </summary>
    private bool CanPoundTile(int x, int y)
    {
        if (!Contains(x, y))
            return false;

        ushort type = At(x, y).Type;
        return type is not (10 or 48 or 137 or 232 or 380 or 387 or 388 or 476 or 484 or 138 or 664 or 190 or 30);
    }

    /// <summary>Source <c>HiveBiome.SpotActuallyNotInHive</c>.</summary>
    private bool SpotActuallyNotInHive(int x, int y)
    {
        for (int column = x - 1; column <= x + 2; column++)
        {
            for (int row = y - 1; row <= y + 2; row++)
            {
                if (column < 10 || column > width - 10)
                    return true;
                if (!Contains(column, row))
                    return true;
                if (At(column, row).IsActive && At(column, row).Type != Hive)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Source <c>HiveBiome.BadSpotForHoneyFall</c>.</summary>
    private bool BadSpotForHoneyFall(int x, int y)
    {
        if (!Contains(x + 1, y + 1))
            return true;
        if (At(x, y).IsActive && At(x, y + 1).IsActive && At(x + 1, y).IsActive)
            return !At(x + 1, y + 1).IsActive;

        return true;
    }

    /// <summary>
    /// Source <c>HiveBiome.CreateStandForLarva</c>: a three by four pocket cleared with a hive floor under it,
    /// and the anchor the Larva pass later frames its egg on.
    /// </summary>
    private void CreateStandForLarva(double positionX, double positionY)
    {
        int x = (int)positionX;
        int y = (int)positionY;
        LarvaAnchors.Add((Math.Clamp(x, 5, width - 5), Math.Clamp(y, 5, height - 5)));

        for (int column = x - 1; column <= x + 1 && column > 0 && column < width; column++)
        {
            for (int row = y - 2; row <= y + 1 && row > 0 && row < height; row++)
            {
                ref WorldTile cell = ref At(column, row);
                if (row != y + 1)
                {
                    cell.Flags &= ~WorldTileFlags.Active;
                    continue;
                }

                cell.Flags |= WorldTileFlags.Active;
                cell.Type = Hive;
                cell.Shape = 0;
            }
        }
    }

    /// <summary>Source <c>HiveBiome.TooCloseToImportantLocations</c>, sampled every tenth cell as the source does.</summary>
    private bool TooCloseToImportantLocations(int x, int y)
    {
        for (int column = x - 150; column < x + 150; column += 10)
        {
            if (column <= 0 || column > width - 1)
                continue;

            for (int row = y - 150; row < y + 150; row += 10)
            {
                if (row <= 0 || row > height - 1)
                    continue;

                WorldTile tile = At(column, row);
                if (tile.IsActive && tile.Type == LihzahrdBrick)
                    return true;
                if (tile.Wall is 83 or 3 or LihzahrdBrickWall)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Source <c>StructureMap.CanPlace</c> against <c>TileID.Sets.GeneralPlacementTiles</c>, plus the hives
    /// this pass has already protected. The shared structure map earlier passes contribute to does not exist in
    /// this runtime, so only the tile-set half of the source's guard and this pass's own entries apply.
    /// </summary>
    private bool CanPlace(int x, int y, int areaWidth, int areaHeight)
    {
        if (x < 0 || y < 0 || x + areaWidth > width - 1 || y + areaHeight > height - 1)
            return false;

        foreach ((int px, int py, int pw, int ph) in protectedAreas)
        {
            if (x < px + pw && px < x + areaWidth && y < py + ph && py < y + areaHeight)
                return false;
        }

        for (int column = x; column < x + areaWidth; column++)
        {
            for (int row = y; row < y + areaHeight; row++)
            {
                WorldTile tile = At(column, row);
                if (tile.IsActive && !IsGeneralPlacementTile(tile.Type))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Source <c>TileID.Sets.GeneralPlacementTiles</c>, which is all tiles except this list.</summary>
    private static bool IsGeneralPlacementTile(ushort type) => type is not (
        225 or 41 or 481 or 43 or 482 or 44 or 483 or 226 or 203 or 112 or 25 or 70 or 151 or 21 or 31 or
        696 or 467 or 12 or 665 or 639 or 138 or 664 or 711 or 712 or 713 or 714 or 715 or 716);

    /// <summary>
    /// The disc scan the site test runs: <c>Shapes.Circle(15)</c> chained through <c>IsSolid</c> and two
    /// <c>OnlyTiles</c> filters, counted at each stage.
    /// </summary>
    private void CountDisc(int originX, int originY, int radius, out int solid, out int mudOrGrass, out int grass)
    {
        solid = 0;
        mudOrGrass = 0;
        grass = 0;

        int limit = (radius + 1) * (radius + 1);
        for (int row = originY - radius; row <= originY + radius; row++)
        {
            double offset = row - originY;
            int span = Math.Min(radius, (int)Math.Sqrt(limit - offset * offset));
            for (int column = originX - span; column <= originX + span; column++)
            {
                if (!IsSolid(column, row))
                    continue;

                solid++;
                ushort type = At(column, row).Type;
                if (type != JungleGrass && type != Mud)
                    continue;

                mudOrGrass++;
                if (type == JungleGrass)
                    grass++;
            }
        }
    }

    /// <summary>Source <c>WorldGen.SolidTile</c>.</summary>
    private bool IsSolid(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !tile.IsActuated && tile.Shape < 2 && tile.Shape != 1;
    }

    private ushort WallAt(int x, int y) => Contains(x, y) ? At(x, y).Wall : (ushort)0;

    private bool InWorld(int x, int y, int fluff) =>
        x >= fluff && x < width - fluff && y >= fluff && y < height - fluff;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
