using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Core;
using Xunit;

namespace TerraRuntime.Tests;

// Configured original UpdateWorld_Players, separately from UI32 or natural client scheduling.
// ShopOpen is an explicit known component context; packet40/quote is not treated as Main.npcShop.
// Source prices are reference outputs; commerce journal integration owns the eventual paid price.
public sealed class DiscountEquipmentPhase1458Tests
{
    [Fact]
    public void Original_equipment_flags_and_known_shop_latch_match_retained_phases()
    {
        using var source = Read();
        int phases = 0, referenceProfiles = 0;
        foreach (bool windows in new[] { false, true })
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            using var normalized = Normalize(row);
            using var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(normalized.RootElement, windows);
            var spec = row.GetProperty("spec");
            if (spec.GetProperty("slot").GetInt32() == 0)
            {
                var before = f.Member.ItemPhase;
                var random = f.Random.Clone();
                Assert.False(f.Players.TryTickRemotePlayerPhase());
                Assert.Equal(before, f.Member.ItemPhase);
                Assert.True(f.Random.HasSameState(random));
                referenceProfiles++;
                continue;
            }
            f.Member.ItemPhase = f.Member.ItemPhase!.Value with
            {
                DiscountAvailable = spec.GetProperty("startAvailable").GetBoolean(),
                ShopOpen = spec.GetProperty("startShop").GetInt32() > 0
            };
            int type = spec.GetProperty("type").GetInt32();
            int loadout = spec.GetProperty("loadout").GetInt32();
            short slot = checked((short)((loadout < 0 ? VanillaPlayerItemSlotCatalog.ArmorStart :
                VanillaPlayerItemSlotCatalog.LoadoutArmorStart + loadout * VanillaPlayerItemSlotCatalog.LoadoutStride) + spec.GetProperty("slot").GetInt32()));
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                int tick = step.GetProperty("tick").GetInt32();
                if (tick > 0)
                {
                    f.Equipment(slot, tick == 2 ? (short)type : (short)0, tick == 2 ? (short)1 : (short)0,
                        flags: loadout >= 0 && tick == 2 ? (byte)1 : (byte)0);
                    f.Member.ItemPhase = f.Member.ItemPhase!.Value with { ShopOpen = tick != 3 };
                }
                if (tick == 1)
                {
                    string branch = spec.GetProperty("branch").GetString()!;
                    if (branch == "dead")
                    {
                        f.Member.IsDead = true;
                        f.Member.NpcHealth = f.Member.NpcHealth!.Value with { Life = 0 };
                        f.Member.ItemPhase = f.Member.ItemPhase!.Value with { DeadTime = 17, RespawnTimer = 500 };
                    }
                    if (branch == "ghost") f.Member.MovementFlags |= 64;
                    if (branch == "outside") { f.Member.PositionX = 0; f.Member.PositionY = 0; }
                }
                AssertRandom(step.GetProperty("before"), f.Random);
                Assert.True(f.Players.TryTickRemotePlayerPhase());
                var item = f.Member.ItemPhase!.Value;
                var combat = item.DerivedCombat!.Value;
                var expected = step.GetProperty("player");
                Assert.Equal(expected.GetProperty("discountEquipped").GetBoolean(), combat.DiscountEquipped);
                Assert.Equal(expected.GetProperty("discountAvailable").GetBoolean(), item.DiscountAvailable);
                Assert.Equal(expected.GetProperty("goldRing").GetBoolean(), combat.GoldRing);
                Assert.Equal(expected.GetProperty("hasLuckyCoin").GetBoolean(), combat.HasLuckyCoin);
                Assert.Equal(expected.GetProperty("hasLuck_LuckyCoin").GetBoolean(), combat.HasLuckLuckyCoin);
                AssertRandom(step.GetProperty("after"), f.Random);
                phases++;
            }
        }
        Assert.Equal(240, phases);
        Assert.Equal(4, referenceProfiles);
    }

    [Fact]
    public void Unknown_equipment_and_late_shop_context_cannot_partially_advance_the_shared_phase()
    {
        using var source = Read();
        var row = source.RootElement.GetProperty("rows")[0];
        AssertLatentShopLifecycle(row);
        using var normalized = Normalize(row, mixed: true);
        using (var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(normalized.RootElement, false))
        {
            // One unsupported actor retires the whole shared cursor rather than committing
            // only the actor whose equipment is represented.
            f.State.Apply(new PlayerEquipmentRuntimeCommand(f.SecondConnection,
                new(f.SecondConnection.Player.Slot, 62, 1, 0, 1321, 0)));
            var first = f.Member.ItemPhase; var second = f.SecondMember.ItemPhase; var random = f.Random.Clone();
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(first, f.Member.ItemPhase); Assert.Equal(second, f.SecondMember.ItemPhase);
            Assert.True(f.Random.HasSameState(random));
            f.State.Tick();
            Assert.Null(f.Member.ItemPhase); Assert.Null(f.SecondMember.ItemPhase);
            Assert.True(f.Random.HasSameState(random));
        }
        foreach (bool replaceSession in new[] { false, true })
        {
            using var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(normalized.RootElement, false);
            Assert.True(f.Players.TrySetTalkNpc(f.Connection, 5));
            var session = new RuntimeTownShopSession1458(5, new(17), RuntimeTownShopSessionKind1458.TalkOnly,
                1f, false, default, [], RuntimeTownCommerceMissingFacts1458.None);
            Assert.True(f.Players.TrySetTownShopSession(f.Connection, session));
            var before = f.Member.ItemPhase; var second = f.SecondMember.ItemPhase; var random = f.Random.Clone();
            bool once = false;
            f.Players.SetNpcHealthWorldFacts(() =>
            {
                if (!once)
                {
                    once = true;
                    if (replaceSession) Assert.True(f.Players.TrySetTownShopSession(f.Connection, session with { PriceAdjustment = 0.9f }));
                    else f.SecondMember.ItemPhase = second!.Value with { ShopOpen = true };
                }
                return new(false, false);
            });
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.True(once); Assert.Equal(before, f.Member.ItemPhase);
            Assert.Equal(replaceSession ? second : second!.Value with { ShopOpen = true }, f.SecondMember.ItemPhase);
            Assert.True(f.Random.HasSameState(random));
        }
    }

    private static void AssertLatentShopLifecycle(JsonElement row)
    {
        var noDiscountNow = RuntimePlayerItemPhase1458.Constructor;
        var noPaidClose = noDiscountNow.CloseShopUiUnknown(3);
        Assert.Equal((byte)15, noPaidClose.CapturedShopDiscountStates);
        Assert.Equal((byte)3, noPaidClose.DiscountAvailableMask);
        Assert.Equal((byte)13, noPaidClose.AdvanceShopDiscount(false).CapturedShopDiscountStates);
        Assert.Throws<ArgumentOutOfRangeException>(() => noDiscountNow.CloseShopUiUnknown(4));
        RuntimePlayerItemPhase1458 latePaid = default;
        var paymentFailure = Record.Exception(() =>
            latePaid = noDiscountNow.ApplyAcceptedShopDiscount(2, retired: true));
        Assert.Null(paymentFailure);
        RuntimePlayerItemPhase1458 paidOpen = default;
        var openFailure = Record.Exception(() =>
            paidOpen = noDiscountNow.ApplyAcceptedShopDiscount(2));
        Assert.Null(openFailure);
        Assert.Equal((byte)8, paidOpen.CapturedShopDiscountStates);
        Assert.True(paidOpen.ShopOpen);
        Assert.Equal((byte)2, paidOpen.DiscountAvailableMask);
        Assert.Equal((byte)10, latePaid.CapturedShopDiscountStates);
        Assert.False(latePaid.ShopOpen);
        Assert.Equal((byte)2, latePaid.DiscountAvailableMask);
        Assert.Equal((byte)9, latePaid.AdvanceShopDiscount(false).CapturedShopDiscountStates);
        Assert.Throws<ArgumentOutOfRangeException>(() => noDiscountNow.ApplyAcceptedShopDiscount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => noDiscountNow.ApplyAcceptedShopDiscount(4));
        using var normalized = Normalize(row);
        foreach (string branch in new[] { "living", "dead", "ghost", "outside" })
        {
            using var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(normalized.RootElement, false);
            f.Member.ItemPhase = f.Member.ItemPhase!.Value with { ShopOpen = true, DiscountAvailable = true };
            Assert.True(f.Players.TryTickRemotePlayerPhase());
            f.Equipment(62, 0, 0);
            var closed = f.Member.ItemPhase!.Value.CloseShopUiUnknown();
            Assert.Equal((byte)10, closed.CapturedShopDiscountStates);
            f.Member.ItemPhase = closed;
            if (branch == "dead")
            {
                f.Member.IsDead = true;
                f.Member.NpcHealth = f.Member.NpcHealth!.Value with { Life = 0 };
                f.Member.ItemPhase = closed with { DeadTime = 17, RespawnTimer = 500 };
            }
            if (branch == "ghost") f.Member.MovementFlags |= 64;
            if (branch == "outside") { f.Member.PositionX = 0; f.Member.PositionY = 0; }
            Assert.True(f.Players.TryTickRemotePlayerPhase());
            var after = f.Member.ItemPhase!.Value;
            // Actual no-draw close follows the retained open/true branch. The separately
            // source-read draw close permits closed/false on the next living phase.
            Assert.Equal(branch == "living" ? (byte)9 : (byte)10, after.CapturedShopDiscountStates);
            Assert.Equal(branch == "living" ? (byte)3 : (byte)2, after.DiscountAvailableMask);
            if (branch == "ghost") Assert.False(after.DerivedCombat!.Value.DiscountEquipped);
            var accepted = after.ApplyAcceptedShopDiscount(2);
            Assert.Equal((byte)8, accepted.CapturedShopDiscountStates);
            Assert.True(accepted.ShopOpen);
            var retired = after.ApplyAcceptedShopDiscount(2, retired: true);
            Assert.Equal((byte)10, retired.CapturedShopDiscountStates);
            Assert.False(retired.ShopOpen);
            Assert.Equal((byte)2, retired.DiscountAvailableMask);
            if (branch == "living")
            {
                f.Member.ItemPhase = retired;
                Assert.True(f.Players.TryTickRemotePlayerPhase());
                Assert.Equal((byte)9, f.Member.ItemPhase!.Value.CapturedShopDiscountStates);
                Assert.Equal((byte)3, f.Member.ItemPhase.Value.DiscountAvailableMask);
            }
        }
    }

    private static JsonDocument Normalize(JsonElement row, bool mixed = false)
    {
        var input = row.GetProperty("spec");
        var spec = new
        {
            name = input.GetProperty("name").GetString(), branch = input.GetProperty("branch").GetString(),
            mode = input.GetProperty("mode").GetInt32(), good = false,
            heart = input.GetProperty("heart").GetBoolean(), weapon = 0, prefix = 0,
            currentMana = 20, baseMana = 200, count = 0, delay = 0f, mixed,
            armor = new[] { new { loadout = input.GetProperty("loadout").GetInt32(), slot = input.GetProperty("slot").GetInt32(),
                type = input.GetProperty("type").GetInt32(), prefix = 0, favorite = input.GetProperty("loadout").GetInt32() >= 0 } }
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(new { spec, initial = new
        { lastItemUseAttemptSuccess = false, buffType = new int[44], buffTime = new int[44], manaHeat = 0f } }));
    }

    private static JsonDocument Read()
    {
        using var stream = typeof(DiscountEquipmentPhase1458Tests).Assembly.GetManifestResourceStream("DiscountEquipmentPhaseSource1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
        Assert.Equal(expected.GetProperty("publicNext").GetInt32(), random.Clone().Next());
    }
}
