namespace TerraRuntime.Application;

/// <summary>
/// TerrariaServer 1.4.5.8 <c>Player.Spawn_SetPosition</c> coordinate conversion.
/// Packet 12 carries the floor tile, while runtime collision/replication state stores the player's top-left pixels.
/// </summary>
internal static class VanillaPlayerSpawnPosition1458
{
    private const float TileSizePixels = 16f;
    private const float FloorCenterOffsetPixels = 8f;

    public static void FromFloorTile(short floorX, short floorY, out float positionX, out float positionY)
    {
        positionX = floorX * TileSizePixels + FloorCenterOffsetPixels - PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        positionY = floorY * TileSizePixels - PlayerAuthority.VanillaBasePlayerHeight;
    }

    public static void ToFloorTile(float positionX, float positionY, out short floorX, out short floorY)
    {
        int x = (int)MathF.Floor((positionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f) / TileSizePixels);
        int y = (int)MathF.Floor((positionY + PlayerAuthority.VanillaBasePlayerHeight) / TileSizePixels);
        floorX = checked((short)Math.Clamp(x, short.MinValue, short.MaxValue));
        floorY = checked((short)Math.Clamp(y, short.MinValue, short.MaxValue));
    }
}
