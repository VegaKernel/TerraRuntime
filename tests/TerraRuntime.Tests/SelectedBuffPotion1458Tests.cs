using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class SelectedBuffPotion1458Tests
{
    [Fact]
    public void Original_buff_potion_local_and_remote_clocks_consumption_and_random_match()
    {
        int rows = 0;
        foreach (string resource in new[] { "SelectedBuffPotionLinuxFirst1458", "SelectedBuffPotionLinuxRepeat1458", "SelectedBuffPotionWindowsFirst1458", "SelectedBuffPotionWindowsRepeat1458" })
        {
            using Stream compressed = typeof(SelectedBuffPotion1458Tests).Assembly.GetManifestResourceStream(resource)!;
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using JsonDocument document = JsonDocument.Parse(gzip);
            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                JsonElement before = row.GetProperty("before");
                var state = State(before);
                bool local = row.GetProperty("local").GetBoolean();
                int stack = Stack(before);
                int currentType = I(row, "itemId");
                var random = new VanillaUnifiedRandom1458(I(row, "seed"));
                JsonElement metadata = row.GetProperty("metadata");
                foreach (JsonElement phase in row.GetProperty("phases").EnumerateArray())
                {
                    JsonElement expected = phase.GetProperty("state");
                    var facts = new PlayerSelectedConsumableFacts1458(new(currentType), I(before, "statLife"),
                        I(before, "statLifeMax2"), I(before, "statMana"), I(before, "statManaMax2"),
                        before.GetProperty("controlUseItem").GetBoolean(), before.GetProperty("lastItemUseAttemptSuccess").GetBoolean(),
                        row.GetProperty("mode").GetString() == "no-items", false, true, false);
                    PlayerSelectedConsumableTransition1458 actual;
                    if (local && currentType != 0)
                        Assert.True(VanillaSelectedConsumable1458.TryStepOwned(state, in facts, stack, random.Next, random.NextDouble, out actual));
                    else
                        Assert.True(VanillaSelectedConsumable1458.TryStep(state, in facts, random.Next, random.NextDouble, out actual));
                    int expectedStack = Stack(expected);
                    bool consumed = local && stack > expectedStack;
                    Assert.Equal(consumed, actual.Consumed);
                    Assert.Equal(expectedStack, local && currentType != 0 ? actual.RemainingStack : stack);
                    Assert.Equal(State(expected), actual.State);
                    Assert.Equal(I(expected, "statLife"), actual.Life);
                    Assert.Equal(I(expected, "statMana"), actual.Mana);
                    // Original AddBuff is offered before the independent immunity/indexed-slot owner.
                    // Immune and longer-existing cases still consume and offer this source duration.
                    Assert.Equal(consumed ? I(metadata, "buffType") : 0, actual.BuffOfferType.Value);
                    Assert.Equal(consumed ? I(metadata, "buffTime") : 0, actual.BuffDurationOffer);
                    Assert.Equal(0, actual.PotionSicknessOffer);
                    Assert.Equal(0, actual.ManaSicknessOffer);
                    Assert.Equal(phase.GetProperty("random").GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray(), Seeds(random));
                    Assert.Equal(phase.GetProperty("random").GetProperty("cursor").GetUInt32(), Cursor(random));
                    Assert.Equal(I(phase, "next"), random.Clone().Next());
                    JsonElement inventory = expected.GetProperty("inventory");
                    Assert.Equal(59, inventory.GetArrayLength());
                    for (int slot = 1; slot < 59; slot++)
                        Assert.Equal(before.GetProperty("inventory")[slot].GetRawText(), inventory[slot].GetRawText());
                    state = actual.State;
                    stack = expectedStack;
                    currentType = I(inventory[0], "type");
                }
                rows++;
            }
        }
        Assert.Equal(480, rows);
    }

    private static int I(JsonElement e, string name) => e.GetProperty(name).GetInt32();
    private static int Stack(JsonElement e) => I(e.GetProperty("inventory")[0], "stack");
    private static PlayerSelectedConsumableState1458 State(JsonElement e) => new(I(e, "itemTime"),
        I(e, "itemTimeMax"), I(e, "itemAnimation"), I(e, "itemAnimationMax"),
        e.GetProperty("releaseUseItem").GetBoolean(), I(e, "potionDelay"), I(e, "revolverCritChanceBonus"));
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] Seeds(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
}
