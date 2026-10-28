using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class ScalarDebuffContinuity1458Tests
{
    [Theory]
    [InlineData(22)] [InlineData(30)] [InlineData(32)]
    [InlineData(33)] [InlineData(35)] [InlineData(36)]
    public void A_changed_buff_image_during_the_phase_cannot_adopt_motion_clocks_health_or_entropy(int type)
    {
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(1458, 400, true);
        f.Move(false); f.State.Tick();
        Assert.NotNull(f.Member.ItemPhase);
        var before = f.Buffs; var afflicted = before.Clone();
        Assert.True(afflicted.TryApplyNpcContactBuff(new BuffTypeId(type), 120));
        Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, before, afflicted));
        var item = f.Member.ItemPhase; var health = f.Member.NpcHealth;
        var physics = f.Member.PhysicsPhase; var actor = f.Member.CaptureSnapshot();
        var random = f.Random.Clone(); int reads = 0;
        f.Players.SetNpcHealthWorldFacts(() => {
            if (++reads == 2) Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, afflicted, before));
            return new(false, false);
        });
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.Equal(2, reads);
        Assert.Equal(item, f.Member.ItemPhase); Assert.Equal(health, f.Member.NpcHealth);
        Assert.Equal(physics, f.Member.PhysicsPhase); Assert.Equal(actor, f.Member.CaptureSnapshot());
        Assert.True(random.HasSameState(f.Random));
        Assert.Equal(0, f.Buffs.GetDuration(new BuffTypeId(type)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Silence_blocks_fresh_staff_payment_but_preserves_an_already_paid_birth(bool paidBeforeSilence)
    {
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(1458, 400, true);
        f.SetSlot(0, 739, 1);
        f.Move(false, local: true, success: true); f.State.Tick();
        Assert.NotNull(f.Member.ItemPhase);
        f.Move(true, local: true, success: true);
        if (paidBeforeSilence) f.State.Tick();
        Assert.NotNull(f.Member.ItemPhase);
        var before = f.Buffs; var afflicted = before.Clone();
        Assert.True(afflicted.TryApplyNpcContactBuff(new BuffTypeId(35), 120));
        Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, before, afflicted));
        Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var selected));
        var item = f.Member.ItemPhase; var mana = f.Member.Mana; var random = f.Random.Clone();
        Assert.Equal(paidBeforeSilence, f.Players.TryPrepareGemStaffPayment(f.Connection, selected,
            out _, out _, out _, out _));
        Assert.Equal(item, f.Member.ItemPhase); Assert.Equal(mana, f.Member.Mana);
        Assert.True(random.HasSameState(f.Random));
        if (paidBeforeSilence) Assert.True(item.Value.MagicPayment is { GemStaff: true, EligibleBirth: true });
    }
}
