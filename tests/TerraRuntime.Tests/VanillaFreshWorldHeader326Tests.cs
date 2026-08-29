using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaFreshWorldHeader326Tests
{
    [Fact]
    public void Create_uses_vanilla_pixel_bounds_and_generator_version()
    {
        Guid uniqueId = Guid.Parse("2df4e11f-f24a-4e65-a247-1921bbefa53e");

        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "Generated",
            "seed-text",
            widthTiles: 4200,
            heightTiles: 1200,
            uniqueId,
            worldId: 123456789);

        Assert.Equal("Generated", header.Name);
        Assert.Equal("seed-text", header.SeedText);
        Assert.Equal(VanillaFreshWorldHeader326.WorldGeneratorVersion, header.WorldGeneratorVersion);
        Assert.Equal(uniqueId, header.UniqueId);
        Assert.Equal(123456789, header.WorldId);
        Assert.Equal(0, header.LeftWorld);
        Assert.Equal(4200 * VanillaFreshWorldHeader326.TileSizePixels, header.RightWorld);
        Assert.Equal(0, header.TopWorld);
        Assert.Equal(1200 * VanillaFreshWorldHeader326.TileSizePixels, header.BottomWorld);
        Assert.Equal(4200, header.Dimensions.WidthTiles);
        Assert.Equal(1200, header.Dimensions.HeightTiles);
    }

    [Fact]
    public void Create_rejects_empty_identity_and_overflowing_pixel_bounds()
    {
        Assert.Throws<ArgumentException>(() => VanillaFreshWorldHeader326.Create(
            "Generated", "seed", 100, 100, Guid.Empty, 1));
        Assert.Throws<OverflowException>(() => VanillaFreshWorldHeader326.Create(
            "Generated", "seed", int.MaxValue, 100, Guid.NewGuid(), 1));
    }
}
