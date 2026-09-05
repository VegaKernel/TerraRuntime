using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class VanillaPlayerWorldBounds1458Tests
{
    [Fact]
    public void Canonical_world_uses_source_backed_640_pixel_player_border()
    {
        var dimensions = new WorldTileDimensions(4200, 1200);
        float maximumX = dimensions.WidthTiles * 16f - VanillaPlayerWorldBounds1458.BorderPixels -
            PlayerAuthority.VanillaBasePlayerWidth;
        float maximumY = dimensions.HeightTiles * 16f - VanillaPlayerWorldBounds1458.BorderPixels -
            PlayerAuthority.VanillaBasePlayerHeight;

        Assert.True(VanillaPlayerWorldBounds1458.ContainsTopLeft(
            in dimensions,
            VanillaPlayerWorldBounds1458.BorderPixels,
            VanillaPlayerWorldBounds1458.BorderPixels));
        Assert.True(VanillaPlayerWorldBounds1458.ContainsTopLeft(in dimensions, maximumX, maximumY));
        Assert.False(VanillaPlayerWorldBounds1458.ContainsTopLeft(in dimensions, 639f, 640f));
        Assert.False(VanillaPlayerWorldBounds1458.ContainsTopLeft(in dimensions, maximumX + 1f, maximumY));
        Assert.False(VanillaPlayerWorldBounds1458.ContainsTopLeft(in dimensions, maximumX, maximumY + 1f));
    }

    [Fact]
    public void Synthetic_sub_border_world_still_requires_the_player_body_inside_tiles()
    {
        var dimensions = new WorldTileDimensions(32, 24);

        Assert.True(VanillaPlayerWorldBounds1458.ContainsTopLeft(in dimensions, 0f, 0f));
        Assert.True(VanillaPlayerWorldBounds1458.ContainsTopLeft(in dimensions, 492f, 342f));
        Assert.False(VanillaPlayerWorldBounds1458.ContainsTopLeft(in dimensions, 493f, 342f));
        Assert.False(VanillaPlayerWorldBounds1458.ContainsTopLeft(in dimensions, 492f, 343f));
    }
}
