using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Ordinary-world MagicConch / TeleportHelpers.RequestMagicConchTeleportPosition (1.4.5.8).
/// Crawls the ocean surface inland, not every dry cavity below it. No world mutation or client coordinates.
/// Skyblock lowTiles and equipment-dependent hazard exemptions are not admitted by this query.
/// </summary>
public static class VanillaOceanLanding1458
{
    public static bool TryFind(WorldTileStore tiles, double worldSurface, float currentX, int gravityDirection,
        out float landingX, out float landingY)
    {
        landingX = landingY = 0;
        if (!double.IsFinite(worldSurface) || worldSurface <= 50 || worldSurface >= tiles.Dimensions.HeightTiles - 40 ||
            !float.IsFinite(currentX) || gravityDirection is not (-1 or 1) || tiles.Dimensions.WidthTiles < 880)
            return false;

        // Player.MagicConch uses top-left X and integer maxTilesX/2. Try the OTHER ocean first.
        bool rightOcean = currentX / 16f < tiles.Dimensions.WidthTiles / 2;
        if (!TryCrawl(tiles, worldSurface, rightOcean, gravityDirection, out int x, out int y) &&
            !TryCrawl(tiles, worldSurface, !rightOcean, gravityDirection, out x, out y))
            return false;
        landingX = x * 16 + 8 - 10;
        landingY = y * 16 + 16 - 42; // landing point is the AIR cell, not the floor tile.
        return true;
    }

    private static bool TryCrawl(WorldTileStore tiles, double surface, bool rightOcean, int gravity,
        out int landingX, out int landingY)
    {
        landingX = landingY = 0;
        int x = rightOcean ? tiles.Dimensions.WidthTiles - 40 : 40;
        int y = 50;
        int inland = rightOcean ? -1 : 1;
        int crawled = 0;
        bool startedSolid = IsGround(tiles.Get(x, y));
        bool found = false;
        for (int attempts = 1; attempts <= 5000 && crawled < 400; attempts++)
        {
            // Vanilla accesses the padded world array. Refuse malformed edge terrain without an out-of-bounds probe.
            if (x < 2 || x >= tiles.Dimensions.WidthTiles - 2 || y < 4 || y >= tiles.Dimensions.HeightTiles - 41)
                return false;
            WorldTile here = tiles.Get(x, y), below = tiles.Get(x, y + 1);
            bool occupied = IsOccupied(here), occupiedBelow = IsOccupied(below);
            if (!occupied && !occupiedBelow &&
                !IsOccupied(tiles.Get(x - 1, y)) && !IsOccupied(tiles.Get(x + 1, y)) &&
                !IsOccupied(tiles.Get(x - 1, y + 1)) && !IsOccupied(tiles.Get(x + 1, y + 1)))
            {
                y++;
                continue;
            }
            if (BlockedBody(tiles, x, y, gravity) || occupied)
            {
                y += startedSolid ? 1 : -1;
                continue;
            }
            startedSolid = false;
            if (!BlockedBody(tiles, x, y + 1, gravity) && !occupiedBelow && y < surface)
            {
                y++;
                continue;
            }
            if ((below.LiquidAmount > 0 && !IsGround(below)) ||
                Dangerous(here, y, surface) || Dangerous(below, y + 1, surface) ||
                Dangerous(tiles.Get(x - inland, y), y, surface) ||
                Dangerous(tiles.Get(x - inland, y + 1), y + 1, surface))
            {
                x += inland;
                crawled++;
                continue;
            }
            if (y < 40) { y++; continue; }
            // Source advances once and finishes when no center support exists (e.g. a side ledge).
            if (!IsGround(below)) { x += inland; crawled++; }
            found = attempts < 5000 && crawled < 400;
            break;
        }
        if (!found || x < 40 || x >= tiles.Dimensions.WidthTiles - 40 || y < 40 || y >= tiles.Dimensions.HeightTiles - 40)
            return false;
        for (int drop = 0; drop < 20; drop++)
        {
            if (!IsOccupied(tiles.Get(x, y + drop))) continue;
            landingX = x;
            landingY = y + Math.Max(0, drop - 1);
            return true;
        }
        return false;
    }

    private static bool IsGround(WorldTile tile) => tile.IsActive && !tile.IsActuated &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        (!VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) || VanillaTileIds.IsPlatform(tile.TileType));

    private static bool IsOccupied(WorldTile tile) => IsGround(tile) || tile.LiquidAmount > 0;

    private static bool Dangerous(WorldTile tile, int y, double surface) =>
        (tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Lava) ||
        (y > surface && IsUnsafeLandingWall(tile.WallType)) ||
        (tile.IsActive && Hurts(tile.Type));

    private static bool IsUnsafeLandingWall(WallTypeId wall) =>
        wall == VanillaWallIds.LihzahrdBrickUnsafe ||
        wall == VanillaWallIds.BlueDungeonUnsafe || wall == VanillaWallIds.GreenDungeonUnsafe ||
        wall == VanillaWallIds.PinkDungeonUnsafe || wall == VanillaWallIds.BlueDungeonSlabUnsafe ||
        wall == VanillaWallIds.BlueDungeonTileUnsafe || wall == VanillaWallIds.PinkDungeonSlabUnsafe ||
        wall == VanillaWallIds.PinkDungeonTileUnsafe || wall == VanillaWallIds.GreenDungeonSlabUnsafe ||
        wall == VanillaWallIds.GreenDungeonTileUnsafe;

    // TileID.Sets.Suffocate, not all falling blocks (TerrariaServer 1.4.5.8).
    private static bool Suffocates(TileTypeId type) => type == VanillaTileIds.Sand ||
        type == VanillaTileIds.Ebonsand || type == VanillaTileIds.Pearlsand ||
        type == VanillaTileIds.Silt || type == VanillaTileIds.Slush || type == VanillaTileIds.Crimsand;

    // Collision.CanTileHurt + TileID.Sets: unknown types and conditional immunity/secret-seed hazards reject.
    private static bool Hurts(ushort type) => type >= VanillaTileCollisionCatalog.TileTypeCount ||
        type is 32 or 37 or 48 or 53 or 58 or 69 or 76 or 80 or 112 or 116 or 123 or 224 or 230 or 232 or 234 or 352 or 484 or 655 or 684 or 750;

    private static bool BlockedBody(WorldTileStore tiles, int x, int y, int gravity)
    {
        float px = x * 16 + 8 - 10, py = y * 16 + 15 - 42;
        if (VanillaWorldSolidCollision.Intersects(tiles, px, py, 20, 42) ||
            VanillaWorldCollision.LavaCollision(tiles, px, py, 20, 42))
            return true;
        // AnyHurtingTiles uses inclusive edges and a two-pixel inset only for the Suffocate set.
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 3; ty <= y + 1; ty++)
        {
            WorldTile tile = tiles.Get(tx, ty);
            if (!tile.IsActive || tile.IsActuated || !Hurts(tile.Type)) continue;
            int inset = Suffocates(tile.TileType) ? 2 : 0;
            float top = ty * 16 + (tile.Shape == 1 ? 8 : 0);
            if (px + 20 - inset >= tx * 16 && px + inset <= tx * 16 + 16 &&
                py + 42 - inset >= top - .5f && py + inset <= ty * 16 + 16.5f)
                return true;
        }
        ReadOnlySpan<(float X, float Y)> probes = [(16, 0), (-16, 0), (0, 16), (0, -16)];
        foreach (var probe in probes)
        {
            var motion = VanillaWorldCollision.TileCollision(tiles, px - probe.X, py - probe.Y,
                probe.X, probe.Y, 20, 42, fallThrough: true, fall2: true, gravDir: gravity);
            if (motion.VelocityX != probe.X || motion.VelocityY != probe.Y) return true;
        }
        return false;
    }
}
