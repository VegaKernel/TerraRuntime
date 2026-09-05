using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class RuntimeConnectionDirectoryPlayerNameTests
{
    [Fact]
    public void Player_names_are_unique_process_wide_case_insensitively()
    {
        var directory = new RuntimeConnectionDirectory();
        GameCommandSourceId first = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId second = GameCommandSourceId.FromConnection(2);

        Assert.True(directory.TryReservePlayerName(first, "Test"));
        Assert.False(directory.TryReservePlayerName(second, "test"));
        Assert.False(directory.TryReservePlayerName(second, " TEST "));
    }

    [Fact]
    public void Same_source_can_keep_or_rename_its_reservation_atomically()
    {
        var directory = new RuntimeConnectionDirectory();
        GameCommandSourceId first = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId second = GameCommandSourceId.FromConnection(2);

        Assert.True(directory.TryReservePlayerName(first, "Alpha"));
        Assert.True(directory.TryReservePlayerName(first, "alpha"));
        Assert.True(directory.TryReservePlayerName(first, "Bravo"));
        Assert.True(directory.TryReservePlayerName(second, "ALPHA"));
        Assert.False(directory.TryReservePlayerName(second, "bravo"));
    }

    [Fact]
    public void Invalid_or_system_sources_cannot_reserve_player_names()
    {
        var directory = new RuntimeConnectionDirectory();
        GameCommandSourceId first = GameCommandSourceId.FromConnection(1);

        Assert.False(directory.TryReservePlayerName(GameCommandSourceId.System, "Alpha"));
        Assert.False(directory.TryReservePlayerName(first, "   "));
    }
}
