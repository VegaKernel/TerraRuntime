using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaPlayerAppearanceNormalizerTests
{
    [Fact]
    public void Normalizes_all_vanilla_packet_4_ranges()
    {
        PlayerAppearanceCommitRequest request = Request("  Player  ") with
        {
            SkinVariant = byte.MaxValue,
            VoiceVariant = byte.MaxValue,
            VoicePitchOffset = float.PositiveInfinity,
            Hair = byte.MaxValue,
            HideVisibleAccessory = ushort.MaxValue,
            HideMisc = byte.MaxValue,
            DifficultyFlags = byte.MaxValue,
            TorchAndCartFlags = byte.MaxValue,
            ConsumableUnlockFlags = byte.MaxValue
        };

        Assert.True(VanillaPlayerAppearanceNormalizer.TryNormalize(in request, out PlayerAppearanceCommitRequest actual));
        Assert.Equal((byte)11, actual.SkinVariant);
        Assert.Equal((byte)4, actual.VoiceVariant);
        Assert.Equal(1f, actual.VoicePitchOffset);
        Assert.Equal((byte)0, actual.Hair);
        Assert.Equal("Player", actual.Name);
        Assert.Equal((ushort)0x03ff, actual.HideVisibleAccessory);
        Assert.Equal((byte)0x03, actual.HideMisc);
        Assert.Equal((byte)0x0c, actual.DifficultyFlags);
        Assert.Equal((byte)0x1f, actual.TorchAndCartFlags);
        Assert.Equal((byte)0x7f, actual.ConsumableUnlockFlags);
    }

    [Fact]
    public void NaN_pitch_becomes_zero_and_voice_zero_becomes_one()
    {
        PlayerAppearanceCommitRequest request = Request("Player") with
        {
            VoiceVariant = 0,
            VoicePitchOffset = float.NaN
        };

        Assert.True(VanillaPlayerAppearanceNormalizer.TryNormalize(in request, out PlayerAppearanceCommitRequest actual));
        Assert.Equal((byte)1, actual.VoiceVariant);
        Assert.Equal(0f, actual.VoicePitchOffset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901")]
    public void Rejects_invalid_vanilla_names(string name)
    {
        PlayerAppearanceCommitRequest request = Request(name);

        Assert.False(VanillaPlayerAppearanceNormalizer.TryNormalize(in request, out _));
    }

    internal static PlayerAppearanceCommitRequest Request(string name) =>
        new(
            new(3),
            SkinVariant: 0,
            VoiceVariant: 1,
            VoicePitchOffset: 0,
            Hair: 0,
            Name: name,
            HairDye: 0,
            HideVisibleAccessory: 0,
            HideMisc: 0,
            HairColor: default,
            SkinColor: default,
            EyeColor: default,
            ShirtColor: default,
            UnderShirtColor: default,
            PantsColor: default,
            ShoeColor: default,
            DifficultyFlags: 0,
            TorchAndCartFlags: 0,
            ConsumableUnlockFlags: 0);
}
