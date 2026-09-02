using System.Collections.ObjectModel;
using System.Drawing;
using TerraRuntime.Contracts.Runtime;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

internal enum SandboxWorldTreeRowKind : byte
{
    World = 0,
    Player = 1,
    Placeholder = 2
}

internal readonly record struct SandboxWorldTreeRow(
    SandboxWorldTreeRowKind Kind,
    SandboxName? Target,
    string? PlayerSelector);

/// <summary>
/// Row-selecting world roster. Unlike a read-only TextView, ListView highlights an item rather than selecting text.
/// Player rows retain drag-and-drop world transfer and right-click exposes semantic world/player actions.
/// </summary>
internal sealed class SandboxWorldTreeView : ListView
{
    private SandboxWorldTreeRow[] rows = [];
    private string[] lines = [];
    private int? draggedRow;

    public SandboxWorldTreeView()
    {
        CanFocus = true;
    }

    public event Action<string, SandboxName?>? TransferRequested;
    public event Action<SandboxName>? DestroyRequested;
    public event Action<string>? KickRequested;

    public void SetRows(string[] valueLines, SandboxWorldTreeRow[] valueRows)
    {
        ArgumentNullException.ThrowIfNull(valueLines);
        ArgumentNullException.ThrowIfNull(valueRows);
        if (valueLines.Length != valueRows.Length)
            throw new ArgumentException("World tree line and row metadata counts must match.");

        SandboxWorldTreeRow? selected = SelectedItem is int selectedIndex &&
                                            (uint)selectedIndex < (uint)rows.Length
            ? rows[selectedIndex]
            : null;

        lines = valueLines;
        rows = valueRows;
        SetSource(new ObservableCollection<string>(lines));

        if (selected is SandboxWorldTreeRow selectedRow)
        {
            int restored = Array.IndexOf(rows, selectedRow);
            if (restored >= 0)
                SelectedItem = restored;
        }
    }

    internal string RenderedText => string.Join(Environment.NewLine, lines);

    internal bool TryTransferRows(int sourceRow, int targetRow)
    {
        if ((uint)sourceRow >= (uint)rows.Length || (uint)targetRow >= (uint)rows.Length)
            return false;

        SandboxWorldTreeRow source = rows[sourceRow];
        SandboxWorldTreeRow target = rows[targetRow];
        if (source.Kind != SandboxWorldTreeRowKind.Player ||
            string.IsNullOrWhiteSpace(source.PlayerSelector) ||
            target.Kind == SandboxWorldTreeRowKind.Placeholder)
        {
            return false;
        }

        TransferRequested?.Invoke(source.PlayerSelector, target.Target);
        return true;
    }

    internal bool TryInvokeContextActionForSmoke(int row)
    {
        if ((uint)row >= (uint)rows.Length)
            return false;
        SandboxWorldTreeRow item = rows[row];
        if (item.Kind == SandboxWorldTreeRowKind.Player && !string.IsNullOrWhiteSpace(item.PlayerSelector))
        {
            KickRequested?.Invoke(item.PlayerSelector);
            return true;
        }
        if (item.Kind == SandboxWorldTreeRowKind.World && item.Target is SandboxName sandbox)
        {
            DestroyRequested?.Invoke(sandbox);
            return true;
        }
        return false;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Position is Point point)
        {
            int row = point.Y + Viewport.Y;
            if ((uint)row < (uint)rows.Length &&
                (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) || mouse.Flags.HasFlag(MouseFlags.RightButtonPressed)))
            {
                SelectedItem = row;
                SetFocus();
            }

            if (mouse.Flags.HasFlag(MouseFlags.RightButtonPressed) && (uint)row < (uint)rows.Length)
            {
                ShowContextMenu(row, ViewportToScreen(point));
                mouse.Handled = true;
                return true;
            }

            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) &&
                (uint)row < (uint)rows.Length &&
                rows[row].Kind == SandboxWorldTreeRowKind.Player)
            {
                draggedRow = row;
                App?.Mouse.GrabMouse(this);
                mouse.Handled = true;
                return true;
            }
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased) && draggedRow is int sourceRow)
        {
            draggedRow = null;
            App?.Mouse.UngrabMouse();
            if (mouse.Position is not Point releasedAt)
                return true;
            int targetRow = releasedAt.Y + Viewport.Y;
            bool handled = TryTransferRows(sourceRow, targetRow);
            mouse.Handled = handled;
            return handled;
        }

        return base.OnMouseEvent(mouse);
    }

    private void ShowContextMenu(int row, Point screenPoint)
    {
        SandboxWorldTreeRow item = rows[row];
        MenuItem? action = item.Kind switch
        {
            SandboxWorldTreeRowKind.Player when !string.IsNullOrWhiteSpace(item.PlayerSelector) =>
                new MenuItem("Kick", "Disconnect this player", () => KickRequested?.Invoke(item.PlayerSelector)),
            SandboxWorldTreeRowKind.World when item.Target is SandboxName sandbox =>
                new MenuItem("Destroy", "Destroy this sandbox world", () => DestroyRequested?.Invoke(sandbox)),
            SandboxWorldTreeRowKind.World =>
                new MenuItem(
                    commandText: "Destroy",
                    helpText: "Primary world cannot be destroyed",
                    action: null,
                    key: null)
                {
                    Enabled = false
                },
            _ => null
        };
        if (action is null)
            return;

        var menu = new PopoverMenu([action])
        {
            Target = new WeakReference<Terminal.Gui.ViewBase.View>(this)
        };
        menu.MakeVisible(screenPoint);
    }
}
