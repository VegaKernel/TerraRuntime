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

        float worldWidth = dimensions.WidthTiles * 16f;
        float worldHeight = dimensions.HeightTiles * 16f;
        float horizontalBorder = worldWidth >= BorderPixels * 2f + PlayerAuthority.VanillaBasePlayerWidth
            ? BorderPixels
            : 0f;
        float verticalBorder = worldHeight >= BorderPixels * 2f + PlayerAuthority.VanillaBasePlayerHeight
            ? BorderPixels
            : 0f;
        float maximumX = worldWidth - horizontalBorder - PlayerAuthority.VanillaBasePlayerWidth;
        float maximumY = worldHeight - verticalBorder - PlayerAuthority.VanillaBasePlayerHeight;
        return maximumX >= horizontalBorder && maximumY >= verticalBorder &&
            positionX >= horizontalBorder && positionX <= maximumX &&
            positionY >= verticalBorder && positionY <= maximumY;
    }
}
