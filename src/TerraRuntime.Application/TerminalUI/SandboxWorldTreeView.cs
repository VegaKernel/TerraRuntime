using System.Drawing;
using TerraRuntime.Contracts.Runtime;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

#pragma warning disable CS0618 // Terminal.Gui TextView is still the built-in selectable read-only surface in 2.4.17.

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
/// Read-only world roster with one narrow interaction: dragging a player row onto a world (or one of that world's
/// player rows) requests the existing semantic Level 1 transfer operation.
/// </summary>
internal sealed class SandboxWorldTreeView : TextView
{
    private SandboxWorldTreeRow[] rows = [];
    private int? draggedRow;

    public SandboxWorldTreeView()
    {
        ReadOnly = true;
        WordWrap = false;
        TabKeyAddsTab = false;
        EnterKeyAddsLine = false;
    }

    public event Action<string, SandboxName?>? TransferRequested;

    public void SetRows(SandboxWorldTreeRow[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        rows = value;
    }

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

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) && mouse.Position is Point pressedAt)
        {
            int row = pressedAt.Y + Viewport.Y;
            if ((uint)row < (uint)rows.Length && rows[row].Kind == SandboxWorldTreeRowKind.Player)
            {
                draggedRow = row;
                SetFocus();
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
}
