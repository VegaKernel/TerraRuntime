using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Source-backed TerrariaServer 1.4.5.8 Surface Ore and Stone pass.</summary>
internal sealed class SurfaceOreStone1458(
    WorldTileStore store, IWorldGenerationVanillaRandom random,
    double worldSurfaceLow, double worldSurface, int copperOre, int ironOre,
    CancellationToken cancellation)
{
    private const int BeachDistance = 380;
    private readonly List<int> oreAnchors = [];
    public int Changed { get; private set; }

    public void Apply()
    {
        int width = store.Dimensions.WidthTiles;
        int oreOffers = random.Next(width * 5 / 4200, width * 10 / 4200);
        for (int offer = 0; offer < oreOffers; offer++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int attempts = width / 420; attempts > 0; attempts--)
            {
                int x = random.Next(BeachDistance, width - BeachDistance);
                while (x >= width * .48 && x <= width * .52)
                    x = random.Next(BeachDistance, width - BeachDistance);
                int y = random.Next((int)worldSurfaceLow, (int)worldSurface);
                if (oreAnchors.Any(anchor => Math.Abs(x - anchor) < 200) || !OrePatch(x, y))
                    continue;
                oreAnchors.Add(x);
                break;
            }
        }

        int stoneOffers = random.Next(1, width * 7 / 4200);
        for (int offer = 0; offer < stoneOffers; offer++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int attempts = width / 420; attempts > 0; attempts--)
            {
                int x = random.Next(BeachDistance, width - BeachDistance);
                while (x >= width * .47 && x <= width * .53)
                    x = random.Next(BeachDistance, width - BeachDistance);
                int y = random.Next((int)worldSurfaceLow, (int)worldSurface);
                if (!oreAnchors.Any(anchor => Math.Abs(x - anchor) < 100) && StonePatch(x, y))
                    break;
            }
        }
    }

    private bool OrePatch(int x, int y)
    {
        ushort ore = (ushort)(random.Next(3) == 0 ? ironOre : copperOre);
        if (!TryFindSurface(x, ref y, rejectEvil: false)) return false;
        y += random.Next(2);
        SetOre(x, y, ore);
        int startY = y;
        while (y < startY + random.Next(8, 13))
        {
            x += random.Next(-1, 2);
            y += random.Next(1, 3);
            if (random.Next(3) == 0) y++;
            SetOre(x, y, ore);
            if (random.Next(4) == 0)
                SetOre(x + random.Next(-2, 3), y + random.Next(2), ore);
        }
        RunOreTail(x, y, ore);
        return true;
    }

    private bool StonePatch(int x, int y)
    {
        if (!TryFindSurface(x, ref y, rejectEvil: true)) return false;
        double velocityX = random.NextDouble() * .6 - .3;
        double velocityY = random.NextDouble() * .5 + .5;
        double strength = random.Next(13, 18);
        int steps = random.Next(13, 19);
        if (random.Next(3) == 0) strength += random.Next(3);
        if (random.Next(3) == 0) steps += random.Next(3);
        RunStoneTail(x, y, velocityX, velocityY, strength, steps);
        return true;
    }

    private bool TryFindSurface(int x, ref int y, bool rejectEvil)
    {
        while (!Solid(x, y))
            if (++y > worldSurface) return false;
        if (!Grass(At(x, y).Type) || !Grass(At(x - 1, y).Type) ||
            !Grass(At(x + 1, y).Type) || At(x, y).Wall != 0)
            return false;
        for (int tx = x - 10; tx <= x + 10; tx++)
        for (int ty = y + 7; ty <= y + 30; ty++)
        {
            WorldTile tile = At(tx, ty);
            if (!tile.IsActive || DungeonGenerationTiles1458.IsDungeonTile(tile.Type) ||
                Cloud(tile.Type) || Sand(tile.Type) || tile.Wall == 0 ||
                (rejectEvil && tile.Type is 199 or 23))
                return false;
        }
        return true;
    }

    private void RunOreTail(int anchorX, int anchorY, ushort ore)
    {
        double positionX = anchorX, positionY = anchorY;
        double velocityX = random.NextDouble() * .6 - .3;
        double velocityY = random.NextDouble() * .5 + .5;
        double strength = random.Next(5, 9);
        int steps = random.Next(9, 14);
        if (random.Next(3) == 0) strength += random.Next(2);
        if (random.Next(3) == 0) steps += random.Next(2);
        while (steps-- > 0)
        {
            for (int x = anchorX - (int)strength * 4; x <= anchorX + strength * 4; x++)
            for (int y = anchorY - (int)strength * 4; y <= anchorY + strength * 4; y++)
            {
                double hollowRadius = strength * (.5 + random.NextDouble() * .5) * .1;
                double oreRadius = strength * (.7 + random.NextDouble() * .6) * .3;
                if (random.Next(8) == 0) oreRadius *= 2;
                double distance = Math.Sqrt((positionX - x) * (positionX - x) +
                                            (positionY - y) * (positionY - y));
                if (distance < hollowRadius)
                {
                    At(x, y).Flags &= ~WorldTileFlags.Active;
                    Changed++;
                }
                else if (distance < oreRadius)
                {
                    ref WorldTile tile = ref At(x, y);
                    tile.Type = ore;
                    if (random.Next(4) == 0) tile.Flags |= WorldTileFlags.Active;
                    OreHelper(x, y);
                    Changed++;
                }
            }
            positionX += velocityX;
            positionY += velocityY;
            velocityX += random.NextDouble() * .2 - .1;
            velocityY += random.NextDouble() * .2 - .1;
            // WorldGen.OrePatch calls Utils.Clamp without assigning the result.
        }
    }

    private void RunStoneTail(int anchorX, int anchorY, double velocityX, double velocityY,
        double strength, int steps)
    {
        double positionX = anchorX, positionY = anchorY;
        while (steps-- > 0)
        {
            for (int x = anchorX - (int)strength * 4; x <= anchorX + strength * 4; x++)
            for (int y = anchorY - (int)strength * 4; y <= anchorY + strength * 4; y++)
            {
                double radius = strength * (.7 + random.NextDouble() * .6) * .3;
                if (random.Next(8) == 0) radius *= 2;
                double distance = Math.Sqrt((positionX - x) * (positionX - x) +
                                            (positionY - y) * (positionY - y));
                if (distance < radius * 2 && !At(x, y).IsActive &&
                    At(x, y + 1) is { IsActive: true, Type: 1 } && random.Next(7) == 0 &&
                    Solid(x - 1, y + 1) && Solid(x + 1, y + 1))
                    PlaceStoneDecoration(x, y);
                if (distance < radius && VanillaTileCollisionCatalog.IsSolid(At(x, y).TileType))
                {
                    At(x, y).Type = 1;
                    Changed++;
                }
            }
            positionX += velocityX;
            positionY += velocityY;
            velocityX += random.NextDouble() * .2 - .1;
            velocityY += random.NextDouble() * .2 - .1;
        }
    }

    private void PlaceStoneDecoration(int x, int y)
    {
        if (random.Next(3) != 0)
            GenerationDecorationPlacement1458.TryPlaceTile3x2(
                store, random, x, y, 186, random.Next(7, 13));
        if (random.Next(3) != 0)
            GenerationDecorationPlacement1458.TryPlaceSmallPile(store, x, y, random.Next(6), 1);
        GenerationDecorationPlacement1458.TryPlaceSmallPile(store, x, y, random.Next(6), 0);
    }

    private void SetOre(int x, int y, ushort ore)
    {
        ref WorldTile tile = ref At(x, y);
        tile.Type = ore;
        tile.Flags |= WorldTileFlags.Active;
        OreHelper(x, y);
        Changed++;
    }

    private void OreHelper(int x, int y)
    {
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
            if (At(tx, ty).Type is 1 or 40) At(tx, ty).Type = 0;
    }

    private bool Solid(int x, int y)
    {
        WorldTile tile = At(x, y);
        return tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
               VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
               !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }

    private static bool Grass(ushort type) => type is 2 or 23 or 109 or 199 or 477 or 492;
    private static bool Cloud(ushort type) => type is 189 or 196 or 460 or 717 or 718 or 719;
    private static bool Sand(ushort type) => type is 53 or 112 or 116 or 234;
    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
