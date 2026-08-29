using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileOwnershipTests
{
    [Fact]
    public void Terraria_1458_reserves_255_as_non_player_server_owner()
    {
        Assert.Equal((byte)254, VanillaProjectileOwnership.MaximumPlayerOwner);
        Assert.Equal(byte.MaxValue, VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaProjectileOwnership.IsPlayerOwned(0));
        Assert.True(VanillaProjectileOwnership.IsPlayerOwned(254));
        Assert.False(VanillaProjectileOwnership.IsPlayerOwned(255));
        Assert.False(VanillaProjectileOwnership.IsServerOwned(254));
        Assert.True(VanillaProjectileOwnership.IsServerOwned(255));
    }
}
