using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class OfficialVanillaWorldGenerator1458Tests
{
    [Theory]
    [InlineData(4200, 1200, 1)]
    [InlineData(6400, 1800, 2)]
    [InlineData(8400, 2400, 3)]
    public void Maps_only_canonical_terraria_world_sizes(int width, int height, int expected)
    {
        Assert.True(OfficialVanillaWorldGenerator1458.TryMapWorldSize(width, height, out int autoCreate));
        Assert.Equal(expected, autoCreate);
    }

    [Fact]
    public void Rejects_custom_dimensions_for_exact_vanilla()
    {
        Assert.False(OfficialVanillaWorldGenerator1458.TryMapWorldSize(5000, 1500, out int autoCreate));
        Assert.Equal(0, autoCreate);
    }

    [Fact]
    public void Builds_prefixed_seed_so_official_server_receives_selected_size_mode_and_evil()
    {
        var generation = new WorldGenerationRequest(
            new WorldGeneratorId(OfficialVanillaWorldGenerator1458.GeneratorIdValue),
            "Exact",
            Seed: 12345,
            WidthTiles: 6400,
            HeightTiles: 1800)
        {
            SeedText = "12345",
            Options = new WorldGenerationOptions(WorldGenerationGameMode.Master, WorldGenerationEvil.Crimson)
        };
        var request = new StartupWorldCreationRequest(generation, Path.Combine(Path.GetTempPath(), "exact.wld"));

        Assert.Equal("2.3.2.12345", OfficialVanillaWorldGenerator1458.BuildServerSeed(in request, autoCreate: 2));
    }

    [Fact]
    public void Preserves_full_terraria_seed_verbatim()
    {
        const string fullSeed = "3.1.2.255.planetoids|1399440699";
        var generation = new WorldGenerationRequest(
            new WorldGeneratorId(OfficialVanillaWorldGenerator1458.GeneratorIdValue),
            "Exact",
            Seed: 0,
            WidthTiles: 8400,
            HeightTiles: 2400)
        {
            SeedText = fullSeed,
            Options = new WorldGenerationOptions(WorldGenerationGameMode.Classic, WorldGenerationEvil.Crimson)
        };
        var request = new StartupWorldCreationRequest(generation, Path.Combine(Path.GetTempPath(), "exact.wld"));

        Assert.Equal(fullSeed, OfficialVanillaWorldGenerator1458.BuildServerSeed(in request, autoCreate: 3));
    }

    [Fact]
    public void Prefixes_secret_seed_text_without_reimplementing_secret_worldgen()
    {
        const string secretSeed = "get fixed boi|planetoids";
        var generation = new WorldGenerationRequest(
            new WorldGeneratorId(OfficialVanillaWorldGenerator1458.GeneratorIdValue),
            "Exact",
            Seed: 0,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = secretSeed,
            Options = new WorldGenerationOptions(WorldGenerationGameMode.Expert, WorldGenerationEvil.Corruption)
        };
        var request = new StartupWorldCreationRequest(generation, Path.Combine(Path.GetTempPath(), "exact.wld"));

        Assert.Equal("1.2.1.get fixed boi|planetoids", OfficialVanillaWorldGenerator1458.BuildServerSeed(in request, autoCreate: 1));
    }

    [Fact]
    public void Server_config_points_official_generator_at_requested_world()
    {
        string output = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Exact Vanilla.wld"));
        var generation = new WorldGenerationRequest(
            new WorldGeneratorId(OfficialVanillaWorldGenerator1458.GeneratorIdValue),
            "Exact Vanilla",
            Seed: 42,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "42",
            Options = new WorldGenerationOptions(WorldGenerationGameMode.Classic, WorldGenerationEvil.Crimson)
        };
        var request = new StartupWorldCreationRequest(generation, output);

        string config = OfficialVanillaWorldGenerator1458.BuildServerConfig(in request, autoCreate: 1, port: 23456);

        Assert.Contains($"world={output}", config, StringComparison.Ordinal);
        Assert.Contains("autocreate=1", config, StringComparison.Ordinal);
        Assert.Contains("seed=1.1.2.42", config, StringComparison.Ordinal);
        Assert.Contains("worldname=Exact Vanilla", config, StringComparison.Ordinal);
        Assert.Contains("difficulty=0", config, StringComparison.Ordinal);
        Assert.Contains("port=23456", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_accepts_text_seed_only_for_exact_vanilla_backend()
    {
        string[] vanillaArgs =
        [
            "--create-world", "SecretWorld",
            "--world-generator", OfficialVanillaWorldGenerator1458.GeneratorIdValue,
            "--world-seed", "get fixed boi|planetoids",
            "--world-width", "4200",
            "--world-height", "1200"
        ];

        Assert.True(
            StartupWorldCreationRequestParser.TryParse(
                vanillaArgs,
                Path.GetTempPath(),
                out StartupWorldCreationRequest vanillaRequest,
                out string? vanillaError),
            vanillaError);
        Assert.Equal(0UL, vanillaRequest.Generation.Seed);
        Assert.Equal("get fixed boi|planetoids", vanillaRequest.Generation.SeedText);

        string[] customArgs = (string[])vanillaArgs.Clone();
        customArgs[3] = "fixture:custom";
        Assert.False(
            StartupWorldCreationRequestParser.TryParse(
                customArgs,
                Path.GetTempPath(),
                out _,
                out string? customError));
        Assert.Contains("unsigned 64-bit integer", customError, StringComparison.Ordinal);
    }
}
