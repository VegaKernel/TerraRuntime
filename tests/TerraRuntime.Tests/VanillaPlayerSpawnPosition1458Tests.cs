namespace TerraRuntime.Tests;

public sealed class VanillaPlayerSpawnPosition1458Tests
{
    [Theory]
    [InlineData((short)100, (short)200)]
    [InlineData((short)0, (short)0)]
    [InlineData((short)2100, (short)297)]
    public void Floor_tile_round_trips_through_vanilla_top_left_position(short floorX, short floorY)
    {
        VanillaPlayerSpawnPosition1458.FromFloorTile(floorX, floorY, out float positionX, out float positionY);

        Assert.Equal(floorX * 16f + 8f - PlayerAuthority.VanillaBasePlayerWidth * 0.5f, positionX);
        Assert.Equal(floorY * 16f - PlayerAuthority.VanillaBasePlayerHeight, positionY);

        VanillaPlayerSpawnPosition1458.ToFloorTile(positionX, positionY, out short roundTripX, out short roundTripY);
        Assert.Equal(floorX, roundTripX);
        Assert.Equal(floorY, roundTripY);
    }
}
