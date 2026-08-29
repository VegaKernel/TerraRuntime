using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileCreativePowersEncoderTests
{
    [Fact]
    public void Roundtrips_all_persistent_creative_powers_through_current_decoder()
    {
        var source = new WorldCreativePowersData(
            FreezeTime: true,
            TimeRateSlider: 0.25f,
            FreezeRain: false,
            FreezeWind: true,
            DifficultySlider: 0.75f,
            StopBiomeSpread: true);

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileCreativePowersEncodeResult.Encoded,
            WorldFileCreativePowersEncoder.TryEncode(source, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);
        Assert.Equal(31, bytesWritten);

        byte[] section = stream.ToArray();
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, section.Length],
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);

        Assert.Equal(
            WorldFileCreativePowersDecodeResult.Decoded,
            WorldFileCreativePowersDecoder.TryDecode(
                section,
                envelope,
                out WorldCreativePowersData? decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        Assert.Equal(source, decoded);
    }

    [Theory]
    [InlineData(float.NaN, 0.5f)]
    [InlineData(float.PositiveInfinity, 0.5f)]
    [InlineData(-0.01f, 0.5f)]
    [InlineData(1.01f, 0.5f)]
    [InlineData(0.5f, float.NaN)]
    [InlineData(0.5f, float.NegativeInfinity)]
    [InlineData(0.5f, -0.01f)]
    [InlineData(0.5f, 1.01f)]
    public void Rejects_invalid_slider_values_before_writing(float timeRate, float difficulty)
    {
        var source = new WorldCreativePowersData(
            FreezeTime: false,
            TimeRateSlider: timeRate,
            FreezeRain: false,
            FreezeWind: false,
            DifficultySlider: difficulty,
            StopBiomeSpread: false);
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileCreativePowersEncodeResult.InvalidSliderValue,
            WorldFileCreativePowersEncoder.TryEncode(source, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }
}
