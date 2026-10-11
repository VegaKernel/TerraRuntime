using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using Xunit;
namespace TerraRuntime.Tests;
public sealed class SelectedPotionLocal1458Tests
{
    [Fact]
    public void Local_and_remote_steps_match_original_potion_effects_consumption_clocks_buffs_and_random()
    {
        int rows = 0;
        foreach (string resource in new[] { "SelectedPotionUseLinuxFirst1458", "SelectedPotionUseLinuxRepeat1458", "SelectedPotionUseWindowsFirst1458", "SelectedPotionUseWindowsRepeat1458" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            foreach (var row in source.RootElement.EnumerateArray())
            {
                var before = row.GetProperty("before");
                var state = State(before);
                int stack = Stack(before);
                int life = I(before, "statLife"), mana = I(before, "statMana");
                var inventory = before.GetProperty("inventory").EnumerateArray().Select(Tuple).ToArray();
                var random = new VanillaUnifiedRandom1458(I(row, "seed"));
                var buffs = new PlayerBuffState();
                bool local = row.GetProperty("local").GetBoolean();
                foreach (var phase in row.GetProperty("phases").EnumerateArray())
                {
                    var expected = phase.GetProperty("state");
                    // Raw cursed alone is not the original direct ItemCheck prohibition.
                    // The corrected original corpus directly captures noItems independently of raw cursed.
                    var facts = new PlayerSelectedConsumableFacts1458(new(I(row, "itemId")), life,
                        I(before, "statLifeMax2"), mana, I(before, "statManaMax2"),
                        before.GetProperty("controlUseItem").GetBoolean(), before.GetProperty("lastItemUseAttemptSuccess").GetBoolean(), before.GetProperty("noItems").GetBoolean(), false, true, false);
                    PlayerSelectedConsumableTransition1458 actual;
                    if (local)
                        Assert.True(VanillaSelectedConsumable1458.TryStepOwned(state, in facts, stack,
                            random.Next, random.NextDouble, out actual));
                    else
                        Assert.True(VanillaSelectedConsumable1458.TryStep(state, in facts,
                            random.Next, random.NextDouble, out actual));
                    int expectedStack = Stack(expected);
                    inventory[0] = inventory[0] with { Stack = local ? actual.RemainingStack : stack };
                    if (local && inventory[0].Stack == 0 && actual.State.Animation == 0) inventory[0] = default;
                    Assert.Equal(expected.GetProperty("inventory").EnumerateArray().Select(Tuple), inventory);
                    Assert.Equal(local && stack > expectedStack, actual.Consumed);
                    Assert.Equal(expectedStack, local ? actual.RemainingStack : stack);
                    Assert.Equal(State(expected), actual.State);
                    Assert.Equal(I(expected, "statLife"), actual.Life);
                    Assert.Equal(I(expected, "statMana"), actual.Mana);
                    if (actual.PotionSicknessOffer > 0)
                        Assert.True(buffs.TryApplySelectedConsumable(new(21), actual.PotionSicknessOffer, false));
                    if (actual.ManaSicknessOffer > 0)
                        Assert.True(buffs.TryApplySelectedConsumable(new(94), actual.ManaSicknessOffer, false));
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    Assert.Equal(expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()),
                        ((BuffTypeId[])typeof(PlayerBuffState).GetField("types", flags)!.GetValue(buffs)!).Select(x => x.Value));
                    Assert.Equal(expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()),
                        (int[])typeof(PlayerBuffState).GetField("durations", flags)!.GetValue(buffs)!);
                    Assert.Equal(phase.GetProperty("random").GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
                        (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
                    Assert.Equal(phase.GetProperty("random").GetProperty("cursor").GetUInt32(),
                        (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
                    Assert.Equal(I(phase, "next"), random.Clone().Next());
                    state = actual.State; life = actual.Life; mana = actual.Mana; stack = expectedStack;
                }
                Assert.Equal(row.GetProperty("beforeSync").GetProperty("inventory").EnumerateArray().Select(Tuple), inventory);
                Assert.Equal(row.GetProperty("afterSync").GetProperty("inventory").EnumerateArray().Select(Tuple), inventory);
                rows++;
            }
        }
        Assert.Equal(576, rows);
        VerifyEarlySyncAirTails();
    }
    private static void VerifyEarlySyncAirTails()
    {
        int rows = 0;
        foreach (string resource in new[] { "EarlySyncLinuxFirst1458", "EarlySyncLinuxRepeat1458", "EarlySyncWindowsFirst1458", "EarlySyncWindowsRepeat1458" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            foreach (var row in source.RootElement.EnumerateArray())
            {
                var before = row.GetProperty("before");
                var state = State(before);
                var random = new VanillaUnifiedRandom1458(I(row, "seed"));
                int life = I(before,"statLife"), mana = I(before,"statMana");
                var inventory = before.GetProperty("inventory").EnumerateArray().Select(Tuple).ToArray();
                var buffs = new PlayerBuffState();
                foreach (var phase in row.GetProperty("phases").EnumerateArray())
                {
                    bool first = I(phase,"tick") == 0;
                    var facts = new PlayerSelectedConsumableFacts1458(first ? new(I(row,"itemId")) : VanillaItemIds.None,
                        life,400,mana,200,true,true,false,false,true,false);
                    PlayerSelectedConsumableTransition1458 actual;
                    if (first)
                    {
                        Assert.True(VanillaSelectedConsumable1458.TryStepOwned(state,in facts,1,
                            random.Next,random.NextDouble,out actual));
                        Assert.True(actual.Consumed);
                        Assert.Equal(0,actual.RemainingStack);
                        Assert.Equal(State(row.GetProperty("firstBeforeSync").GetProperty("state")),actual.State);
                    }
                    else
                    {
                        Assert.True(VanillaSelectedConsumable1458.TryStep(state,in facts,
                            random.Next,random.NextDouble,out actual));
                        Assert.False(actual.Consumed);
                    }
                    // Actual source Sync immediately canonicalizes the consumed zero stack to Air.
                    inventory[0] = default;
                    var expected = phase.GetProperty("state");
                    Assert.Equal(expected.GetProperty("inventory").EnumerateArray().Select(Tuple),inventory);
                    Assert.Equal(State(expected),actual.State);
                    Assert.Equal(I(expected,"statLife"),actual.Life);
                    Assert.Equal(I(expected,"statMana"),actual.Mana);
                    if(actual.PotionSicknessOffer>0) Assert.True(buffs.TryApplySelectedConsumable(new(21),actual.PotionSicknessOffer,false));
                    if(actual.ManaSicknessOffer>0) Assert.True(buffs.TryApplySelectedConsumable(new(94),actual.ManaSicknessOffer,false));
                    const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    Assert.Equal(expected.GetProperty("buffType").EnumerateArray().Select(x=>x.GetInt32()),((BuffTypeId[])typeof(PlayerBuffState).GetField("types",flags)!.GetValue(buffs)!).Select(x=>x.Value));
                    Assert.Equal(expected.GetProperty("buffTime").EnumerateArray().Select(x=>x.GetInt32()),(int[])typeof(PlayerBuffState).GetField("durations",flags)!.GetValue(buffs)!);
                    Assert.Equal(phase.GetProperty("random").GetProperty("state").EnumerateArray().Select(x=>x.GetInt32()),(int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",flags)!.GetValue(random)!);
                    Assert.Equal(phase.GetProperty("random").GetProperty("cursor").GetUInt32(),(uint)typeof(VanillaUnifiedRandom1458).GetField("inext",flags)!.GetValue(random)!);
                    Assert.Equal(I(phase,"next"),random.Clone().Next());
                    state=actual.State;life=actual.Life;mana=actual.Mana;
                }
                rows++;
            }
        }
        Assert.Equal(32,rows);
    }

    [Fact]
    public void Owned_potion_boundary_refuses_nonpotions_and_bad_stacks_before_random_draws()
    {
        var random = new VanillaUnifiedRandom1458(12345);
        var before = random.Clone();
        var facts = new PlayerSelectedConsumableFacts1458(new(28), 50, 400, 10, 200,
            true, true, false, false, true, false);
        foreach (int stack in new[] { -1, 10000, int.MaxValue })
        {
            Assert.False(VanillaSelectedConsumable1458.TryStepOwned(
                VanillaSelectedConsumable1458.ConstructorState, in facts, stack,
                random.Next, random.NextDouble, out _));
            Assert.True(random.HasSameState(before));
        }
        var crystal = facts with { Item = new(109) };
        Assert.False(VanillaSelectedConsumable1458.TryStepOwned(
            VanillaSelectedConsumable1458.ConstructorState, in crystal, 1,
            random.Next, random.NextDouble, out _));
        Assert.True(random.HasSameState(before));
    }
    private readonly record struct ItemTuple(int Type, int Stack, int Prefix, bool Favorite);
    private static ItemTuple Tuple(JsonElement e) => new(I(e,"type"), I(e,"stack"), I(e,"prefix"), e.GetProperty("favorite").GetBoolean());
    private static int I(JsonElement e, string n) => e.GetProperty(n).GetInt32();
    private static int Stack(JsonElement e) => I(e.GetProperty("inventory")[0], "stack");
    private static PlayerSelectedConsumableState1458 State(JsonElement e) => new(I(e,"itemTime"),
        I(e,"itemTimeMax"), I(e,"itemAnimation"), I(e,"itemAnimationMax"),
        e.GetProperty("releaseUseItem").GetBoolean(), I(e,"potionDelay"), I(e,"revolverCritChanceBonus"));
}
