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
}
