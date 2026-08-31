using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Runtime-owned TerrariaServer 1.4.5.8 King Slime world queries. Teleport candidate enumeration mirrors
/// BuildKingSlimeTeleportCache/AddKingSlimeTeleportCacheTiles: the 10/7 ring is tried first, then 6/2, with
/// a uniformly selected candidate. Anti-cheese and empty-cache fallback use the closest target bottom. The same
/// world owner also exposes the exact rectangle-based Collision.CanHit query required by Good World Eye AI.
/// </summary>
internal sealed class VanillaKingSlimeWorldEnvironment :
    IVanillaKingSlimeEnvironment,
    IVanillaEyeOfCthulhuEnvironment
{
    private const int TileSize = 16;
    private const float BasePlayerHeight = 42f;
    private const int CacheCapacity = 512;

    private readonly WorldTileStore _tiles;
    private readonly IVanillaNpcRandom _random;
    private readonly TeleportTile[] _cache = new TeleportTile[CacheCapacity];
    private int _cacheCount;

    public VanillaKingSlimeWorldEnvironment(
        WorldTileStore tiles,
        IVanillaNpcRandom? random = null)
    {
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _random = random ?? new SystemVanillaNpcRandom();
    }

    public float WorldPixelWidth => checked(_tiles.Dimensions.WidthTiles * (float)TileSize);

    public float WorldPixelHeight => checked(_tiles.Dimensions.HeightTiles * (float)TileSize);

    public bool CanHitLine(float fromX, float fromY, float toX, float toY) =>
        VanillaWorldLineOfSight.CanHitLine(_tiles, fromX, fromY, toX, toY);

    public bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight) =>
        VanillaWorldCanHit.HasLineOfSight(
            _tiles,
            sourcePositionX,
            sourcePositionY,
            sourceWidth,
            sourceHeight,
            targetPositionX,
            targetPositionY,
            targetWidth,
            targetHeight);

    public bool TryResolveTeleport(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target,
        bool antiCheese,
        out VanillaKingSlimeTeleportDestination destination)
    {
        if (!target.Active || target.Ghost || target.Dead ||
            !float.IsFinite(target.CenterX) || !float.IsFinite(target.CenterY) ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            destination = default;
            return false;
        }

        if (antiCheese)
        {
            destination = PlayerBottom(in target);
            return true;
        }

        int targetTileX = (int)(target.CenterX / TileSize);
        int targetTileY = (int)(target.CenterY / TileSize);
        if (TryBuildCache(targetTileX, targetTileY, outerRange: 10, innerRange: 7, hitbox.Height, in target) ||
            TryBuildCache(targetTileX, targetTileY, outerRange: 6, innerRange: 2, hitbox.Height, in target))
        {
            TeleportTile tile = _cache[_random.NextInt32(0, _cacheCount)];
            destination = new VanillaKingSlimeTeleportDestination(
                BottomX: tile.X * TileSize + TileSize * 0.5f,
                BottomY: tile.Y * TileSize);
            return true;
        }

        destination = PlayerBottom(in target);
        return true;
    }

    private bool TryBuildCache(
        int targetTileX,
        int targetTileY,
        int outerRange,
        int innerRange,
        int npcHeight,
        in VanillaNpcTargetCandidate target)
    {
        _cacheCount = 0;
        AddCacheTiles(targetTileX - outerRange, targetTileX - innerRange, targetTileY - outerRange, targetTileY + outerRange, npcHeight, in target);
        AddCacheTiles(targetTileX + innerRange, targetTileX + outerRange, targetTileY - outerRange, targetTileY + outerRange, npcHeight, in target);
        AddCacheTiles(targetTileX - innerRange, targetTileX + innerRange, targetTileY - outerRange, targetTileY - innerRange, npcHeight, in target);
        AddCacheTiles(targetTileX - innerRange, targetTileX + innerRange, targetTileY + innerRange, targetTileY + outerRange, npcHeight, in target);
        return _cacheCount > 0;
    }

    private void AddCacheTiles(
        int x0,
        int x1,
        int y0,
        int y1,
        int npcHeight,
        in VanillaNpcTargetCandidate target)
    {
        int maxX = _tiles.Dimensions.WidthTiles - 1;
        int maxY = _tiles.Dimensions.HeightTiles - 1;
        int startX = Math.Max(0, x0);
        int endX = Math.Min(maxX, x1);
        int startY = Math.Max(1, y0);
        int endY = Math.Min(maxY, y1);

        for (int x = startX; x <= endX && _cacheCount < _cache.Length; x++)
        {
            for (int y = startY; y <= endY && _cacheCount < _cache.Length; y++)
            {
                WorldTile support = _tiles.Get(x, y);
                if (!IsTeleportSupport(in support) || IsFullSolid(_tiles.Get(x, y - 1)))
                    continue;
                if (support.LiquidAmount > 0 && support.LiquidKind == WorldLiquidKind.Lava)
                    continue;

                float lineX = x * TileSize + TileSize * 0.5f;
                float lineY = y * TileSize - npcHeight * 0.5f;
                if (!CanHitLine(lineX, lineY, target.CenterX, target.CenterY))
                    continue;

                _cache[_cacheCount++] = new TeleportTile(x, y);
            }
        }
    }

    private static bool IsTeleportSupport(in WorldTile tile)
    {
        if (!tile.IsActive || (tile.Flags & WorldTileFlags.Inactive) != 0)
            return false;

        return (VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)) ||
               IsPlatform(tile.Type);
    }

    private static bool IsFullSolid(in WorldTile tile) =>
        tile.IsActive &&
        (tile.Flags & WorldTileFlags.Inactive) == 0 &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    // Terraria 1.4.5.8 TileID.Sets.Platforms = Factory.CreateBoolSet(19, 427, 435, 436, 437, 438, 439).
    private static bool IsPlatform(ushort type) => type is 19 or 427 or 435 or 436 or 437 or 438 or 439;

    private static VanillaKingSlimeTeleportDestination PlayerBottom(in VanillaNpcTargetCandidate target) =>
        new(target.CenterX, target.CenterY + BasePlayerHeight * 0.5f);

    private readonly record struct TeleportTile(int X, int Y);
}
