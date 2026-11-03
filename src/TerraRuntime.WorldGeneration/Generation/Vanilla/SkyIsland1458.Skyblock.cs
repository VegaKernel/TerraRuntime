using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class EarlyPass1458
{
    private void ApplySkyblock(IWorldGenerationContext context, Workspace workspace)
    {
        // This caller owns only the basic Skyblock option: extra island styles remain outside its admission.
        if (state.Profile.Special != VanillaSpecialWorldSeed1458.Skyblock || state.Profile.Secret != VanillaSecretWorldSeed1458.None)
            throw new InvalidOperationException("Additional Skyblock options require their island-style owner.");
        foreach (ref WorldTile tile in workspace.TileStore.Tiles)
        {
            tile.Flags &= ~WorldTileFlags.Active;
            tile.Type = 0;
        }
        IWorldGenerationVanillaRandom random = context.VanillaRandom!;
        int width = workspace.TileStore.Dimensions.WidthTiles, height = workspace.TileStore.Dimensions.HeightTiles;
        int x = random.Next((int)(width * .45), (int)(width * .55));
        int y = random.Next((int)(state.MainWorldSurface * .6), (int)(state.MainWorldSurface * .8));
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        var islands = new SkyIsland1458(workspace.TileStore, random);
        islands.GenerateBasicSkyblock(x, y, shimmer: false, workspace, framing, context.CancellationToken);
        int shimmerY = y + random.Next(-50, 50);
        int shimmerX = x >= width / 2 ? random.Next(580, (int)(width * .3)) : random.Next((int)(width * .7), width - 580);
        // In this option the first shimmer request has no proximity check, so it always succeeds.
        islands.GenerateBasicSkyblock(Math.Clamp(shimmerX, 580, width - 580), Math.Clamp(shimmerY, 80, height - 80),
            shimmer: true, workspace, framing, context.CancellationToken);
        int spawnY = y;
        while (spawnY < height - 1 && (!workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, spawnY)].IsActive ||
            workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, spawnY)].Type != 0)) spawnY++;
        if (spawnY > height - 10)
        {
            spawnY = 0;
            while (spawnY < height - 1 && !workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, spawnY)].IsActive) spawnY++;
            if (spawnY > height - 10) spawnY = y;
        }
        if (!workspace.TrySetSpawn(x, spawnY)) throw new InvalidOperationException("Skyblock spawn was outside its candidate.");
        workspace.MarkVanillaSpawnRandomized();
    }
}

internal sealed partial class SkyIsland1458
{
    internal void GenerateBasicSkyblock(int originX, int originY, bool shimmer, Workspace workspace,
        GenerationTileFraming1458 framing, CancellationToken cancellationToken)
    {
        double span = random.Next(220, 250);
        if (shimmer) span = random.Next(145, 156);
        double originalSpan = span;
        left = right = originX;
        top = bottom = originY;
        SkyblockTrail(originX, originY, span, core: false, shimmer, framing);
        for (int x = left + random.Next(5); x < right;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int y = bottom + 10;
            while (!At(x, y).IsActive && y > 0) y--;
            if (y <= 0) { x += random.Next(5); continue; }
            y += random.Next(-3, 4);
            int radius = random.Next(4, 8);
            ushort type = random.Next(4) == 0 ? Rain : Cloud;
            for (int column = x - radius; column <= x + radius; column++)
                for (int row = y - radius; row <= y + radius; row++)
                    if (SkyblockInside(column, row, 5) && row > top && SkyblockDistance(column - x, (row - y) * 2) < radius + random.Next(2))
                        SkyblockSet(column, row, type, framing);
            x += random.Next(radius, (int)(radius * 1.5));
        }
        SkyblockTrail(originX, top, originalSpan * (double).8f, core: true, shimmer, framing);
        for (int x = left + random.Next(5); x < right;)
        {
            int y = bottom;
            while ((!At(x, y).IsActive || At(x, y).Type != Dirt) && x < right)
            {
                y--;
                if (y < top) { y = bottom; x += random.Next(1, 4); }
            }
            if (x >= right) continue;
            y += random.Next(0, 4);
            int radius = random.Next(2, 8);
            for (int column = x - radius; column <= x + radius; column++)
                for (int row = y - radius; row <= y + radius; row++)
                    if (SkyblockInside(column, row, 5) && row > top && SkyblockDistance(column - x, (row - y) * 2) < radius)
                    {
                        At(column, row).Type = Cloud; // Source does not activate these underside replacements.
                        framing.SquareTileFrame(column, row);
                    }
            x += random.Next(radius, (int)(radius * 1.5));
        }
        if (shimmer) SkyblockShimmer(originX, framing);
        var walls = new GenerationWallFraming1458(tiles, random);
        for (int x = left - 20; x <= right + 20; x++)
            for (int y = top - 20; y <= bottom + 20; y++)
            {
                if (!SkyblockInside(x, y, 5)) continue;
                bool surrounded = true;
                for (int column = x - 1; column <= x + 1; column++)
                    for (int row = y - 1; row <= y + 1; row++)
                        if (SkyblockInside(column, row, 10) && (!At(column, row).IsActive || At(column, row).Wall is not (0 or Wall))) surrounded = false;
                if (surrounded) { At(x, y).Wall = Wall; walls.SquareWallFrame(x, y); }
            }
        SkyblockFurniture(originX, shimmer, framing);
        int pockets = random.Next(4);
        for (int index = 0; index <= pockets; index++)
        {
            int x = random.Next(left - 5, right + 5), y = top - random.Next(20, 40), radius = random.Next(4, 8);
            ushort type = random.Next(2) == 0 ? Rain : Cloud;
            for (int column = x - radius; column <= x + radius; column++)
                for (int row = y - radius; row <= y + radius; row++)
                    if (SkyblockDistance(column - x, (row - y) * 2) < radius + random.Next(-1, 2) && SkyblockInside(column, row, 10))
                        SkyblockSet(column, row, type, framing);
        }
        workspace.AddVanillaLandmass(new(VanillaLandmassKind1458.SkyblockIsland, originX, originY, 10, shimmer ? 1 : 0, -1));
    }

    private void SkyblockTrail(int originX, int originY, double span, bool core, bool shimmer, GenerationTileFraming1458 framing)
    {
        int steps = core ? random.Next(10, 15) : random.Next(20, 30);
        double x = originX, y = originY, vx = SkyblockVelocity(), vy = random.Next(-20, -10) * .02;
        double verticalScale = core ? (shimmer ? 1.75 : 4) : (shimmer ? 2.25 : 4.5);
        while (span > 0 && steps-- > 0)
        {
            span -= random.Next(4);
            int minX = Math.Max(0, (int)(x - span * .5)), maxX = Math.Min(tiles.Dimensions.WidthTiles, (int)(x + span * .5));
            int minY = Math.Max(0, core ? top - 1 : (int)(y - span * .5)), maxY = Math.Min(tiles.Dimensions.HeightTiles, (int)(y + span * .5));
            double radius = span * random.Next(80, 120) * .01;
            double surface = y + 1;
            for (int column = minX; column < maxX; column++)
            {
                if (random.Next(2) == 0) surface += random.Next(-1, 2);
                surface = Math.Clamp(surface, y, y + 2);
                for (int row = minY; row < maxY; row++)
                {
                    if (!SkyblockInside(column, row, 5) || row <= surface + (core ? 0 : 1) || SkyblockDistance(column - x, Math.Abs(row - y) * verticalScale) >= radius * .4) continue;
                    if (core)
                    {
                        if (At(column, row).Type == Cloud) { At(column, row).Type = Dirt; framing.SquareTileFrame(column, row); }
                    }
                    else
                    {
                        left = Math.Min(left, column); right = Math.Max(right, column); top = Math.Min(top, row); bottom = Math.Max(bottom, row);
                        SkyblockSet(column, row, Cloud, framing);
                    }
                }
            }
            x += vx; y += vy;
            vx = Math.Clamp(vx + random.Next(-20, 21) * .05, -1, 1);
            if (vy > .2 || vy < -.2) vy = -.2;
        }
    }

    private void SkyblockShimmer(int originX, GenerationTileFraming1458 framing)
    {
        double span = random.Next(40, 51);
        int steps = random.Next(3, 5);
        double x = originX, y = top, vx = SkyblockVelocity();
        while (span > 0 && steps-- > 0)
        {
            span -= random.Next(4);
            double radius = span * random.Next(80, 120) * .01, surface = y + 1;
            int minX = Math.Max(0, (int)(x - span * .5)), maxX = Math.Min(tiles.Dimensions.WidthTiles, (int)(x + span * .5));
            int minY = Math.Max(0, top - 1), maxY = Math.Min(tiles.Dimensions.HeightTiles, (int)(y + span * .5));
            for (int column = minX; column < maxX; column++)
                for (int row = minY; row < maxY; row++)
                {
                    if (!SkyblockInside(column, row, 5)) continue;
                    if (row > surface)
                    {
                        double distance = SkyblockDistance(column - x, Math.Abs(row - y) * 1.3);
                        if (distance < radius * .4)
                        {
                            if ((At(column, row).LiquidAmount == 0 || At(column, row).LiquidKind != WorldLiquidKind.Shimmer)) { At(column, row).Type = 56; At(column, row).Flags |= WorldTileFlags.Active; }
                            if ((At(column, row + 1).LiquidAmount == 0 || At(column, row + 1).LiquidKind != WorldLiquidKind.Shimmer)) { At(column, row + 1).Type = 56; At(column, row + 1).Flags |= WorldTileFlags.Active; }
                            framing.SquareTileFrame(column, row);
                        }
                        if (distance < radius * .3)
                        {
                            SkyblockFillShimmer(column, row);
                            if (At(column, row - 1).IsActive) SkyblockFillShimmer(column, row - 1);
                            framing.SquareTileFrame(column, row);
                        }
                    }
                    if (row > surface - 5 && row <= surface) At(column, row).Flags &= ~WorldTileFlags.Active;
                }
            x += vx;
            vx = Math.Clamp(vx + random.Next(-20, 21) * .05, -1, 1);
        }
    }

    private void SkyblockFurniture(int originX, bool shimmer, GenerationTileFraming1458 framing)
    {
        for (int fixture = 0; fixture < (shimmer ? 2 : 1); fixture++)
        {
            int attempts = 10000, range = shimmer ? 10 : 20;
            while (attempts > 0)
            {
                attempts--;
                if (attempts % (shimmer ? 50 : 20) == 0) range++;
                int x = Math.Clamp(random.Next(originX - range, originX + range), left, right), y = shimmer ? top - 30 : 6;
                if (!SkyblockInside(x, y, 5)) continue;
                while (!At(x, y).IsActive && y < tiles.Dimensions.HeightTiles - 10) y++;
                if (At(x, y).Type != Dirt && (!shimmer || At(x, y).Type != 56)) continue;
                y--;
                if (shimmer && (At(x, y).LiquidAmount != 0 || At(x + 2, y + 1).LiquidAmount != 0 || At(x - 1, y + 1).LiquidAmount != 0)) continue;
                if (shimmer && fixture == 1 && attempts > 2000 && SkyblockNearby(x, y, 658, 10)) continue;
                ushort type = !shimmer ? (ushort)220 : fixture == 0 ? (ushort)658 : (ushort)139;
                framing.TryPlaceSkyblockFixture(x, y, type, type == 139 ? 87 : 0);
                if (At(x, y - (shimmer ? 0 : 1)).Type != type) continue;
                if (shimmer) framing.SwitchSkyblockFixture(x, y);
                break;
            }
        }
    }

    private bool SkyblockNearby(int x, int y, ushort type, int radius)
    {
        for (int column = x - radius; column <= x + radius; column++)
            for (int row = y - radius; row <= y + radius; row++)
                if (SkyblockInside(column, row, 10) && At(column, row).IsActive && At(column, row).Type == type) return true;
        return false;
    }
    private double SkyblockVelocity() { double value; do value = random.Next(-20, 21) * .2; while (value > -2 && value < 2); return value; }
    private bool SkyblockInside(int x, int y, int margin) => x >= margin && x < tiles.Dimensions.WidthTiles - margin && y >= margin && y < tiles.Dimensions.HeightTiles - margin;
    private static double SkyblockDistance(double x, double y) => Math.Sqrt(x * x + y * y);
    private void SkyblockSet(int x, int y, ushort type, GenerationTileFraming1458 framing) { At(x, y).Flags |= WorldTileFlags.Active; At(x, y).Type = type; framing.SquareTileFrame(x, y); }
    private void SkyblockFillShimmer(int x, int y) { At(x, y).Flags &= ~WorldTileFlags.Active; At(x, y).LiquidAmount = byte.MaxValue; At(x, y).LiquidKind = WorldLiquidKind.Shimmer; }
}
