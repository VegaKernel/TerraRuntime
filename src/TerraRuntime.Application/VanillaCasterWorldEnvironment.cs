using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Read-only owned-world implementation of NPC.AI_AttemptToFindTeleportSpot for type32, 1.4.5.8.</summary>
internal sealed class VanillaCasterWorldEnvironment(WorldTileStore tiles, bool? useFnaRectangleUnion = null) : IVanillaCasterEnvironment
{
    // FNA's ref/out Rectangle.Union aliases its first input in this original call. XNA caches its input edges.
    private readonly bool fnaRectangleUnion = useFnaRectangleUnion ?? !OperatingSystem.IsWindows();

    public bool TryFindTeleportSpot(float npcCenterX, float npcCenterY, int targetTileX, int targetTileY,
        bool skeletronActive, ReadOnlySpan<VanillaNpcTargetCandidate> players, IVanillaNpcRandom random,
        out int tileX, out int tileY)
    {
        tileX = tileY = 0;
        int width = tiles.Dimensions.WidthTiles, height = tiles.Dimensions.HeightTiles;
        // The original assumes interior game coordinates. Invalid host inputs fail closed before any indexing/draw.
        if (!float.IsFinite(npcCenterX) || !float.IsFinite(npcCenterY) ||
            targetTileX < 20 || targetTileX >= width - 20 || targetTileY < 20 || targetTileY >= height - 20)
            return false;
        int ownX = (int)npcCenterX / 16, ownY = (int)npcCenterY / 16;
        if (Math.Abs((long)ownX * 16 - targetTileX * 16L) + Math.Abs((long)ownY * 16 - targetTileY * 16L) > 2000)
            return false;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            int x = random.NextInt32(targetTileX - 20, targetTileX + 21);
            int startY = random.NextInt32(targetTileY - 20, targetTileY + 21);
            for (int y = startY; y < targetTileY + 20; y++)
            {
                if (Math.Abs(y - ownY) <= 1 && Math.Abs(x - ownX) <= 1) continue;
                WorldTile floor = tiles.Get(x, y);
                if (!floor.IsActive || floor.IsActuated || y == 0) continue;
                WorldTile above = tiles.Get(x, y - 1);
                bool dungeon = VanillaWallDefinitionCatalog.TryGet(above.WallType, out var wall) && wall.IsDungeonWall;
                // Source's dungeon exemption is an alternative branch to lava rejection, even with zero liquid.
                if (!dungeon)
                {
                    if (!skeletronActive) continue;
                }
                else if (above.LiquidKind == WorldLiquidKind.Lava) continue;
                if (!VanillaTileCollisionCatalog.IsSolid(floor.TileType) || SolidClearance(x, y)) continue;
                bool overlaps = false;
                foreach (var player in players)
                {
                    if (!player.Active || player.Dead) continue;
                    int px = (int)(player.CenterX - VanillaPlayerHitboxFacts.BaseWidth * .5f);
                    int py = (int)(player.CenterY - VanillaPlayerHitboxFacts.BaseHeight * .5f);
                    int dx = (int)(player.VelocityX * 20f), dy = (int)(player.VelocityY * 20f);
                    int left = Math.Min(px, px + dx), top = Math.Min(py, py + dy);
                    int right = (fnaRectangleUnion ? px : Math.Max(px, px + dx)) + (int)VanillaPlayerHitboxFacts.BaseWidth;
                    int bottom = (fnaRectangleUnion ? py : Math.Max(py, py + dy)) + (int)VanillaPlayerHitboxFacts.BaseHeight;
                    if (left < x * 16 + 96 && right > x * 16 - 80 && top < y * 16 + 96 && bottom > y * 16 - 80)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps) { tileX = x; tileY = y; return true; }
                // Once geometry succeeds, a player overlap restarts the random attempt, not the vertical scan.
                break;
            }
        }
        return false;
    }

    private bool SolidClearance(int x, int floorY)
    {
        if (x - 1 < 0 || x + 1 >= tiles.Dimensions.WidthTiles || floorY - 4 < 0 ||
            floorY - 1 >= tiles.Dimensions.HeightTiles - 40) return true;
        for (int cx = x - 1; cx <= x + 1; cx++)
            for (int cy = floorY - 4; cy <= floorY - 1; cy++)
            {
                WorldTile tile = tiles.Get(cx, cy);
                if (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                    !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)) return true;
            }
        return false;
    }
}
