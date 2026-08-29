using TerraRuntime.TerminalUI;
using Terminal.Gui.Drawing;

namespace TerraRuntime.Tests;

public sealed class TerminalUiCompatibilityTests
{
    [Fact]
    public void Windows_production_dashboard_uses_dotnet_driver_instead_of_native_windows_driver()
    {
        Assert.Equal("dotnet", TerminalUiHost.ResolveProductionDriverName(isWindows: true));
        Assert.Null(TerminalUiHost.ResolveProductionDriverName(isWindows: false));
    }

    [Fact]
    public void Hacker_theme_keeps_base_and_menu_text_explicit_and_contrasting()
    {
        Scheme baseScheme = TerminalUiTheme.CreateBaseScheme();
        Scheme menuScheme = TerminalUiTheme.CreateMenuScheme();

        AssertExplicitContrast(baseScheme.Normal);
        AssertExplicitContrast(baseScheme.Focus);
        AssertExplicitContrast(baseScheme.HotNormal);
        AssertExplicitContrast(baseScheme.HotFocus);
        AssertExplicitContrast(menuScheme.Normal);
        AssertExplicitContrast(menuScheme.Focus);
        AssertExplicitContrast(menuScheme.HotNormal);
        AssertExplicitContrast(menuScheme.HotFocus);
    }

    private static void AssertExplicitContrast(Terminal.Gui.Drawing.Attribute attribute)
    {
        Assert.NotEqual(Color.None, attribute.Foreground);
        Assert.NotEqual(Color.None, attribute.Background);
        Assert.NotEqual(attribute.Foreground, attribute.Background);
    }
}
