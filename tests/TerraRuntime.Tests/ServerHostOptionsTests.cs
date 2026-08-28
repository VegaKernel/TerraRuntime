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

    [Theory]
    [InlineData("tui", true)]
    [InlineData("terminal", true)]
    [InlineData("plain", false)]
    [InlineData("console", false)]
    [InlineData("headless", false)]
    public void Vega_ui_modes_are_supported(string mode, bool expectedEnabled)
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld", "--vega-ui", mode],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.Equal(expectedEnabled, options.TerminalUiEnabled);
    }

    [Fact]
    public void Vega_ui_equals_syntax_is_supported()
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld", "--vega-ui=plain"],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.False(options.TerminalUiEnabled);
    }

    [Fact]
    public void Vega_style_server_argument_aliases_are_supported()
    {
        bool parsed = ServerHostOptions.TryParse(
            ["-world", "test.wld", "-port", "7788", "-maxplayers", "16"],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.EndsWith("test.wld", options.WorldPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7788, options.Port);
        Assert.Equal(16, options.MaxPlayers);
        Assert.True(options.TerminalUiEnabled);
    }
}
