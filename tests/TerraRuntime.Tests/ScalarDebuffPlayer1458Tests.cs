using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Application;

namespace TerraRuntime.Tests;

public sealed class ScalarDebuffPlayer1458Tests
{
    public static IEnumerable<object[]> RemoteSword()
    {
        using var stream = typeof(ScalarDebuffPlayer1458Tests).Assembly.GetManifestResourceStream("ScalarDebuffPlayerLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var capture in document.RootElement.GetProperty("captures").EnumerateArray())
            foreach (var row in capture.GetProperty("rows").EnumerateArray())
                if (row.GetProperty("role").GetInt32() == 255 &&
                    row.GetProperty("duration").GetInt32() == 120 && row.GetProperty("secondBuff").GetInt32() is 0 or 20 &&
                    !row.GetProperty("reverse").GetBoolean())
                    yield return [capture.GetProperty("seed").GetInt32(), row.GetRawText()];
    }

    [Theory, MemberData(nameof(RemoteSword))]
    public void Actual_original_scalar_effect_cure_and_motion_continue_on_the_owned_remote_player(int seed, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(seed, 400, true);
        int weapon = row.GetProperty("weapon").GetInt32();
        f.SetSlot(0, (short)weapon, 1);
        if (weapon == 39) f.SetSlot(54, 40, 20);
        f.SetSlot(59, 89, 1);
        int gear = row.GetProperty("gear").GetInt32();
        if (gear != 0) f.SetSlot(62, (short)gear, 1, 65);
        f.Restore(row.GetProperty("before").GetProperty("player"));
        AssertInitialPreimage(row, f);
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            f.Move(step.GetProperty("held").GetBoolean(), f.Member.VelocityX, f.Member.VelocityY);
            var expectedBefore = step.GetProperty("before").GetProperty("player");
            Assert.Equal(F(expectedBefore.GetProperty("velocity")[0]), f.Member.VelocityX);
            Assert.Equal(F(expectedBefore.GetProperty("velocity")[1]), f.Member.VelocityY);
            f.State.Tick();
            AssertAfter(step, f);
        }
    }

    public static IEnumerable<object[]> FinalLocalEffects()
    {
        using var stream = typeof(ScalarDebuffPlayer1458Tests).Assembly.GetManifestResourceStream("ScalarDebuffPlayerLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var capture in document.RootElement.GetProperty("captures").EnumerateArray())
            if (capture.GetProperty("seed").GetInt32() == 1458)
                foreach (var row in capture.GetProperty("rows").EnumerateArray())
                    if (row.GetProperty("role").GetInt32() == 0 && row.GetProperty("weapon").GetInt32() == 24 &&
                        row.GetProperty("duration").GetInt32() == 1 && row.GetProperty("secondBuff").GetInt32() == 26 &&
                        row.GetProperty("gear").GetInt32() is 0 or 1612)
                        yield return [capture.GetProperty("seed").GetInt32(), row.GetRawText()];
    }

    [Theory, MemberData(nameof(FinalLocalEffects))]
    public void Final_contact_frame_keeps_source_food_order_and_late_cure_effects(int seed, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        // The actor joins an already running clock. This phase advances the
        // contact timer; server tick0 itself is the owner's initial epoch.
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(seed, 400, true, true);
        f.SetSlot(59, 89, 1);
        if (row.GetProperty("gear").GetInt32() != 0) f.SetSlot(62, 1612, 1, 65);
        var owned = new PlayerBuffState();
        var input = row.GetProperty("before").GetProperty("player");
        for (int i = 0; i < 44; i++)
        {
            var type = new BuffTypeId(input.GetProperty("buffType")[i].GetInt32());
            int duration = input.GetProperty("buffTime")[i].GetInt32();
            if (type.Value == 0) continue;
            Assert.True(type.Value == 26 ? owned.TryApplySelectedConsumable(type, duration, false) :
                owned.TryApplyNpcContactBuff(type, duration));
        }
        Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, f.Buffs, owned));
        f.Move(false, local: true);
        AssertInitialPreimage(row, f);
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            f.Move(step.GetProperty("held").GetBoolean(), f.Member.VelocityX, f.Member.VelocityY,
                local: true, success: true);
            f.State.Tick();
            AssertAfter(step, f);
        }
    }

    private static void AssertAfter(JsonElement step, AnkhEquipmentPhase1458Tests.Fixture f)
    {
        Assert.NotNull(f.Member.ItemPhase);
        var phase = f.Member.ItemPhase.Value;
        Assert.NotNull(phase.DerivedCombat);
        var combat = phase.DerivedCombat.Value;
        var source = step.GetProperty("after").GetProperty("player");
        AnkhEquipmentImmunity1458Tests.Slots(source, f.Buffs);
        Assert.Equal(source.GetProperty("statDefense").GetInt32(), combat.Defense);
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(source, "meleeDamage"), combat.MeleeDamage);
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(source, "rangedDamage"), combat.RangedDamage);
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(source, "magicDamage"), combat.MagicDamage);
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(source, "minionDamage"), combat.MinionDamage);
        // The original late CapAttackSpeeds stores this reciprocal.
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(source, "meleeSpeed"), combat.MeleeAnimationMultiplier);
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(source, "moveSpeed"), combat.MoveSpeed);
        Assert.Equal(source.GetProperty("statLife").GetInt32(), f.Member.NpcHealth!.Value.Life);
        Assert.Equal(source.GetProperty("lifeRegenCount").GetInt32(), f.Member.NpcHealth.Value.RegenCount);
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(source, "lifeRegenTime"), f.Member.NpcHealth.Value.RegenTime);
        Assert.Equal(source.GetProperty("blind").GetBoolean(), f.Buffs.HasDarknessEffect);
        Assert.Equal(source.GetProperty("bleed").GetBoolean(), f.Buffs.HasBleedingEffect);
        Assert.Equal(source.GetProperty("slow").GetBoolean(), f.Buffs.HasSlowEffect);
        Assert.Equal(source.GetProperty("silence").GetBoolean(), f.Buffs.HasSilenceEffect);
        Assert.Equal(source.GetProperty("brokenArmor").GetBoolean(), f.Buffs.HasBrokenArmorEffect);
        Assert.Equal(source.GetProperty("statMana").GetInt32(), phase.Mana.Mana);
        Assert.Equal(source.GetProperty("itemAnimation").GetInt32(), phase.Selected.Animation);
        Assert.Equal(source.GetProperty("itemTime").GetInt32(), phase.Selected.ItemTime);
        var position = source.GetProperty("position"); var velocity = source.GetProperty("velocity");
        Assert.Equal(F(position[0]), f.Member.PositionX); Assert.Equal(F(position[1]), f.Member.PositionY);
        Assert.Equal(F(velocity[0]), f.Member.VelocityX); Assert.Equal(F(velocity[1]), f.Member.VelocityY);
        AnkhEquipmentImmunity1458Tests.RandomState(step.GetProperty("after").GetProperty("rng"), f.Random);
    }

    public static IEnumerable<object[]> BothControls()
    {
        using var stream = typeof(ScalarDebuffPlayer1458Tests).Assembly.GetManifestResourceStream("ScalarDebuffControlsLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var capture in document.RootElement.GetProperty("captures").EnumerateArray())
            foreach (var row in capture.GetProperty("rows").EnumerateArray())
                yield return [capture.GetProperty("seed").GetInt32(), row.GetRawText()];
    }

    [Theory, MemberData(nameof(BothControls))]
    public void Both_controls_choose_the_source_direction_using_this_frames_reduced_speed_cap(int seed, string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(seed, 400, true);
        f.SetSlot(59, 89, 1);
        var source = row.GetProperty("before").GetProperty("player");
        f.Restore(source);
        f.Move(false, F(source.GetProperty("velocity")[0]), 0f, both: true);
        AssertInitialPreimage(row, f, 255);
        f.State.Tick();
        AssertAfter(row, f);
    }

    private static void AssertInitialPreimage(JsonElement row, AnkhEquipmentPhase1458Tests.Fixture f, int? role = null)
    {
        var before = row.GetProperty("before"); var p = before.GetProperty("player");
        Assert.Equal(role ?? row.GetProperty("role").GetInt32(), p.GetProperty("actualMyPlayer").GetInt32());
        Assert.Equal(2, p.GetProperty("actualNetMode").GetInt32());
        Assert.False(p.GetProperty("dead").GetBoolean()); Assert.False(f.Member.IsDead);
        Assert.True(p.GetProperty("active").GetBoolean());
        Assert.NotNull(f.Member.ItemPhase); var item = f.Member.ItemPhase.Value;
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(p, "manaHeat"), item.ManaHeat);
        Assert.Equal(p.GetProperty("statMana").GetInt32(), f.Member.Mana);
        Assert.Equal(p.GetProperty("statManaMax2").GetInt32(), f.Member.MaxMana);
        Assert.Equal(p.GetProperty("itemAnimation").GetInt32(), item.Selected.Animation);
        Assert.Equal(p.GetProperty("itemTime").GetInt32(), item.Selected.ItemTime);
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(p, "manaRegenDelay"), item.Mana.Delay);
        Assert.Equal(p.GetProperty("manaRegenCount").GetInt32(), item.Mana.Count);
        Assert.NotNull(f.Member.NpcHealth); var health = f.Member.NpcHealth.Value;
        Assert.True(health.SourceProfileKnown);
        Assert.Equal(p.GetProperty("statLife").GetInt32(), health.Life);
        Assert.Equal(p.GetProperty("statLifeMax2").GetInt32(), f.Member.DerivedLifeMax);
        Assert.Equal(p.GetProperty("lifeRegenCount").GetInt32(), health.RegenCount);
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(p, "lifeRegenTime"), health.RegenTime);
        Assert.Equal(F(p.GetProperty("position")[0]), f.Member.PositionX);
        Assert.Equal(F(p.GetProperty("position")[1]), f.Member.PositionY);
        Assert.Equal(F(p.GetProperty("velocity")[0]), f.Member.VelocityX);
        Assert.Equal(F(p.GetProperty("velocity")[1]), f.Member.VelocityY);
        Assert.False(p.GetProperty("noItems").GetBoolean()); Assert.False(f.Buffs.HasNoItemsEffect);
        AnkhEquipmentImmunity1458Tests.Slots(p, f.Buffs);
        int slot = 0;
        foreach (var source in p.GetProperty("inventory").EnumerateArray())
        {
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, slot++, out var actual));
            Assert.Equal(source[0].GetInt32(), actual.ItemType.Value);
            Assert.Equal(source[1].GetInt32(), actual.Stack);
            Assert.Equal(source[2].GetInt32(), actual.Prefix.Value);
        }
        AnkhEquipmentImmunity1458Tests.RandomState(before.GetProperty("rng"), f.Random);
    }
    private static float F(JsonElement value) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));
}
