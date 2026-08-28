namespace TerraRuntime.Tests;

public sealed class ServerHostOptionsTests
{
    [Fact]
    public void Terminal_ui_is_disabled_by_default()
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld"],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.False(options.TerminalUiEnabled);
    }

    [Fact]
    public void Terminal_ui_can_be_enabled_from_startup_flag()
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld", "--tui"],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.True(options.TerminalUiEnabled);
    }
}
