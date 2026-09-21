using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// WorldTileStore-backed source queries for TerrariaServer 1.4.5.8 Wall of Flesh AI_027/028 and the Good-World
/// Fire Imp support branch. It exposes only collision/placement facts; presentation state remains client-owned.
/// </summary>
internal sealed class VanillaWallOfFleshWorldEnvironment : IVanillaWallOfFleshEnvironment, IVanillaDungeonCasterEnvironment
{
    private const int TileSize = 16;
    private readonly WorldTileStore tiles;
    // FNA's ref/out Rectangle.Union aliases its first input in NPC.AI_AttemptToFindTeleportSpot.
    private readonly bool fnaRectangleUnion;

    public VanillaWallOfFleshWorldEnvironment(WorldTileStore tiles, bool? useFnaRectangleUnion = null)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        fnaRectangleUnion = useFnaRectangleUnion ?? !OperatingSystem.IsWindows();
    }

    public int WorldWidthTiles => tiles.Dimensions.WidthTiles;
    public int WorldHeightTiles => tiles.Dimensions.HeightTiles;
    public int UnderworldLayerTiles => Math.Max(0, WorldHeightTiles - 200);

    public bool TryResolveCorridor(
        float positionX,
        float positionY,
        int width,
        int height,
        out float topPixels,
        out float bottomPixels)
    {
        topPixels = 0f;
        bottomPixels = 0f;
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY) || width <= 0 || height <= 0)
            return false;

        int minBand = UnderworldLayerTiles + 10;
        int maxBand = Math.Min(WorldHeightTiles - 10, minBand + 70);
        if (minBand >= maxBand)
            return false;

        int left = Math.Clamp((int)(positionX / TileSize), 2, Math.Max(2, WorldWidthTiles - 3));
        int right = Math.Clamp((int)((positionX + width) / TileSize), left, Math.Max(left, WorldWidthTiles - 3));
        int centerY = Math.Clamp((int)((positionY + height * .5f) / TileSize), minBand, maxBand);

        int lowerContacts = 0;
        int lower = centerY + 7;
        while (lowerContacts < 15 && lower < WorldHeightTiles - 10)
        {
            lower++;
            if (lower > WorldHeightTiles - 10) { lower = WorldHeightTiles - 10; break; }
            if (lower < minBand) continue;
            for (int x = left; x <= right; x++)
            {
                if (IsSolidOrLiquid(x, lower))
                    lowerContacts++;
            }
        }
        lower += 4;

        int upperContacts = 0;
        int upper = centerY - 7;
        while (upperContacts < 15 && upper < WorldHeightTiles - 10)
        {
            upper--;
            if (upper <= 10) { upper = 10; break; }
            if (upper > maxBand) continue;
            if (upper < minBand) { upper = minBand; break; }
            for (int x = left; x <= right; x++)
            {
                if (IsSolidOrLiquid(x, upper))
                    upperContacts++;
            }
        }
        upper -= 4;

        topPixels = Math.Clamp(upper * (float)TileSize, minBand * (float)TileSize, maxBand * (float)TileSize);
        bottomPixels = Math.Clamp(lower * (float)TileSize, minBand * (float)TileSize, maxBand * (float)TileSize);
        if (topPixels > bottomPixels - 160f)
            topPixels = bottomPixels - 160f;
        if (bottomPixels < topPixels + 160f)
            bottomPixels = topPixels + 160f;
        return true;
    }

    public bool CanHit(
        float sourceX,
        float sourceY,
        int sourceWidth,
        int sourceHeight,
        float targetX,
        float targetY,
        int targetWidth,
        int targetHeight) =>
        VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourceX,
            sourceY,
            sourceWidth,
            sourceHeight,
            targetX,
            targetY,
            targetWidth,
            targetHeight);

    public bool TryFindGroundSpawn(int tileX, int startTileY, out int bottomX, out int bottomY)
    {
        bottomX = 0;
        bottomY = 0;
        if (tileX < 2 || tileX >= WorldWidthTiles - 2)
            return false;

        int y = Math.Clamp(startTileY, 2, WorldHeightTiles - 11);
        while (y < WorldHeightTiles - 10 && !IsFullSolid(tileX, y))
            y++;
        y--;
        if (y < 2 || IsFullSolid(tileX, y) || !IsFullSolid(tileX, y + 1))
            return false;

        bottomX = tileX * TileSize + TileSize / 2;
        bottomY = (y + 1) * TileSize;
        return true;
    }

    public bool TryFindTeleportSpot(
        float npcCenterX,
        float npcCenterY,
        int targetTileX,
        int targetTileY,
        ReadOnlySpan<VanillaNpcTargetCandidate> players,
        IVanillaNpcRandom random,
        out int tileX,
        out int tileY)
    {
        tileX = tileY = 0;
        if (!float.IsFinite(npcCenterX) || !float.IsFinite(npcCenterY) ||
            targetTileX < 20 || targetTileX >= WorldWidthTiles - 20 ||
            targetTileY < 20 || targetTileY >= WorldHeightTiles - 20)
        {
            return false;
        }

        int ownX = (int)npcCenterX / TileSize;
        int ownY = (int)npcCenterY / TileSize;
        if (Math.Abs((long)ownX * TileSize - targetTileX * (long)TileSize) +
            Math.Abs((long)ownY * TileSize - targetTileY * (long)TileSize) > 2_000L)
        {
            return false;
        }

        for (int attempt = 0; attempt < 100; attempt++)
        {
            int x = random.NextInt32(targetTileX - 20, targetTileX + 21);
            int startY = random.NextInt32(targetTileY - 20, targetTileY + 21);
            for (int y = startY; y < targetTileY + 20; y++)
            {
                WorldTile floor = tiles.Get(x, y);
                if ((y >= ownY - 1 && y <= ownY + 1 && x >= ownX - 1 && x <= ownX + 1) ||
                    !floor.IsActive || floor.IsActuated)
                {
                    continue;
                }

                if (tiles.Get(x, y - 1).LiquidKind == WorldLiquidKind.Lava ||
                    !VanillaTileCollisionCatalog.IsSolid(floor.TileType) ||
                    SolidClearance(x, y))
                    continue;
                if (IntersectsPlayerTeleportSafety(x, y, players))
                    break;

                tileX = x;
                tileY = y;
                return true;
            }
        }

        return false;
    }

    public bool TryFindDungeonCasterTeleportSpot(
        float npcCenterX,
        float npcCenterY,
        int targetTileX,
        int targetTileY,
        bool skeletronActive,
        ReadOnlySpan<VanillaNpcTargetCandidate> players,
        IVanillaNpcRandom random,
        out int tileX,
        out int tileY) =>
        new VanillaCasterWorldEnvironment(tiles, fnaRectangleUnion).TryFindTeleportSpot(
            npcCenterX, npcCenterY, targetTileX, targetTileY, skeletronActive, players, random, out tileX, out tileY);

    private bool SolidClearance(int x, int floorY)
    {
        if (x - 1 < 0 || x + 1 >= WorldWidthTiles || floorY - 4 < 0 || floorY - 1 >= WorldHeightTiles)
            return true;
        for (int cx = x - 1; cx <= x + 1; cx++)
            for (int cy = floorY - 4; cy <= floorY - 1; cy++)
                if (IsFullSolid(cx, cy))
                    return true;
        return false;
    }

    private bool IntersectsPlayerTeleportSafety(
        int tileX,
        int tileY,
        ReadOnlySpan<VanillaNpcTargetCandidate> players)
    {
        int left = tileX * TileSize - 80;
        int top = tileY * TileSize - 80;
        int right = tileX * TileSize + 96;
        int bottom = tileY * TileSize + 96;
        foreach (VanillaNpcTargetCandidate player in players)
        {
            if (!player.Active || player.Dead)
                continue;

            int width = (int)player.Width;
            int height = (int)player.Height;
            int playerLeft = (int)(player.CenterX - player.Width * .5f);
            int playerTop = (int)(player.CenterY - player.Height * .5f);
            int deltaX = (int)(player.VelocityX * 20f);
            int deltaY = (int)(player.VelocityY * 20f);
            int sweptLeft = Math.Min(playerLeft, playerLeft + deltaX);
            int sweptTop = Math.Min(playerTop, playerTop + deltaY);
            int sweptRight = (fnaRectangleUnion ? playerLeft : Math.Max(playerLeft, playerLeft + deltaX)) + width;
            int sweptBottom = (fnaRectangleUnion ? playerTop : Math.Max(playerTop, playerTop + deltaY)) + height;
            if (sweptLeft < right && sweptRight > left && sweptTop < bottom && sweptBottom > top)
                return true;
        }

        return false;
    }

    private bool IsSolidOrLiquid(int x, int y)
    {
        if ((uint)x >= (uint)WorldWidthTiles || (uint)y >= (uint)WorldHeightTiles)
            return true;
        WorldTile tile = tiles.Get(x, y);
        return tile.LiquidAmount > 0 ||
               (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType));
    }

    private bool IsFullSolid(int x, int y)
    {
        if ((uint)x >= (uint)WorldWidthTiles || (uint)y >= (uint)WorldHeightTiles)
            return true;
        WorldTile tile = tiles.Get(x, y);
        return tile.IsActive && !tile.IsActuated &&
               VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
               !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }
}
