using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Source-backed player top-left bounds from TerrariaServer 1.4.5.8 <c>Player.BordersMovement</c>.
/// Synthetic test worlds smaller than the two vanilla border bands fall back to body-inside-world bounds.
/// </summary>
internal static class VanillaPlayerWorldBounds1458
{
    internal const float BorderPixels = 640f;

    public static bool ContainsTopLeft(
        in WorldTileDimensions dimensions,
        float positionX,
        float positionY)
    {
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY))
            return false;

        GetBounds(in dimensions, out float horizontalBorder, out float verticalBorder, out float maximumX, out float maximumY);
        return maximumX >= horizontalBorder && maximumY >= verticalBorder &&
            positionX >= horizontalBorder && positionX <= maximumX &&
            positionY >= verticalBorder && positionY <= maximumY;
    }

    internal static bool IsNearLanding(in WorldTileDimensions dimensions, float landingX, float landingY, float x, float y)
    {
        if (!float.IsFinite(landingX) || !float.IsFinite(landingY) || !ContainsTopLeft(in dimensions, x, y)) return false;
        GetBounds(in dimensions, out float minX, out float minY, out float maxX, out float maxY);
        // A vanilla client runs BordersMovement before its acknowledgement. A trusted warp into
        // a border band must accept that clamped landing without accepting arbitrary old motion.
        float dx = x - Math.Clamp(landingX, minX, maxX), dy = y - Math.Clamp(landingY, minY, maxY);
        return float.IsFinite(dx) && float.IsFinite(dy) && dx * dx + dy * dy <= 128f * 128f;
    }

    private static void GetBounds(in WorldTileDimensions dimensions, out float horizontalBorder, out float verticalBorder, out float maximumX, out float maximumY)
    {
        float worldWidth = dimensions.WidthTiles * 16f;
        float worldHeight = dimensions.HeightTiles * 16f;
        horizontalBorder = worldWidth >= BorderPixels * 2f + PlayerAuthority.VanillaBasePlayerWidth
            ? BorderPixels
            : 0f;
        verticalBorder = worldHeight >= BorderPixels * 2f + PlayerAuthority.VanillaBasePlayerHeight
            ? BorderPixels
            : 0f;
        maximumX = worldWidth - horizontalBorder - PlayerAuthority.VanillaBasePlayerWidth;
        maximumY = worldHeight - verticalBorder - PlayerAuthority.VanillaBasePlayerHeight;
    }
}
