namespace TerraRuntime.Tests;

public sealed partial class OwnedAmmoUseContinuity1458Tests
{
    [Fact]
    public void Source_inventory_5_before_first_27_retains_one_unpaid_offer_without_a_cursor_debit()
    {
        foreach (int weapon in new[] { 39, 98, 534, 4703 })
        {
            using var source = Read(weapon, "repeat", 0);
            var row = source.RootElement;
            using var f = Start(row);
            f.Fund(54, 12);
            var step = row.GetProperty("steps")[0];
            f.Control(0, true);
            var before = f.PlayerRandom.Clone();
            // The actual source packet5 tail arrives before any packet27.
            // It certifies a possible source debit; the owned phase must still
            // execute PickAmmo and pay exactly once before projectile delivery.
            foreach (var frame in step.GetProperty("syncFrames").EnumerateArray())
                f.OriginalAmmoFrame(Convert.FromHexString(frame.GetString()!));
            Assert.Equal(12, f.Item(54).Stack);
            Assert.Equal(12, f.Funded(54));
            Assert.True(f.PlayerRandom.HasSameState(before));
            Assert.Equal(0, f.Projectiles.PromotedClientProjectileSpawns);
            Assert.NotNull(f.ToolClock.WeaponShootHistory!.PendingCapture);
            Assert.Empty(f.ToolClock.WeaponShootHistory.PendingReports);
            f.State.Tick();
            SameRandom(step.GetProperty("random"), f.PlayerRandom, "early5 source phase");
            int remaining = step.GetProperty("after").GetProperty("inventory")[54].GetProperty("stack").GetInt32();
            Assert.Equal(remaining, f.Item(54).Stack);
            Assert.Equal(remaining, f.Funded(54));
            Assert.Equal(0, f.Projectiles.PromotedClientProjectileSpawns);
            var paidRandom = f.PlayerRandom.Clone();
            int births = 0;
            foreach (var frame in step.GetProperty("checkFrames").EnumerateArray())
            {
                var wire = Convert.FromHexString(frame.GetString()!);
                if (wire[2] != 27) continue;
                births++;
                f.OriginalAmmoFrame(wire);
            }
            Assert.Equal(births, f.Projectiles.PromotedClientProjectileSpawns);
            Assert.InRange(births, 1, 8);
            Assert.Equal(remaining, f.Item(54).Stack);
            Assert.Equal(remaining, f.Funded(54));
            Assert.True(f.PlayerRandom.HasSameState(paidRandom));
            Assert.Equal(0, f.State.RejectedPlayerEquipmentUpdates);
        }
    }
}
