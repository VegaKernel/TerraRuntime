using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonGlobalLights ceiling selection, lantern/chandelier placement and wired switches.</summary>
internal sealed class DungeonLights1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken,
    IReadOnlyList<DungeonPitTraps1458.Pit>? pits = null)
{
    public int Place(DungeonBounds1458 bounds, IReadOnlyList<int> variants, int color,
        DungeonDecorationProfile1458 decoration, DungeonBounds1458? entrance)
    {
        int width = tiles.Dimensions.WidthTiles, height = tiles.Dimensions.HeightTiles;
        if (color is < 0 or > 2 || variants.Count != 3 || bounds.Left < 10 || bounds.Top < 10 ||
            bounds.Right > width - 10 || bounds.Bottom > height - 10 || bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom)
            throw new InvalidOperationException("Invalid dungeon light settings.");
        int completed = 0, failures = 0, placed = 0, target = (int)(28f * ((float)width / 4200f));
        while (completed < target)
        {
            cancellationToken.ThrowIfCancellationRequested();
            failures++;
            int x = random.Next(bounds.Left, bounds.Right), seedY = random.Next(bounds.Top, bounds.Bottom);
            if (DungeonGenerationTiles1458.IsDungeonWall(At(x, seedY).Wall))
                for (int y = seedY; y > bounds.Top; y--)
                {
                    if (!At(x, y - 1).IsActive || !DungeonGenerationTiles1458.IsDungeonTile(At(x, y - 1).Type) ||
                        At(x, y).Wall == 350 || DungeonPitTraps1458.Contains(pits, x, y) ||
                        !(Contains(entrance, x, y) || DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall))) continue;
                    bool blocked = At(x - 1, y).IsActive || At(x + 1, y).IsActive ||
                        At(x - 1, y + 1).IsActive || At(x + 1, y + 1).IsActive || At(x, y + 2).IsActive;
                    for (int tx = Math.Max(1, x - 15); tx < Math.Min(width, x + 15); tx++)
                    for (int ty = Math.Max(1, y - 15); ty < Math.Min(height, y + 15); ty++)
                        if (At(tx, ty).Type is 42 or 34) blocked = true; // Source deliberately does not test active().
                    if (blocked) break;
                    bool chandelier = false;
                    if (random.Next(7) == 0)
                    {
                        bool floor = false;
                        for (int dy = 0; dy < 15; dy++)
                        {
                            if (y + dy >= height) throw new InvalidOperationException("Dungeon light search escaped world.");
                            if (Solid(x, y + dy)) { floor = true; break; }
                        }
                        if (!floor && DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall))
                        {
                            PlaceChandelier(x, y, At(x, y).Wall is >= 94 and <= 105 ? 53 : 27 + color);
                            chandelier = At(x, y).Type == 34;
                        }
                    }
                    if (!chandelier)
                    {
                        int variant = At(x, y).Wall == variants[2] ? 2 : At(x, y).Wall == variants[1] ? 1 : 0;
                        int style = decoration.GetLanternStyle(variant);
                        if (random.Next(3) == 0 && At(x, y).Wall is >= 94 and <= 105) style = 53;
                        PlaceLantern(x, y, style);
                    }
                    if (chandelier || At(x, y).Type == 42)
                    {
                        failures = 0; completed++; placed++;
                        GenerateSwitch(x, y);
                    }
                    break;
                }
            if (failures > 1000) { completed++; failures = 0; }
        }
        return placed;
    }

    private void PlaceLantern(int x, int y, int style)
    {
        if (!Ceiling(x, y - 1) || At(x, y + 1).IsActive) return;
        // Direct Place1x2Top, not PlaceTile: source may overwrite the anchor and keeps shape/paint/liquid.
        for (int row = 0; row < 2; row++)
        {
            ref WorldTile cell = ref At(x, y + row);
            cell.Flags |= WorldTileFlags.Active; cell.Type = 42;
            cell.FrameX = 0; cell.FrameY = (short)(style * 36 + row * 18);
        }
    }

    private void PlaceChandelier(int x, int y, int style)
    {
        if (!Ceiling(x, y - 1)) return;
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y; ty < y + 3; ty++) if (At(tx, ty).IsActive) return;
        int frameX = style / 36 * 108, frameY = style * 54 - style / 36 * 54 * 37;
        for (int column = 0; column < 3; column++)
        for (int row = 0; row < 3; row++)
        {
            ref WorldTile cell = ref At(x - 1 + column, y + row);
            cell.Flags |= WorldTileFlags.Active; cell.Type = 34;
            cell.FrameX = (short)(frameX + column * 18); cell.FrameY = (short)(frameY + row * 18);
        }
    }

    private void GenerateSwitch(int lightX, int lightY)
    {
        var placement = new DungeonObjectPlacement1458(tiles, random);
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = lightX + random.Next(-12, 13), y = lightY + random.Next(3, 21);
            if (x <= 1 || x >= tiles.Dimensions.WidthTiles - 2 || y <= 1 || y >= tiles.Dimensions.HeightTiles - 2)
                throw new InvalidOperationException("Dungeon switch search escaped world.");
            if (At(x, y).IsActive || At(x, y + 1).IsActive ||
                !DungeonGenerationTiles1458.IsDungeonTile(At(x - 1, y).Type) || !DungeonGenerationTiles1458.IsDungeonTile(At(x + 1, y).Type) ||
                !VanillaWorldCanHit.HasLineOfSight(tiles, x * 16, y * 16, 16, 16, lightX * 16, lightY * 16 + 1, 16, 16, crackedBricksSolid: false)) continue;
            if (!(Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y + 1)) ||
                !DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall)) continue;
            // Both side types are verified dungeon bricks; no tree/beam or wall-less attachment approximation.
            HellFortGenerator1458.ClearPlacementAnchor(ref At(x, y));
            At(x, y).Flags |= WorldTileFlags.Active; At(x, y).Type = 136;
            At(x, y).FrameX = At(x - 1, y).IsActive && !At(x - 1, y).IsActuated && At(x - 1, y).Shape is 0 or 3 or 5 ? (short)18 : (short)36;
            placement.FrameSquare(x, y);
            while (x != lightX || y != lightY)
            {
                At(x, y).Flags |= WorldTileFlags.WireRed;
                x += Math.Sign(lightX - x); At(x, y).Flags |= WorldTileFlags.WireRed;
                y += Math.Sign(lightY - y); At(x, y).Flags |= WorldTileFlags.WireRed;
            }
            if (random.Next(3) > 0) { At(lightX, lightY).FrameX = 18; At(lightX, lightY + 1).FrameX = 18; }
            break;
        }
    }

    private bool Ceiling(int x, int y) => At(x, y).IsActive && !At(x, y).IsActuated &&
        DungeonGenerationTiles1458.IsSolidType(At(x, y).TileType) && !VanillaTileCollisionCatalog.IsSolidTop(At(x, y).TileType);
    private bool Solid(int x, int y) => DungeonGenerationTiles1458.SolidTile(At(x, y));
    private static bool Contains(DungeonBounds1458? area, int x, int y) => area is { } a && x >= a.Left && x <= a.Right && y >= a.Top && y <= a.Bottom;
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
