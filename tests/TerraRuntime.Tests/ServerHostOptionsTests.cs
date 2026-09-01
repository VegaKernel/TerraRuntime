namespace TerraRuntime.Tests;

public sealed class ServerHostOptionsTests
{
    [Fact]
    public void Terminal_ui_is_enabled_by_default()
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld"],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.True(options.TerminalUiEnabled);
    }

    [Fact]
    public void Terminal_ui_can_be_explicitly_disabled()
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld", "--no-tui"],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.False(options.TerminalUiEnabled);
    }

    [Fact]
    public void Explicit_tui_flag_keeps_terminal_ui_enabled()
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld", "--tui"],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.True(options.TerminalUiEnabled);
    }

    [Fact]
    public void Sandbox_capacity_and_materialization_concurrency_have_bounded_defaults()
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld"],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.Equal(ServerHostOptions.DefaultMaxWorldRuntimes, options.MaxWorldRuntimes);
        Assert.Equal(
            ServerHostOptions.DefaultSandboxMaterializationConcurrency,
            options.SandboxMaterializationConcurrency);
    }

    [Fact]
    public void Sandbox_capacity_and_materialization_concurrency_can_be_configured()
    {
        bool parsed = ServerHostOptions.TryParse(
            [
                "--world", "test.wld",
                "--max-world-runtimes", "12",
                "--sandbox-materialization-concurrency", "3"
            ],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.Equal(12, options.MaxWorldRuntimes);
        Assert.Equal(3, options.SandboxMaterializationConcurrency);
    }

    [Theory]
    [InlineData("--max-world-runtimes", "1")]
    [InlineData("--max-world-runtimes", "65")]
    [InlineData("--sandbox-materialization-concurrency", "0")]
    [InlineData("--sandbox-materialization-concurrency", "5")]
    public void Sandbox_operational_limits_reject_out_of_range_values(string option, string value)
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld", option, value],
            out ServerHostOptions? options,
            out string? error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.NotNull(error);
    }
}
