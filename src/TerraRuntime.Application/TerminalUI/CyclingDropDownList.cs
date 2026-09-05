using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace TerraRuntime.Application.TerminalUI;

/// <summary>
/// Read-only dropdown that keeps the normal single-click popover behavior while allowing a closed control to
/// advance to the next item with a double-click. The selection wraps from the final item to the first.
/// </summary>
internal sealed class CyclingDropDownList : DropDownList
{
    private bool openedByLeftClick;

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            // Terminal.Gui delivers the first click of a double-click normally, so a read-only DropDownList can
            // already have opened its popover before the double-click event arrives. Close that popover again and
            // treat the gesture as a cycle operation instead of leaving a list open underneath the new value.
            var popovers = App?.Popovers;
            if (openedByLeftClick && popovers is not null && popovers.GetActivePopover() is { } activePopover)
                popovers.Hide(activePopover);

            openedByLeftClick = false;
            if (TrySelectNextCyclic())
            {
                mouse.Handled = true;
                return true;
            }
        }

        bool leftClick = mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked);
        bool handled = base.OnMouseEvent(mouse);
        if (leftClick)
            openedByLeftClick = true;
        return handled;
    }

    internal bool TrySelectNextCyclic()
    {
        if (!Enabled || Source is null || Source.Count == 0)
            return false;

        System.Collections.IList items = Source.ToList();
        int current = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i]?.ToString(), Text, StringComparison.Ordinal))
            {
                current = i;
                break;
            }
        }

        int next = current < 0 || current + 1 >= items.Count ? 0 : current + 1;
        Text = items[next]?.ToString() ?? string.Empty;
        return true;
    }
}
