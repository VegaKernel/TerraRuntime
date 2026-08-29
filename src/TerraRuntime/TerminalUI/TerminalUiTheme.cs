using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace TerraRuntime.TerminalUI;

/// <summary>
/// Explicit high-contrast TerraRuntime palette. The production dashboard does not inherit terminal-default
/// Color.None values because some Windows/conhost combinations can resolve those defaults into an unreadable
/// content area while MenuBar chrome remains visible.
/// </summary>
internal static class TerminalUiTheme
{
    private const string Background = "#020604";
    private const string Panel = "#07120A";
    private const string MenuBackground = "#0A2112";
    private const string Primary = "#78FF98";
    private const string Bright = "#B8FFC5";
    private const string Accent = "#2DFF70";
    private const string Muted = "#428A54";
    private const string Disabled = "#24472E";
    private const string Danger = "#FF5C57";

    internal static void Apply()
    {
        SchemeManager.AddScheme(nameof(Schemes.Base), CreateBaseScheme());
        SchemeManager.AddScheme(nameof(Schemes.Menu), CreateMenuScheme());
        SchemeManager.AddScheme(nameof(Schemes.Dialog), CreateDialogScheme());
        SchemeManager.AddScheme(nameof(Schemes.Accent), CreateAccentScheme());
        SchemeManager.AddScheme(nameof(Schemes.Error), CreateErrorScheme());
    }

    internal static Scheme CreateBaseScheme() => new()
    {
        Normal = new TgAttribute(Primary, Background),
        Focus = new TgAttribute(Background, Accent, TextStyle.Bold),
        HotNormal = new TgAttribute(Bright, Background, TextStyle.Bold | TextStyle.Underline),
        HotFocus = new TgAttribute(Background, Bright, TextStyle.Bold | TextStyle.Underline),
        Active = new TgAttribute(Bright, Panel, TextStyle.Bold),
        HotActive = new TgAttribute(Accent, Panel, TextStyle.Bold | TextStyle.Underline),
        Highlight = new TgAttribute(Background, Accent, TextStyle.Bold),
        Editable = new TgAttribute(Bright, Panel),
        ReadOnly = new TgAttribute(Muted, Background),
        Disabled = new TgAttribute(Disabled, Background, TextStyle.Faint)
    };

    internal static Scheme CreateMenuScheme() => new()
    {
        Normal = new TgAttribute(Primary, MenuBackground, TextStyle.Bold),
        Focus = new TgAttribute(Background, Accent, TextStyle.Bold),
        HotNormal = new TgAttribute(Bright, MenuBackground, TextStyle.Bold | TextStyle.Underline),
        HotFocus = new TgAttribute(Background, Bright, TextStyle.Bold | TextStyle.Underline),
        Active = new TgAttribute(Bright, Panel, TextStyle.Bold),
        HotActive = new TgAttribute(Accent, Panel, TextStyle.Bold | TextStyle.Underline),
        Highlight = new TgAttribute(Background, Accent, TextStyle.Bold),
        ReadOnly = new TgAttribute(Muted, MenuBackground),
        Disabled = new TgAttribute(Disabled, MenuBackground, TextStyle.Faint)
    };

    internal static Scheme CreateDialogScheme() => new()
    {
        Normal = new TgAttribute(Primary, Panel),
        Focus = new TgAttribute(Background, Accent, TextStyle.Bold),
        HotNormal = new TgAttribute(Bright, Panel, TextStyle.Bold | TextStyle.Underline),
        HotFocus = new TgAttribute(Background, Bright, TextStyle.Bold | TextStyle.Underline),
        Highlight = new TgAttribute(Background, Accent, TextStyle.Bold),
        Editable = new TgAttribute(Bright, Background),
        ReadOnly = new TgAttribute(Muted, Panel),
        Disabled = new TgAttribute(Disabled, Panel, TextStyle.Faint)
    };

    internal static Scheme CreateAccentScheme() => new()
    {
        Normal = new TgAttribute(Bright, Panel),
        Focus = new TgAttribute(Background, Accent, TextStyle.Bold),
        HotNormal = new TgAttribute(Accent, Panel, TextStyle.Bold | TextStyle.Underline),
        HotFocus = new TgAttribute(Background, Bright, TextStyle.Bold | TextStyle.Underline),
        Highlight = new TgAttribute(Background, Accent, TextStyle.Bold),
        ReadOnly = new TgAttribute(Muted, Panel),
        Disabled = new TgAttribute(Disabled, Panel, TextStyle.Faint)
    };

    internal static Scheme CreateErrorScheme() => new()
    {
        Normal = new TgAttribute(Danger, Background, TextStyle.Bold),
        Focus = new TgAttribute(Background, Danger, TextStyle.Bold),
        HotNormal = new TgAttribute(Bright, Background, TextStyle.Bold | TextStyle.Underline),
        HotFocus = new TgAttribute(Background, Bright, TextStyle.Bold | TextStyle.Underline),
        Highlight = new TgAttribute(Background, Danger, TextStyle.Bold),
        ReadOnly = new TgAttribute(Danger, Background),
        Disabled = new TgAttribute(Disabled, Background, TextStyle.Faint)
    };
}
