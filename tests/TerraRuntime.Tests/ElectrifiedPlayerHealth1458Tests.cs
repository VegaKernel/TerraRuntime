using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class ElectrifiedPlayerHealth1458Tests
{
    private static IEnumerable<JsonElement> Rows(string name)
    {
        using var source = typeof(ElectrifiedPlayerHealth1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.ElectrifiedPlayerHealthLinux1458.v1.json.gz")!;
        using var zip = new GZipStream(source, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(zip);
        foreach (var row in doc.RootElement.GetProperty(name).EnumerateArray()) yield return row.Clone();
    }
    public static IEnumerable<object[]> Components() => Rows("component").Select(r => new object[] { r.GetRawText() });
    public static IEnumerable<object[]> WholeLocal() => Rows("whole").Where(r => r.GetProperty("seed").GetInt32() == 1458 &&
        r.GetProperty("role").GetInt32() == 0 && r.GetProperty("duration").GetInt32() == 1 &&
        r.GetProperty("scene").GetString() is "idle" or "left" or "right" or "coast")
        .Select(r => new object[] { r.GetRawText() });

    [Theory, MemberData(nameof(Components))]
    public void Existing_health_kernel_matches_actual_original_regen_flags_counts_and_timer_order(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var row = doc.RootElement;
        var initial = row.GetProperty("before").GetProperty("player");
        PlayerNpcHealthState1458? state = new(initial.GetProperty("statLife").GetInt32(),
            initial.GetProperty("lifeRegenCount").GetInt32(), F(initial, "lifeRegenTime"), true);
        foreach (var frame in row.GetProperty("steps").EnumerateArray())
        {
            var before = frame.GetProperty("before").GetProperty("player");
            var after = frame.GetProperty("after").GetProperty("player");
            int regeneration = 0;
            for (int i = 0; i < 44; i++)
                if (before.GetProperty("buffType")[i].GetInt32() == 2 && before.GetProperty("buffTime")[i].GetInt32() > 0)
                    regeneration++;
            state = VanillaRemotePlayerHealth1458.Step(state, 100, false, false, false, true, false,
                F(before.GetProperty("velocity")[0]) != 0f, regeneration,
                after.GetProperty("poisoned").GetBoolean(), after.GetProperty("onFire").GetBoolean(),
                after.GetProperty("onFire2").GetBoolean(), false, false, false,
                after.GetProperty("electrified").GetBoolean(), before.GetProperty("controlLeft").GetBoolean() ||
                    before.GetProperty("controlRight").GetBoolean());
            Assert.NotNull(state);
            Assert.Equal(after.GetProperty("statLife").GetInt32(), state.Value.Life);
            Assert.Equal(after.GetProperty("lifeRegenCount").GetInt32(), state.Value.RegenCount);
            Assert.Equal(F(after, "lifeRegenTime"), state.Value.RegenTime);
        }
    }

    [Theory, MemberData(nameof(WholeLocal))]
    public void Actual_local_player_phase_keeps_last_contact_effect_then_continues_with_the_original_rng(string json)
    {
        using var doc = JsonDocument.Parse(json); var row = doc.RootElement;
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(1458, 400, true, true);
        var source = row.GetProperty("before").GetProperty("player");
        f.Member.NpcHealth = new(source.GetProperty("statLife").GetInt32(),
            source.GetProperty("lifeRegenCount").GetInt32(), F(source, "lifeRegenTime"), true);
        var owned = f.Buffs.Clone();
        Assert.True(owned.TryApplyNpcContactBuff(VanillaBuffIds.Electrified, 1));
        Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, f.Buffs, owned));
        bool first = true;
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            var before = step.GetProperty("before").GetProperty("player");
            byte controls = (byte)((before.GetProperty("controlLeft").GetBoolean() ? 4 : 0) |
                (before.GetProperty("controlRight").GetBoolean() ? 8 : 0));
            float vx = first ? F(before.GetProperty("velocity")[0]) : f.Member.VelocityX;
            float vy = first ? F(before.GetProperty("velocity")[1]) : f.Member.VelocityY;
            Move(f, controls, true, vx, vy); first = false;
            f.State.Tick();
            var after = step.GetProperty("after").GetProperty("player");
            Assert.NotNull(f.Member.ItemPhase);
            Assert.NotNull(f.Member.NpcHealth);
            Assert.Equal(after.GetProperty("statLife").GetInt32(), f.Member.NpcHealth.Value.Life);
            Assert.Equal(after.GetProperty("lifeRegenCount").GetInt32(), f.Member.NpcHealth.Value.RegenCount);
            Assert.Equal(F(after, "lifeRegenTime"), f.Member.NpcHealth.Value.RegenTime);
            Assert.Equal(after.GetProperty("electrified").GetBoolean(), f.Buffs.CaptureDebuffFlags().Electrified);
            AnkhEquipmentImmunity1458Tests.Slots(after, f.Buffs);
            Assert.Equal(F(after.GetProperty("position")[0]), f.Member.PositionX);
            Assert.Equal(F(after.GetProperty("position")[1]), f.Member.PositionY);
            Assert.Equal(F(after.GetProperty("velocity")[0]), f.Member.VelocityX);
            Assert.Equal(F(after.GetProperty("velocity")[1]), f.Member.VelocityY);
            AnkhEquipmentImmunity1458Tests.RandomState(step.GetProperty("after").GetProperty("rng"), f.Random);
        }
    }

    [Fact]
    public void Local_both_buttons_with_electrified_refuse_before_contact_timers_health_and_entropy()
    {
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(1458, 400, true, true);
        Move(f, 8, true, 0f, 0f); f.State.Tick();
        var before = f.Buffs; var afflicted = before.Clone();
        Assert.True(afflicted.TryApplyNpcContactBuff(VanillaBuffIds.Electrified, 1));
        Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, before, afflicted));
        Move(f, 12, true, f.Member.VelocityX, f.Member.VelocityY);
        var health = f.Member.NpcHealth; var item = f.Member.ItemPhase; var physics = f.Member.PhysicsPhase;
        var random = f.Random.Clone(); var buffs = f.Buffs.Clone();
        f.Players.AdvanceCombatTick(100);
        Assert.True(buffs.HasSameSlots(f.Buffs));
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.Equal(health, f.Member.NpcHealth);
        Assert.Equal(item, f.Member.ItemPhase); Assert.Equal(physics, f.Member.PhysicsPhase);
        Assert.True(buffs.HasSameSlots(f.Buffs));
        Assert.True(random.HasSameState(f.Random));
        f.Players.TickHealthContext();
        Assert.Null(f.Member.NpcHealth); // Retire unsupported clock provenance, retain reported HP.
        Assert.Equal(100, f.Member.Life);
        Assert.Equal(1, f.Buffs.GetDuration(VanillaBuffIds.Electrified));
        Assert.True(random.HasSameState(f.Random));
    }

    [Fact]
    public void Current_electrified_flags_participate_in_clone_equality_and_clear_after_last_frame_or_death()
    {
        var before = new PlayerBuffState();
        Assert.True(before.TryApplyNpcContactBuff(VanillaBuffIds.Electrified, 1));
        var current = before.Clone(); current.TickNpcContactBuffs();
        Assert.Equal(0, current.GetDuration(VanillaBuffIds.Electrified));
        Assert.True(current.CaptureDebuffFlags().Electrified);
        var copy = current.Clone(); Assert.True(copy.HasSameSlots(current));
        copy.TickNpcContactBuffs();
        Assert.False(copy.CaptureDebuffFlags().Electrified);
        Assert.False(copy.HasSameSlots(current));
        Assert.True(current.CaptureDebuffFlags().Electrified);
        current.ClearNonPersistentOnDeath();
        Assert.False(current.CaptureDebuffFlags().Electrified);
    }

    [Fact]
    public void Control_callback_mutation_cannot_adopt_a_planned_electrified_phase()
    {
        using var f = new AnkhEquipmentPhase1458Tests.Fixture(1458, 400, true);
        Move(f, 0, false, 0f, 0f); f.State.Tick();
        var before = f.Buffs; var afflicted = before.Clone();
        Assert.True(afflicted.TryApplyNpcContactBuff(VanillaBuffIds.Electrified, 90));
        Assert.True(f.Profiles.TryAdoptBuffState(f.Connection, before, afflicted));
        var item = f.Member.ItemPhase; var health = f.Member.NpcHealth; var physics = f.Member.PhysicsPhase;
        var random = f.Random.Clone(); int reads = 0;
        f.Players.SetNpcHealthWorldFacts(() =>
        {
            if (++reads == 2) Move(f, 4, false, f.Member.VelocityX, f.Member.VelocityY);
            return new(false, false);
        });
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.Equal(2, reads);
        Assert.Equal(item, f.Member.ItemPhase); Assert.Equal(health, f.Member.NpcHealth);
        Assert.Equal(physics, f.Member.PhysicsPhase); Assert.True(random.HasSameState(f.Random));
        Assert.Equal(4, f.Member.ControlFlags & 12);
    }

    private static void Move(AnkhEquipmentPhase1458Tests.Fixture f, byte controls, bool local, float vx, float vy) =>
        f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection, new(f.Member.Slot, controls,
            (byte)(16 | (vx != 0f || vy != 0f ? 4 : 0)), 0, 0, 0, f.Member.PositionX, f.Member.PositionY,
            vx != 0f || vy != 0f, vx, vy, false, 0, false, 0, 0, 0, 0, false, 0, 0))
            { ItemUseCaller = local ? RuntimeItemUseCaller1458.LocalOwnerProjection : RuntimeItemUseCaller1458.DedicatedRemote });
    private static float F(JsonElement value, string name) => F(value.GetProperty(name));
    private static float F(JsonElement value) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));
}
