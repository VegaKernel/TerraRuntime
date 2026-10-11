using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class SelectedPermanentConsumable1458Tests
{
    public static long Checks { get; private set; }

    [Fact]
    public void Original_local_effects_and_remote_clocks_match_all_permanent_consumable_phases()
    {
        int rows = 0;
        int phases = 0;
        foreach (string name in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using var stream = typeof(SelectedPermanentConsumable1458Tests).Assembly
                .GetManifestResourceStream("LifeProgression" + name + "1458");
            Assert.NotNull(stream);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            foreach (JsonElement row in source.RootElement.EnumerateArray())
            {
                rows++;
                JsonElement before = row.GetProperty("before");
                var clocks = Clocks(before);
                int baseLife = I(before, "statLifeMax");
                int derivedLife = I(before, "statLifeMax2");
                int baseMana = I(before, "statManaMax");
                int derivedMana = I(before, "statManaMax2");
                int life = I(before, "statLife");
                int mana = I(before, "statMana");
                int stack = I(before.GetProperty("inventory")[0], "stack");
                bool local = row.GetProperty("local").GetBoolean();
                var random = new VanillaUnifiedRandom1458(I(row, "seed"));
                SetRandom(random, row.GetProperty("beforeRandom"));
                foreach (JsonElement phase in row.GetProperty("phases").EnumerateArray())
                {
                    phases++;
                    var facts = new PlayerSelectedConsumableFacts1458(new(I(row, "itemId")), life,
                        derivedLife, mana, derivedMana, B(before, "controlUseItem"),
                        B(before, "lastItemUseAttemptSuccess"), B(before, "cursed"), false, true, false);
                    Assert.True(VanillaSelectedConsumable1458.TryStepPermanentConsumable(clocks, in facts,
                        baseLife, baseMana, stack, local, B(before, "noItems"), random.Next, random.NextDouble, out var actual));
                    JsonElement expected = phase.GetProperty("state");
                    Equal(Clocks(expected), actual.Use.State);
                    Equal(I(expected, "statLife"), actual.Use.Life);
                    Equal(I(expected, "statMana"), actual.Use.Mana);
                    Equal(I(expected, "statLifeMax"), actual.BaseLifeMaximum);
                    Equal(I(expected, "statLifeMax2"), actual.DerivedLifeMaximum);
                    Equal(I(expected, "statManaMax"), actual.BaseManaMaximum);
                    Equal(I(expected, "statManaMax2"), actual.DerivedManaMaximum);
                    int expectedStack = I(expected.GetProperty("inventory")[0], "stack");
                    Equal(expectedStack < stack, actual.Use.Consumed);
                    Equal(expectedStack, actual.Use.RemainingStack);
                    Equal(I(expected, "statLifeMax") != baseLife || I(expected, "statManaMax") != baseMana,
                        actual.Applied);
                    Equal((short)0, actual.Use.BuffOfferType.Value);
                    Equal(0, actual.Use.BuffDurationOffer);
                    Equal(0, actual.Use.PotionSicknessOffer);
                    Equal(0, actual.Use.ManaSicknessOffer);
                    Equal(phase.GetProperty("random").GetProperty("cursor").GetUInt32(), Access.Cursor(random));
                    Equal(phase.GetProperty("random").GetProperty("state").EnumerateArray()
                        .Select(x => x.GetInt32()).ToArray(), Access.Seed(random));
                    Equal(I(phase, "next"), random.Clone().Next());
                    JsonElement inventory = expected.GetProperty("inventory");
                    for (int slot = 0; slot < 59; slot++)
                    {
                        JsonElement initialItem = before.GetProperty("inventory")[slot];
                        Equal(I(initialItem, "type"), I(inventory[slot], "type"));
                        Equal(I(initialItem, "prefix"), I(inventory[slot], "prefix"));
                        Equal(B(initialItem, "favorite"), B(inventory[slot], "favorite"));
                        Equal(slot == 0 ? actual.Use.RemainingStack : I(initialItem, "stack"),
                            I(inventory[slot], "stack"));
                    }
                    Equal(before.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                        expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()).ToArray());
                    Equal(before.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                        expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray());
                    clocks = actual.Use.State;
                    baseLife = actual.BaseLifeMaximum;
                    derivedLife = actual.DerivedLifeMaximum;
                    baseMana = actual.BaseManaMaximum;
                    derivedMana = actual.DerivedManaMaximum;
                    life = actual.Use.Life;
                    mana = actual.Use.Mana;
                    stack = actual.Use.RemainingStack;
                }
                // Synchronization is a separate source boundary: its literal packet5 must match the
                // captured source afterSync image; the detached Game method does not emit packets.
                foreach (JsonElement encoded in row.GetProperty("syncFrames").EnumerateArray())
                {
                    byte[] frame = Convert.FromHexString(encoded.GetString()!);
                    if (frame[2] != 5) continue;
                    int slot = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(4));
                    JsonElement item = row.GetProperty("afterSync").GetProperty("inventory")[slot];
                    Equal(I(item, "stack"), System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(6)));
                    Equal(I(item, "prefix"), frame[8]);
                    Equal(I(item, "type"), System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(9)));
                }
            }
        }
        Equal(408, rows);
        Equal(8160, phases);
    }

    private static void Equal<T>(T expected, T actual)
    {
        Checks++;
        Assert.Equal(expected, actual);
    }

    private static int I(JsonElement item, string property) => item.GetProperty(property).GetInt32();
    private static bool B(JsonElement item, string property) => item.GetProperty(property).GetBoolean();
    private static PlayerSelectedConsumableState1458 Clocks(JsonElement state) => new(
        I(state, "itemTime"), I(state, "itemTimeMax"), I(state, "itemAnimation"),
        I(state, "itemAnimationMax"), B(state, "releaseUseItem"), I(state, "potionDelay"),
        I(state, "revolverCritChanceBonus"));

    private static void SetRandom(VanillaUnifiedRandom1458 random, JsonElement before)
    {
        Access.Seed(random) = before.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Access.Cursor(random) = before.GetProperty("cursor").GetUInt32();
    }

    private static class Access
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
        internal static extern ref int[] Seed(VanillaUnifiedRandom1458 random);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
        internal static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
    }
}
