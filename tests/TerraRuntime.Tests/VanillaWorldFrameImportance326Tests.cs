using System.Security.Cryptography;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldFrameImportance326Tests
{
    [Fact]
    public void Packed_mask_matches_official_1458_generated_world_fixture()
    {
        ReadOnlySpan<byte> packed = VanillaWorldFrameImportance326.PackedBits;

        Assert.Equal(VanillaWorldFrameImportance326.PackedByteCount, packed.Length);
        Assert.Equal(95, packed.Length);
        Assert.Equal(
            VanillaWorldFrameImportance326.PackedBitsSha256,
            Convert.ToHexString(SHA256.HashData(packed)));

        int importantCount = 0;
        for (int tileType = 0; tileType < VanillaWorldFrameImportance326.Count; tileType++)
        {
            if (VanillaWorldFrameImportance326.IsFrameImportant(tileType))
                importantCount++;
        }

        Assert.Equal(VanillaWorldFrameImportance326.FrameImportantTileCount, importantCount);
        Assert.Equal(412, importantCount);

        // The final packed byte contains only IDs 752 and 753. Unused bits beyond the current catalog must stay 0.
        Assert.Equal(0, packed[^1] & 0xFC);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(144, true)]
    [InlineData(423, true)]
    [InlineData(520, true)]
    [InlineData(724, true)]
    [InlineData(750, false)]
    [InlineData(751, true)]
    [InlineData(752, true)]
    [InlineData(753, true)]
    public void Known_bits_match_official_fixture(int tileType, bool expected)
    {
        Assert.Equal(expected, VanillaWorldFrameImportance326.IsFrameImportant(tileType));
    }
}
