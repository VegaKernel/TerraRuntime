using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct FallingTerrain1458(float X, float Y,
    float VelocityX, float VelocityY, float MovementX, float MovementY,
    ProjectileLiquidState Liquid, byte WetCount, byte ShimmerColourDraws, bool HasImpact);

internal sealed partial class VanillaProjectileWorldMotionResolver
{
    internal bool TryCaptureFallingTerrain(ToolWorldTileOverlay1458 overlay, ProjectileTypeId type,
        float x, float y, float beforeVx, float beforeVy, float vx, float vy, bool tileCollide,
        in ProjectileLifecycleState life, in VanillaProjectileBehaviorContext wind, out FallingTerrain1458 result)
    {
        result = default;
        if (!ReferenceEquals(tiles, overlay.Tiles) || !overlay.IsCurrentOwned ||
            !VanillaFallingBlock1458.TryGetTile(type, out _) ||
            !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(vx) || !float.IsFinite(vy) ||
            MathF.Abs(vx) > 10000f || MathF.Abs(vy) > 10000f) return false;
        WorldTile Read(int tx, int ty) => overlay.TryRead(tx, ty, out var tile) ? tile : default;
        // AI10 is admitted by ShouldUseWindPhysics and none of the eleven source
        // identities has a WindPhysicsImmunity override. Read the staged wall.
        if (wind.WindPhysics && (double)(y + 5f) < worldSurfaceTiles * 16d)
        {
            int tx = (int)((x + 5f) / 16f), ty = (int)((y + 5f) / 16f);
            if ((uint)tx < (uint)tiles.Dimensions.WidthTiles && (uint)ty < (uint)tiles.Dimensions.HeightTiles &&
                Read(tx, ty).Wall == 0)
            {
                float delta = wind.WindSpeedCurrent * wind.WindPhysicsStrength;
                if (MathF.Abs(vx) < 16f && (vx > 0f && wind.WindSpeedCurrent < 0f ||
                    vx < 0f && wind.WindSpeedCurrent > 0f || MathF.Abs(vx) < MathF.Abs(delta) * 180f)) vx += delta;
            }
        }
        var contacts = VanillaWorldCollision.GetLiquidContacts(tiles, x, y, 10, 10, Read);
        bool lava = VanillaWorldCollision.LavaCollision(tiles, x, y, 10, 10, Read);
        var before = life.Liquid;
        bool shimmer = before.ShimmerWet || contacts.Shimmer;
        int counter = life.SourceWetCount;
        byte colours = 0;
        ProjectileLiquidState liquid;
        if (contacts.Wet)
        {
            if (!before.Wet && counter == 0 && !lava && shimmer) colours = 10;
            liquid = new(true, before.LavaWet || lava, before.HoneyWet || contacts.Honey, shimmer);
        }
        else
        {
            if (before.Wet && counter == 0)
            {
                counter = 10;
                if (!before.LavaWet && before.ShimmerWet) colours = 10;
            }
            liquid = default;
        }
        float clampedX = vx, clampedY = vy;
        if (tileCollide)
        {
            // AI10 bypasses the ordinary wet/stride/slope branches entirely.
            var collision = VanillaWorldCollision.TileCollision(tiles, x, y, vx, vy,
                10, 10, false, false, Read);
            clampedX = collision.VelocityX; clampedY = collision.VelocityY;
            if (type.Value is >= 411 and <= 414)
                ResolveFallingCoinMaterials(overlay, x, y, ref clampedX, ref clampedY);
        }
        if (!overlay.IsCurrentOwned) return false;
        result = new(x, y, clampedX, clampedY,
            liquid.Wet ? beforeVx : clampedX, liquid.Wet ? beforeVy : clampedY,
            liquid, (byte)Math.Max(0, counter - 1), colours,
            tileCollide && (clampedX != vx || clampedY != vy));
        return true;
    }

    private static void ResolveFallingCoinMaterials(ToolWorldTileOverlay1458 overlay,
        float x, float y, ref float vx, ref float vy)
    {
        float incomingX = vx, incomingY = vy;
        float destinationX = x + incomingX, destinationY = y + incomingY;
        int minX = Math.Max(0, (int)(x / 16f) - 1);
        int maxX = Math.Min(overlay.Tiles.Dimensions.WidthTiles, (int)((x + 10f) / 16f) + 2);
        int minY = Math.Max(0, (int)(y / 16f) - 1);
        int maxY = Math.Min(overlay.Tiles.Dimensions.HeightTiles - 40, (int)((y + 10f) / 16f) + 2);
        int sideX = -1, sideY = -1, verticalX = -1, verticalY = -1;
        for (int tx = minX; tx < maxX; tx++)
            for (int ty = minY; ty < maxY; ty++)
            {
                if (!overlay.TryRead(tx, ty, out var tile)) return;
                // Specific-set collision intentionally includes actuated tiles.
                if (!tile.IsActive || !VanillaFallingBlock1458.TryGetProjectile(tile.TileType, out _)) continue;
                float left = tx * 16f, top = ty * 16f + (tile.IsHalfBrick ? 8f : 0f);
                float height = tile.IsHalfBrick ? 8f : 16f;
                if (destinationX + 10f <= left || destinationX >= left + 16f ||
                    destinationY + 10f <= top || destinationY >= top + height) continue;
                if (y + 10f <= top)
                {
                    verticalX = tx; verticalY = ty;
                    if (verticalX != sideX) vy = top - (y + 10f);
                }
                else if (x + 10f <= left && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))
                {
                    sideX = tx; sideY = ty;
                    if (sideY != verticalY) vx = left - (x + 10f);
                    if (verticalX == sideX) vy = incomingY;
                }
                else if (x >= left + 16f && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))
                {
                    sideX = tx; sideY = ty;
                    if (sideY != verticalY) vx = left + 16f - x;
                    if (verticalX == sideX) vy = incomingY;
                }
                else if (y >= top + height && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))
                {
                    verticalX = tx; verticalY = ty;
                    vy = top + height - y + .01f;
                    if (verticalY == sideY) vx = incomingX + .01f;
                }
            }
    }
}

internal sealed partial class VanillaProjectileWorldStateStepper
{
    internal bool TryCaptureFallingTerrain(ToolWorldTileOverlay1458 overlay, ProjectileTypeId type,
        float x, float y, float beforeVx, float beforeVy, float vx, float vy, bool tileCollide,
        in ProjectileLifecycleState life, in VanillaProjectileBehaviorContext wind, out FallingTerrain1458 terrain) =>
        worldMotion.TryCaptureFallingTerrain(overlay, type, x, y, beforeVx, beforeVy, vx, vy,
            tileCollide, in life, in wind, out terrain);
}
