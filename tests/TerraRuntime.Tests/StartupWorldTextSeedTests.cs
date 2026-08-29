using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class StartupWorldTextSeedTests
{
    [Fact]
    public void Startup_accepts_textual_special_and_combined_secret_seed_without_normalizing_seed_text()
    {
        const string seedText = "1.1.1.0.Beam me up|Purify this|Electric Boogaloo";
        string[] args =
        [
            "--create-world", "SecretWorld",
            "--world-generator", "terraruntime:vanilla",
            "--world-seed", seedText,
            "--world-width", "4200",
            "--world-height", "1200"
        ];

        Assert.True(
            StartupWorldCreationRequestParser.TryParse(
                args,
                Path.GetTempPath(),
                out StartupWorldCreationRequest request,
                out string? error),
            error);
        Assert.Equal(seedText, request.Generation.SeedText);
        Assert.NotEqual(0UL, request.Generation.Seed);

        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedProfile1458.Parse(
            request.Generation.SeedText,
            request.Generation.Seed);
        Assert.True(profile.HasModifier(VanillaSecretSeedModifier1458.BeamMeUp));
        Assert.True(profile.HasFlag(VanillaWorldSeedFlags1458.InfectedSeed));
        Assert.True(profile.HasFlag(VanillaWorldSeedFlags1458.MoreLightningSeed));
    }
}
