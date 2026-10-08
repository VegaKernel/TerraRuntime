using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class SelectedConsumablePhases1458Tests
{
    [Fact]
    public void Empty_constructor_item_retains_ambient_random_and_timers_in_actual_continuous_remote_phases()
    {
        using var source = Read("EmptySelectedConsumablePhase1458");
        int compared = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            Assert.Equal(0, I(row, "useStyle"));
            Assert.True(VanillaSelectedConsumableCatalog1458.TryGet(VanillaItemIds.None, out var item));
            Assert.Equal(I(row, "useTime"), item.UseTime);
            Assert.Equal(I(row, "useAnimation"), item.UseAnimation);
            Assert.Equal(0, item.DrinkColourCount);
            var events = row.GetProperty("sourcePhaseEvents").EnumerateArray().ToArray();
            var random = new VanillaUnifiedRandom1458(0);
            var mana = new PlayerManaRegenerationState1458(10, 200, 0, 0, 0);
            var selected = SelectedState(events.First(e => e.GetProperty("phase").GetString() == "ItemCheck:before"));
            float heat = 0;
            foreach (var expected in row.GetProperty("phases").EnumerateArray())
            {
                int tick = I(expected, "tick");
                Assert.True(VanillaSelectedConsumable1458.TryStepHeat(heat, random.NextDouble, random.Next, out heat));
                var manaBefore = events.Single(e => I(e, "tick") == tick && e.GetProperty("phase").GetString() == "UpdateManaRegen:before");
                var manaFacts = ManaFacts(manaBefore);
                Assert.True(VanillaRemoteManaRegeneration1458.TryStep(mana, in manaFacts, out mana, out int rate));
                var manaAfter = events.Single(e => I(e, "tick") == tick && e.GetProperty("phase").GetString() == "UpdateManaRegen:after");
                Assert.Equal(ManaState(manaAfter), mana);
                Assert.Equal(I(manaAfter, "rate"), rate);
                var before = events.Single(e => I(e, "tick") == tick && e.GetProperty("phase").GetString() == "ItemCheck:before");
                Assert.Equal(selected, SelectedState(before));
                AssertRandom(before, random);
                var facts = new PlayerSelectedConsumableFacts1458(VanillaItemIds.None, 50, 400, mana.Mana, 200,
                    B(before, "control"), B(before, "success"), false, false, true, false);
                Assert.True(VanillaSelectedConsumable1458.TryStep(selected, in facts, random.Next, random.NextDouble, out var result));
                selected = result.State;
                Assert.False(result.BeganUse);
                Assert.Equal(0, result.PotionSicknessOffer);
                Assert.Equal(0, result.ManaSicknessOffer);
                Assert.Equal(50, result.Life);
                Assert.Equal(mana.Mana, result.Mana);
                var after = events.Single(e => I(e, "tick") == tick && e.GetProperty("phase").GetString() == "ItemCheck:after");
                Assert.Equal(SelectedState(after), selected);
                AssertRandom(after, random);
                Assert.Equal(0, I(expected, "stack"));
                Assert.Equal(0, I(expected, "type"));
                compared++;
            }
            Assert.All(row.GetProperty("frames").EnumerateArray(), frame => Assert.Equal(13, Convert.FromHexString(frame.GetString()!)[2]));
        }
        Assert.Equal(240, compared);
    }

    [Fact]
    public void Ordinary_families_match_actual_defaults_and_remote_reports_do_not_debit_items()
    {
        using var source = Read("OrdinaryConsumableFamily1458");
        int rows = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            Assert.True(VanillaSelectedConsumableCatalog1458.TryGet(new(I(row, "itemId")), out var item));
            Assert.Equal(I(row, "healLife"), item.HealLife);
            Assert.Equal(I(row, "healMana"), item.HealMana);
            Assert.Equal(B(row, "potion"), item.HealingDelay);
            Assert.Equal(I(row, "useTime"), item.UseTime);
            Assert.Equal(I(row, "useAnimation"), item.UseAnimation);
            Assert.Equal(I(row, "drinkColourCount"), item.DrinkColourCount);
            Assert.Equal(9, I(row, "useStyle"));
            Assert.Equal(0, I(row, "prefix"));
            Assert.Equal(0, I(row, "itemMana"));
            Assert.Equal(0, I(row, "shoot"));
            foreach (var phase in row.GetProperty("phases").EnumerateArray()) Assert.Equal(3, I(phase, "stack"));
            Assert.All(row.GetProperty("frames").EnumerateArray(), frame => Assert.Equal(13, Convert.FromHexString(frame.GetString()!)[2]));
            rows++;
        }
        Assert.Equal(16, rows);
    }

    [Fact]
    public void Original_ItemCheck_entries_and_exits_preserve_vitals_timers_and_full_source_random_state()
    {
        using var source = Read("SelectedConsumablePhases1458");
        int compared = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            var events = row.GetProperty("sourcePhaseEvents").EnumerateArray().ToArray();
            for (int i = 0; i < events.Length; i++)
            {
                var before = events[i];
                if (before.GetProperty("phase").GetString() != "ItemCheck:before") continue;
                var after = events[i + 1];
                Assert.Equal("ItemCheck:after", after.GetProperty("phase").GetString());
                var random = RandomAt(before);
                var facts = new PlayerSelectedConsumableFacts1458(new(I(row, "itemId")), I(before, "life"),
                    I(before, "lifeMax"), I(before, "mana"), I(before, "maximum"), B(before, "control"),
                    B(before, "success"), B(before, "cursed"), B(before, "cced"), B(before, "isAllowed"), false);
                Assert.True(VanillaSelectedConsumable1458.TryStep(SelectedState(before), in facts,
                    random.Next, random.NextDouble, out var actual));
                Assert.Equal(SelectedState(after), actual.State);
                Assert.Equal(I(after, "life"), actual.Life);
                Assert.Equal(I(after, "mana"), actual.Mana);
                AssertRandom(after, random);
                compared++;
            }
        }
        Assert.Equal(1_440, compared);
    }

    [Fact]
    public void Genuine_continuous_mana_method_preserves_delay_counter_nebula_and_stationary_thresholds()
    {
        using var source = Read("RemoteManaRegeneration1458");
        int compared = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            PlayerManaRegenerationState1458? state = null;
            foreach (var phase in row.GetProperty("phases").EnumerateArray())
            {
                var before = phase.GetProperty("before");
                state ??= ManaState(before);
                Assert.Equal(ManaState(before), state);
                var facts = ManaFacts(before);
                Assert.True(VanillaRemoteManaRegeneration1458.TryStep(state, in facts, out var after, out int rate));
                Assert.Equal(ManaState(phase.GetProperty("after")), after);
                Assert.Equal(I(phase.GetProperty("after"), "rate"), rate);
                Assert.Equal(0u, phase.GetProperty("after").GetProperty("cursor").GetUInt32());
                state = after;
                compared++;
            }
        }
        Assert.Equal(1_280, compared);
    }

    [Fact]
    public void Recorded_world_inputs_drive_one_continuous_mana_and_selected_use_model_without_random_offsets()
    {
        using var source = Read("SelectedConsumablePhases1458");
        int compared = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            string mode = row.GetProperty("mode").GetString()!;
            var random = new VanillaUnifiedRandom1458(0);
            var mana = new PlayerManaRegenerationState1458(mode == "near-max" ? 190 : 10, 200, 0, 0, 0);
            var selected = new PlayerSelectedConsumableState1458(0, 0, 0, 0, true, mode == "sickness" ? 60 : 0, 0);
            int life = mode == "near-max" ? 390 : 50;
            int[] buffs = new int[44];
            int[] times = new int[44];
            if (mode is "cursed" or "existing-21" or "existing-94")
            {
                buffs[0] = mode == "cursed" ? 23 : mode == "existing-21" ? 21 : 94;
                times[0] = 60;
            }
            float heat = 0;
            var events = row.GetProperty("sourcePhaseEvents").EnumerateArray().ToArray();
            foreach (var expected in row.GetProperty("phases").EnumerateArray())
            {
                int tick = I(expected, "tick");
                Assert.True(VanillaSelectedConsumable1458.TryStepHeat(heat, random.NextDouble, random.Next, out heat));
                if (mode == "dead")
                {
                    life = 0;
                }
                else
                {
                    selected = selected with { PotionDelay = Math.Max(0, selected.PotionDelay - 1) };
                    int sickness = Array.IndexOf(buffs, 21);
                    if (sickness >= 0) selected = selected with { PotionDelay = times[sickness] };
                    var manaBefore = events.Single(e => I(e, "tick") == tick && e.GetProperty("phase").GetString() == "UpdateManaRegen:before");
                    var manaFacts = ManaFacts(manaBefore);
                    Assert.True(VanillaRemoteManaRegeneration1458.TryStep(mana, in manaFacts, out mana, out int rate));
                    var manaAfter = events.Single(e => I(e, "tick") == tick && e.GetProperty("phase").GetString() == "UpdateManaRegen:after");
                    Assert.Equal(ManaState(manaAfter), mana);
                    Assert.Equal(I(manaAfter, "rate"), rate);
                    bool control = mode != "control-false" && (mode != "release-repeat" || tick < 18 || tick >= 20);
                    var facts = new PlayerSelectedConsumableFacts1458(new(I(row, "itemId")), life, 400,
                        mana.Mana, 200, control, mode != "success-false", mode == "cursed", false, true, false);
                    Assert.True(VanillaSelectedConsumable1458.TryStep(selected, in facts, random.Next, random.NextDouble, out var result));
                    selected = result.State;
                    life = result.Life;
                    mana = mana with { Mana = result.Mana };
                    AddBuff(buffs, times, 21, result.PotionSicknessOffer);
                    AddBuff(buffs, times, 94, result.ManaSicknessOffer);
                }
                Assert.Equal(I(expected, "life"), life);
                Assert.Equal(I(expected, "mana"), mana.Mana);
                Assert.Equal(I(expected, "itemTime"), selected.ItemTime);
                Assert.Equal(I(expected, "itemTimeMax"), selected.ItemTimeMax);
                Assert.Equal(I(expected, "animation"), selected.Animation);
                Assert.Equal(I(expected, "animationMax"), selected.AnimationMax);
                Assert.Equal(B(expected, "release"), selected.ReleaseUseItem);
                Assert.Equal(I(expected, "potionDelay"), selected.PotionDelay);
                Assert.Equal(I(expected, "revolverCritChanceBonus"), selected.RevolverCritBonus);
                Assert.Equal(expected.GetProperty("buffTypes").EnumerateArray().Select(x => x.GetInt32()), buffs);
                Assert.Equal(expected.GetProperty("buffTimes").EnumerateArray().Select(x => x.GetInt32()), times);
                AssertRandom(expected, random, "rngCursor", "rngState");
                compared++;
            }
            Assert.Equal(I(row, "next"), random.Next());
        }
        Assert.Equal(1_600, compared);
    }

    [Fact]
    public void Actual_constructor_and_spawn_preserve_different_provenance_from_a_report_configured_use()
    {
        using var source = Read("ConsumableLifecycle1458");
        int rows = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            var constructor = row.GetProperty("constructor");
            Assert.Equal(VanillaRemoteManaRegeneration1458.ConstructorBaseMaximum, I(constructor, "manaMax"));
            Assert.Equal(VanillaRemoteManaRegeneration1458.ConstructorState, new PlayerManaRegenerationState1458(
                I(constructor, "mana"), I(constructor, "manaMax2"), F(constructor, "delay"), I(constructor, "count"), I(constructor, "nebulaCount")));
            Assert.Equal(VanillaSelectedConsumable1458.ConstructorHeat, F(constructor, "heat"));
            Assert.False(B(constructor, "release"));
            Assert.Equal(VanillaSelectedConsumable1458.ConstructorState, new PlayerSelectedConsumableState1458(
                I(constructor, "time"), I(constructor, "timeMax"), I(constructor, "animation"),
                I(constructor, "animationMax"), B(constructor, "release"), I(constructor, "potionDelay"), I(constructor, "crit")));
            string kind = row.GetProperty("kind").GetString()!;
            if (kind.Contains("spawn", StringComparison.Ordinal))
            {
                foreach (string field in new[] { "release", "animation", "animationMax", "time", "timeMax", "heat", "count", "delay", "crit", "potionDelay" })
                    Assert.Equal(row.GetProperty("before").GetProperty(field).GetRawText(), row.GetProperty("afterBoundary").GetProperty(field).GetRawText());
            }
            if (kind == "constructor-first-use")
                Assert.All(row.GetProperty("phases").EnumerateArray(), phase => Assert.Equal(0, I(phase.GetProperty("state"), "animation")));
            if (kind == "constructor-release-press")
            {
                Assert.Equal(0, I(row.GetProperty("phases")[0].GetProperty("state"), "animation"));
                Assert.True(B(row.GetProperty("phases")[1].GetProperty("state"), "release"));
                Assert.Equal(16, I(row.GetProperty("phases")[2].GetProperty("state"), "animation"));
            }
            rows++;
        }
        Assert.Equal(8, rows);
    }

    [Fact]
    public void Actual_packet42_zero_maximum_has_minimum_regeneration_without_fabricating_constructor_maximum()
    {
        using var source = Read("ConsumableLifecycle1458");
        var row = source.RootElement.EnumerateArray().Single(r => r.GetProperty("kind").GetString() == "packet42-zero");
        Assert.Equal(0, I(row.GetProperty("afterBoundary"), "manaMax"));
        var state = VanillaRemoteManaRegeneration1458.ConstructorState;
        var facts = new PlayerManaRegenerationFacts1458(0, 0, false, false, false, 0, 0, 0);
        foreach (var phase in row.GetProperty("phases").EnumerateArray())
        {
            Assert.True(VanillaRemoteManaRegeneration1458.TryStep(state, in facts, out state, out int rate));
            var expected = phase.GetProperty("state");
            Assert.Equal(I(expected, "mana"), state.Mana);
            Assert.Equal(I(expected, "manaMax2"), state.Maximum);
            Assert.Equal(I(expected, "count"), state.Count);
            Assert.Equal(I(expected, "rate"), rate);
        }
    }

    [Fact]
    public void Unknown_imported_state_and_unsupported_items_refuse_before_random_offers()
    {
        int calls = 0;
        int Integer(int min, int max) { calls++; return min; }
        double Unit() { calls++; return 0; }
        var facts = new PlayerSelectedConsumableFacts1458(new(28), 50, 400, 10, 200, true, true, false, false, true, false);
        Assert.False(VanillaSelectedConsumable1458.TryStep(null, in facts, Integer, Unit, out _));
        var imported = new PlayerSelectedConsumableState1458(0, 0, 0, 0, true, 0, 0);
        var unsupported = facts with { Item = new(227) };
        Assert.False(VanillaSelectedConsumable1458.TryStep(imported, in unsupported, Integer, Unit, out _));
        Assert.False(VanillaSelectedConsumable1458.TryStepHeat(null, Unit, Integer, out _));
        var manaFacts = new PlayerManaRegenerationFacts1458(0, 0, false, false, false, 0, 0, 0);
        Assert.False(VanillaRemoteManaRegeneration1458.TryStep(null, in manaFacts, out _, out _));
        Assert.False(VanillaRemoteManaRegeneration1458.TryStep(new(10, 200, 0, int.MaxValue, 0), in manaFacts, out _, out _));
        Assert.Equal(0, calls);
    }

    private static void AddBuff(int[] buffs, int[] times, int type, int offer)
    {
        if (offer == 0) return;
        int existing = Array.IndexOf(buffs, type);
        int slot = existing >= 0 ? existing : Array.IndexOf(buffs, 0);
        Assert.True(slot >= 0);
        buffs[slot] = type;
        times[slot] = type == 94 && existing >= 0 ? Math.Min(600, times[slot] + offer) : Math.Max(times[slot], offer);
    }

    private static PlayerSelectedConsumableState1458 SelectedState(JsonElement e) => new(I(e, "itemTime"),
        I(e, "itemTimeMax"), I(e, "animation"), I(e, "animationMax"), B(e, "release"), I(e, "potionDelay"), I(e, "revolver"));
    private static PlayerManaRegenerationState1458 ManaState(JsonElement e) => new(I(e, "mana"), I(e, "maximum"),
        F(e, "delay"), I(e, "count"), I(e, "nebulaCount"));
    private static PlayerManaRegenerationFacts1458 ManaFacts(JsonElement e) => new(F(e.GetProperty("velocity"), "X"),
        F(e.GetProperty("velocity"), "Y"), e.TryGetProperty("hook", out var hook) ? hook.GetInt32() >= 0 :
        e.GetProperty("grappling")[0].GetInt32() >= 0, B(e, "buff"), B(e, "arcane"), I(e, "bonus"), F(e, "delayBonus"),
        e.TryGetProperty("nebulaLevel", out var nebula) ? nebula.GetInt32() : I(e, "nebula"));

    private static VanillaUnifiedRandom1458 RandomAt(JsonElement e)
    {
        var result = new VanillaUnifiedRandom1458(0);
        typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(result, e.GetProperty("cursor").GetUInt32());
        var array = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(result)!;
        e.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo(array, 0);
        return result;
    }
    private static void AssertRandom(JsonElement e, VanillaUnifiedRandom1458 random, string cursor = "cursor", string state = "state")
    {
        Assert.Equal(e.GetProperty(cursor).GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458)
            .GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
        Assert.Equal(e.GetProperty(state).EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }
    private static JsonDocument Read(string name)
    {
        using var stream = typeof(SelectedConsumablePhases1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    private static int I(JsonElement e, string name) => e.GetProperty(name).GetInt32();
    private static float F(JsonElement e, string name) => e.GetProperty(name).GetSingle();
    private static bool B(JsonElement e, string name) => e.GetProperty(name).GetBoolean();
}
