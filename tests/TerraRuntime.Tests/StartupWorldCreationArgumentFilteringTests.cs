namespace TerraRuntime.Tests;

public sealed class StartupWorldCreationArgumentFilteringTests
{
    [Fact]
    public void RemoveCreationArguments_preserves_only_server_options()
    {
        string[] args =
        [
            "--port", "17777",
            "--create-world", "Generated",
            "--world-generator", "terraruntime:flat",
            "--world-seed", "42",
            "--world-width", "4200",
            "--world-height", "1200",
            "--world-output", "generated.wld",
            "--max-players", "4",
            "--no-tui"
        ];

        string[] filtered = StartupWorldCreationRequestParser.RemoveCreationArguments(args);

        Assert.Equal(
            ["--port", "17777", "--max-players", "4", "--no-tui"],
            filtered);
    }
}
