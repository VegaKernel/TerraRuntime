using System.Collections.ObjectModel;
using TerraRuntime.Application.TerminalUI;
using Terminal.Gui.Views;

namespace TerraRuntime.Tests;

public sealed class CyclingDropDownListTests
{
    [Fact]
    public void Closed_cycle_advances_and_wraps()
    {
        using var dropDown = new CyclingDropDownList
        {
            ReadOnly = true,
            Source = new ListWrapper<string>(new ObservableCollection<string>(["One", "Two", "Three"])),
            Text = "One"
        };

        Assert.True(dropDown.TrySelectNextCyclic());
        Assert.Equal("Two", dropDown.Text);
        Assert.True(dropDown.TrySelectNextCyclic());
        Assert.Equal("Three", dropDown.Text);
        Assert.True(dropDown.TrySelectNextCyclic());
        Assert.Equal("One", dropDown.Text);
    }

    [Fact]
    public void Cycle_selects_first_when_text_is_not_in_source()
    {
        using var dropDown = new CyclingDropDownList
        {
            ReadOnly = true,
            Source = new ListWrapper<string>(new ObservableCollection<string>(["One", "Two"])),
            Text = "Unknown"
        };

        Assert.True(dropDown.TrySelectNextCyclic());
        Assert.Equal("One", dropDown.Text);
    }
}
