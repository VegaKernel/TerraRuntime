using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Genuine_honey_and_lava_casts_use_world_liquid_and_pole_gates()
    {
        foreach (var liquid in new[] { WorldLiquidKind.Honey, WorldLiquidKind.Lava })
        {
            using var f = new FishingFixture(8, 2674, liquid, liquid == WorldLiquidKind.Lava ? (short)2422 : (short)2294);
            bool rewarded = false;
            for (int cast = 0; cast < 6 && !rewarded; cast++)
            {
                f.Cast(); var bite = f.WaitForBite(16000);
                Assert.True(f.Shots.TryGetLifecycle(bite.Snapshot.Handle, out var life));
                Assert.Equal(liquid == WorldLiquidKind.Lava, life.Liquid.LavaWet);
                Assert.Equal(liquid == WorldLiquidKind.Honey, life.Liquid.HoneyWet);
                int item = bite.Result.ItemType;
                if (liquid == WorldLiquidKind.Honey) Assert.Equal(2314, item);
                else Assert.Contains(item, new[] { 4877, 4819, 4820, 4872, 2312, 2315 });
                f.Pull();
                bool paid = f.Authority.CaptureFishingBobber(f.Connection)?.PaidCatch == true;
                f.Return();
                if (paid) { Assert.True(f.Count(item) > 0); rewarded = true; }
            }
            Assert.True(rewarded);
        }
        using (var denied = new FishingFixture(1, 2674, WorldLiquidKind.Lava))
        {
            denied.Cast(); bool enteredLava = false;
            for (int tick = 0; tick < 1800; tick++)
            {
                denied.Tick(); var bobber = denied.Authority.CaptureFishingBobber(denied.Connection);
                Assert.NotNull(bobber); Assert.False(bobber.Result.HasBite);
                Assert.True(denied.Shots.TryGetLifecycle(bobber.Snapshot.Handle, out var life));
                enteredLava |= life.Liquid.LavaWet;
            }
            Assert.True(enteredLava);
            Assert.Equal(1, denied.Funded(denied.BaitSlot));
            Assert.Equal(1, denied.Item(denied.BaitSlot).Stack);
            denied.Pull(); denied.Return();
            Assert.Equal(1, denied.Funded(denied.BaitSlot));
        }
    }
}
