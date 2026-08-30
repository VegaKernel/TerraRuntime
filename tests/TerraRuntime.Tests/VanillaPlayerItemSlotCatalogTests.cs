using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaPlayerItemSlotCatalogTests
{
    [Theory]
    [InlineData(-1, false, false)]
    [InlineData(0, true, true)]
    [InlineData(98, true, true)]
    [InlineData(99, true, false)]
    [InlineData(499, true, false)]
    [InlineData(699, true, false)]
    [InlineData(700, true, true)]
    [InlineData(989, true, true)]
    [InlineData(990, false, false)]
    [InlineData(short.MaxValue, false, false)]
    public void Matches_official_1458_slot_bounds_and_relay_flags(
        short slot,
        bool valid,
        bool canRelay)
    {
        Assert.Equal(valid, VanillaPlayerItemSlotCatalog.IsValid(slot));
        Assert.Equal(canRelay, VanillaPlayerItemSlotCatalog.CanRelay(slot));
    }

    [Fact]
    public void Relayable_slot_count_is_exact_and_bounded()
    {
        int count = 0;
        for (short slot = 0; slot < VanillaPlayerItemSlotCatalog.Count; slot++)
        {
            if (VanillaPlayerItemSlotCatalog.CanRelay(slot))
                count++;
        }

        Assert.Equal(VanillaPlayerItemSlotCatalog.RelayableCount, count);
        Assert.Equal(389, count);
    }

    [Theory]
    [InlineData(-1, false, false, false, false)]
    [InlineData(0, true, false, false, false)]
    [InlineData(49, true, false, false, false)]
    [InlineData(50, false, true, false, false)]
    [InlineData(53, false, true, false, false)]
    [InlineData(54, false, false, true, false)]
    [InlineData(57, false, false, true, false)]
    [InlineData(58, false, false, false, true)]
    [InlineData(59, false, false, false, false)]
    public void Low_inventory_subranges_are_named_and_non_overlapping(
        short slot,
        bool main,
        bool coin,
        bool ammo,
        bool mouse)
    {
        Assert.Equal(main, VanillaPlayerItemSlotCatalog.IsMainInventorySlot(slot));
        Assert.Equal(coin, VanillaPlayerItemSlotCatalog.IsCoinSlot(slot));
        Assert.Equal(ammo, VanillaPlayerItemSlotCatalog.IsAmmoSlot(slot));
        Assert.Equal(mouse, VanillaPlayerItemSlotCatalog.IsMouseItemSlot(slot));
    }
}
