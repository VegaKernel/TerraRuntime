using TerraRuntime.Operations;
using TerraRuntime.TerminalUI;

namespace TerraRuntime.Tests;

public sealed class ProjectileDisplayFormatterTests
{
    [Fact]
    public void Known_projectile_types_use_catalog_names_and_unknown_types_keep_numeric_identity()
    {
        Assert.Equal("WoodenArrowFriendly (#1)", ProjectileDisplayFormatter.FormatType(1));
        Assert.Equal("FireArrow (#2)", ProjectileDisplayFormatter.FormatType(2));
        Assert.Equal("Shuriken (#3)", ProjectileDisplayFormatter.FormatType(3));
        Assert.Equal("projectile #417", ProjectileDisplayFormatter.FormatType(417));
    }

    [Fact]
    public void Owner_formatting_resolves_live_player_server_world_and_unresolved_slot()
    {
        RuntimePlayerSnapshot[] players =
        [
            new RuntimePlayerSnapshot(
                ConnectionId: 17,
                Slot: 4,
                Generation: 2,
                Name: "Arrow\nOwner",
                Team: 0,
                PositionX: 0f,
                PositionY: 0f,
                VelocityX: 0f,
                VelocityY: 0f,
                SelectedItem: 0,
                MountType: 0,
                HasHealth: false,
                Life: 0,
                MaxLife: 0,
                HasMana: false,
                Mana: 0,
                MaxMana: 0)
        ];

        Assert.Equal("owner Arrow Owner(#4)", ProjectileDisplayFormatter.FormatOwner(4, players));
        Assert.Equal("server/world", ProjectileDisplayFormatter.FormatOwner(byte.MaxValue, players));
        Assert.Equal("owner slot #7", ProjectileDisplayFormatter.FormatOwner(7, players));
    }
}
