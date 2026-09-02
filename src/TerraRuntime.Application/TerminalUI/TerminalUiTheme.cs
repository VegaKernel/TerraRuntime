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
    private const string Background = "#020806";
    private const string Panel = "#06120D";
    private const string FocusedPanel = "#0D2A19";
    private const string MenuBackground = "#071C11";
    private const string Primary = "#B7EFC8";
    private const string Bright = "#E2F9EA";
    private const string Accent = "#52E584";
    private const string SelectionBackground = "#B8FF6A";
    private const string Muted = "#6FA781";
    private const string Disabled = "#36523F";
    private const string Danger = "#FF6B6B";

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
        // Terminal.Gui TextView 2.4.17 renders selected text with VisualRole.Active.
        // Keep that state intentionally loud so mouse/keyboard selections remain obvious on dark terminals.
        Active = new TgAttribute(Background, SelectionBackground, TextStyle.Bold),
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
        Normal = new TgAttribute(Bright, FocusedPanel, TextStyle.Bold),
        Focus = new TgAttribute(Background, Accent, TextStyle.Bold),
        HotNormal = new TgAttribute(Accent, FocusedPanel, TextStyle.Bold | TextStyle.Underline),
        HotFocus = new TgAttribute(Background, Bright, TextStyle.Bold | TextStyle.Underline),
        Active = new TgAttribute(Background, SelectionBackground, TextStyle.Bold),
        HotActive = new TgAttribute(Accent, FocusedPanel, TextStyle.Bold | TextStyle.Underline),
        Highlight = new TgAttribute(Background, Accent, TextStyle.Bold),
        Editable = new TgAttribute(Bright, FocusedPanel),
        ReadOnly = new TgAttribute(Muted, FocusedPanel),
        Disabled = new TgAttribute(Disabled, FocusedPanel, TextStyle.Faint)
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
