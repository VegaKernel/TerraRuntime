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
}
