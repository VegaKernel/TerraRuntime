namespace TerraRuntime.Tests;

public sealed class OwnedLocalWeaponClockAuthority1458Tests
{
    [Fact]
    public void Original_local_common_clocks_metadata_and_mana_order_match_both_1458_editions() =>
        LocalWeaponClockSourceChecks1458.Run();

    [Fact]
    public void Owned_weapon_source_tails_preserve_clock_resource_and_shared_random_before_tools() =>
        Assert.Equal(203,LocalWeaponClockRuntimeChecks1458.Run());
}
