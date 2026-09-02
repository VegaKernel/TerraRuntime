using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Runtime;
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
        Assert.Equal(VanillaPlayerAppearanceNormalizer.HideVisibleAccessoryMask, actual.HideVisibleAccessory);
        Assert.Equal(VanillaPlayerAppearanceNormalizer.HideMiscMask, actual.HideMisc);
        Assert.Equal(
            (byte)(VanillaPlayerAppearanceNormalizer.JourneyDifficultyFlag |
                   VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag),
            actual.DifficultyFlags);
        Assert.Equal(VanillaPlayerAppearanceNormalizer.TorchAndCartFlagsMask, actual.TorchAndCartFlags);
        Assert.Equal(VanillaPlayerAppearanceNormalizer.ConsumableUnlockFlagsMask, actual.ConsumableUnlockFlags);
    }

    [Theory]
    [InlineData(
        VanillaPlayerAppearanceNormalizer.MediumcoreDifficultyFlag,
        VanillaPlayerAppearanceNormalizer.MediumcoreDifficultyFlag)]
    [InlineData(
        VanillaPlayerAppearanceNormalizer.HardcoreDifficultyFlag,
        VanillaPlayerAppearanceNormalizer.HardcoreDifficultyFlag)]
    [InlineData(
        VanillaPlayerAppearanceNormalizer.JourneyDifficultyFlag,
        VanillaPlayerAppearanceNormalizer.JourneyDifficultyFlag)]
    [InlineData(
        (byte)(VanillaPlayerAppearanceNormalizer.MediumcoreDifficultyFlag |
               VanillaPlayerAppearanceNormalizer.HardcoreDifficultyFlag |
               VanillaPlayerAppearanceNormalizer.JourneyDifficultyFlag |
               VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag),
        (byte)(VanillaPlayerAppearanceNormalizer.JourneyDifficultyFlag |
               VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag))]
    public void Difficulty_normalization_uses_named_vanilla_precedence(byte input, byte expected)
    {
        PlayerAppearanceCommitRequest request = Request("Player") with { DifficultyFlags = input };

        Assert.True(VanillaPlayerAppearanceNormalizer.TryNormalize(in request, out PlayerAppearanceCommitRequest actual));
        Assert.Equal(expected, actual.DifficultyFlags);
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
