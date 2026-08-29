using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class StartupWorldCreationRequestParserTests
{
    [Fact]
    public void Parses_complete_request_and_places_default_output_in_worlds_directory()
    {
        string worldsDirectory = Path.Combine(Path.GetTempPath(), "terraruntime-worldgen-tests");
        string[] args =
        [
            "--create-world", "GeneratedWorld",
            "--world-generator", "fixture:worldgen",
            "--world-seed", "18446744073709551615",
            "--world-width", "4200",
            "--world-height", "1200"
        ];

        bool parsed = StartupWorldCreationRequestParser.TryParse(
            args,
            worldsDirectory,
            out StartupWorldCreationRequest request,
            out string? error);

        Assert.True(parsed, error);
        Assert.Null(error);
        Assert.Equal(new WorldGeneratorId("fixture:worldgen"), request.Generation.GeneratorId);
        Assert.Equal("GeneratedWorld", request.Generation.WorldName);
        Assert.Equal(ulong.MaxValue, request.Generation.Seed);
        Assert.Equal(4200, request.Generation.WidthTiles);
        Assert.Equal(1200, request.Generation.HeightTiles);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(worldsDirectory, "GeneratedWorld.wld")),
            request.OutputPath);
    }

    [Fact]
    public void Explicit_output_path_is_normalized_and_may_live_outside_worlds_directory()
    {
        string worldsDirectory = Path.Combine(Path.GetTempPath(), "terraruntime-worlds");
        string output = Path.Combine(Path.GetTempPath(), "custom", "generated.wld");
        string[] args =
        [
            "--create-world", "GeneratedWorld",
            "--world-generator", "fixture:worldgen",
            "--world-seed", "42",
            "--world-width", "6400",
            "--world-height", "1800",
            "--world-output", output
        ];

        Assert.True(
            StartupWorldCreationRequestParser.TryParse(
                args,
                worldsDirectory,
                out StartupWorldCreationRequest request,
                out string? error),
            error);
        Assert.Equal(Path.GetFullPath(output), request.OutputPath);
    }

    [Fact]
    public void Missing_generation_argument_fails_closed()
    {
        string[] args =
        [
            "--create-world", "GeneratedWorld",
            "--world-seed", "42",
            "--world-width", "4200",
            "--world-height", "1200"
        ];

        Assert.False(
            StartupWorldCreationRequestParser.TryParse(
                args,
                Path.GetTempPath(),
                out _,
                out string? error));
        Assert.Equal("Missing required option --world-generator.", error);
    }

    [Fact]
    public void Duplicate_generation_argument_is_rejected()
    {
        string[] args =
        [
            "--create-world", "GeneratedWorld",
            "--world-generator", "fixture:first",
            "--world-generator", "fixture:second",
            "--world-seed", "42",
            "--world-width", "4200",
            "--world-height", "1200"
        ];

        Assert.False(
            StartupWorldCreationRequestParser.TryParse(
                args,
                Path.GetTempPath(),
                out _,
                out string? error));
        Assert.Equal("Option --world-generator may be specified only once.", error);
    }

    [Fact]
    public void Default_output_rejects_world_name_that_can_escape_worlds_directory()
    {
        string[] args =
        [
            "--create-world", "../escape",
            "--world-generator", "fixture:worldgen",
            "--world-seed", "42",
            "--world-width", "4200",
            "--world-height", "1200"
        ];

        Assert.False(
            StartupWorldCreationRequestParser.TryParse(
                args,
                Path.GetTempPath(),
                out _,
                out string? error));
        Assert.Contains("valid file name", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_output_must_be_a_world_file()
    {
        string[] args =
        [
            "--create-world", "GeneratedWorld",
            "--world-generator", "fixture:worldgen",
            "--world-seed", "42",
            "--world-width", "4200",
            "--world-height", "1200",
            "--world-output", "generated.dat"
        ];

        Assert.False(
            StartupWorldCreationRequestParser.TryParse(
                args,
                Path.GetTempPath(),
                out _,
                out string? error));
        Assert.Equal("--world-output must end in .wld.", error);
    }
}
