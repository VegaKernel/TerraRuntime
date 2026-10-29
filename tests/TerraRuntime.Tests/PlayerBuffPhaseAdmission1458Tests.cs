using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Tests;

public sealed class PlayerBuffPhaseAdmission1458Tests
{
    public static IEnumerable<object[]> ContactEffects()
    {
        // All source-owned contact identities, including unsupported effects.
        for (int id = 0; id < 512; id++)
            if (VanillaNpcContactBuffs1458.IsContactBuffType(new BuffTypeId(id)))
                foreach (int duration in new[] { 1, 2 })
                    yield return [id, duration];
    }

    [Theory, MemberData(nameof(ContactEffects))]
    public void Last_contact_effect_must_pass_the_same_phase_admission_as_its_live_timer(int id, int duration)
    {
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(1458, 400, true, true);
        f.Players.SetNpcHealthWorldFacts(static () => new(false, false));
        var before = f.Buffs; var afflicted = before.Clone();
        Assert.True(afflicted.TryApplyNpcContactBuff(new BuffTypeId(id), duration));
        Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, before, afflicted));
        f.Move(false, local: true);
        var checkpoint = f.Random.Clone(); var initial = f.Member.ItemPhase;
        Assert.NotNull(initial);
        Assert.Equal(0, initial.Value.Selected.Animation);
        f.State.Tick();
        bool represented = id is 20 or 22 or 23 or 24 or 30 or 32 or 33 or 35 or 36 or 39;
        Assert.Equal(duration - 1, f.Buffs.GetDuration(new BuffTypeId(id)));
        Assert.Contains(new BuffTypeId(id), f.Buffs.CaptureEffectTypes());
        Assert.Equal(represented, f.Member.ItemPhase.HasValue);
        Assert.Equal(!represented, checkpoint.HasSameState(f.Random));
        if (!represented)
        {
            Assert.Null(f.Member.NpcHealth);
            Assert.Equal(false, f.Member.NpcLifeCurrent);
            Assert.Null(f.Member.PhysicsPhase);
            Assert.Equal(1600f, f.Member.PositionX);
            Assert.Equal(1606f, f.Member.PositionY);
            Assert.Equal(0, f.Member.ItemAnimation);
        }
        else
        {
            Assert.True(f.Member.NpcHealth is { SourceProfileKnown: true });
            if (id is 20 or 24 or 39)
            {
                // Source UpdateLifeRegen consumes the final positive effect
                // even when UpdateBuffs has already removed its duration-one slot.
                int rate = id == 20 ? -4 : id == 24 ? -8 : -24;
                Assert.Equal(rate, f.Member.NpcHealth.Value.RegenCount);
                Assert.Equal(1f, f.Member.NpcHealth.Value.RegenTime);
            }
        }
    }

    [Fact]
    public void Fallback_health_cannot_adopt_a_consumed_effect_snapshot_changed_by_its_world_provider()
    {
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(1458, 400, true, true);
        var before = f.Buffs; var poisoned = before.Clone();
        Assert.True(poisoned.TryApplyNpcContactBuff(new BuffTypeId(20), 1));
        Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, before, poisoned));
        f.Players.AdvanceCombatTick(1);
        Assert.Equal(0, f.Buffs.GetDuration(new BuffTypeId(20)));
        Assert.Contains(new BuffTypeId(20), f.Buffs.CaptureEffectTypes());
        var health = f.Member.NpcHealth; var random = f.Random.Clone(); int reads = 0;
        f.Players.SetNpcHealthWorldFacts(() => {
            reads++;
            var current = f.Buffs;
            current.CaptureSlots(out var types, out var durations);
            var changed = PlayerBuffState.FromSlots(types, durations);
            Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, current, changed));
            return new(false, false);
        });
        f.Players.TickHealthContext();
        Assert.Equal(1, reads);
        Assert.Equal(health, f.Member.NpcHealth);
        Assert.True(random.HasSameState(f.Random));
        Assert.Empty(f.Buffs.CaptureEffectTypes());
    }
}
