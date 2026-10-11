using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class SelectedPermanentUnlock1458Tests
{
    public static long Checks { get; private set; }

    [Fact]
    public void Original_eight_personal_flags_and_clocks_match_local_and_remote_ItemCheck()
    {
        int rows = 0;
        int phases = 0;
        foreach (string name in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using var stream = typeof(SelectedPermanentUnlock1458Tests).Assembly
                .GetManifestResourceStream("PermanentUnlock" + name + "1458");
            Assert.NotNull(stream);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            foreach (JsonElement row in source.RootElement.EnumerateArray())
            {
                rows++;
                JsonElement before = row.GetProperty("before");
                var clocks = Clocks(before);
                var unlocks = Unlocks(before);
                var selectedItem = new ItemTypeId(I(row,"itemId"));
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
                    var facts = new PlayerSelectedConsumableFacts1458(selectedItem, life,
                        derivedLife, mana, derivedMana, B(before, "controlUseItem"),
                        B(before, "lastItemUseAttemptSuccess"), B(before, "cursed"), false, true, false);
                    Assert.True(VanillaSelectedConsumable1458.TryStepPermanentUnlock(clocks, in facts,
                        in unlocks, stack, local, B(before, "noItems"), B(row, "expert"), random.Next, random.NextDouble, out var actual));
                    JsonElement expected = phase.GetProperty("state");
                    Equal(Clocks(expected), actual.Use.State);
                    Equal(I(expected, "statLife"), actual.Use.Life);
                    Equal(I(expected, "statMana"), actual.Use.Mana);
                    Equal(I(expected, "statLifeMax"), baseLife);
                    Equal(I(expected, "statLifeMax2"), derivedLife);
                    Equal(I(expected, "statManaMax"), baseMana);
                    Equal(I(expected, "statManaMax2"), derivedMana);
                    int expectedStack = I(expected.GetProperty("inventory")[0], "stack");
                    Equal(expectedStack < stack, actual.Use.Consumed);
                    Equal(expectedStack, actual.Use.RemainingStack);
                    Equal(Unlocks(expected), actual.Unlocks);
                    Equal(Unlocks(expected) != unlocks, actual.Applied);
                    unlocks = actual.Unlocks;
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
                        Equal(slot == 0 ? actual.SelectedItem.Value : I(initialItem, "type"), I(inventory[slot], "type"));
                        Equal(I(initialItem, "prefix"), I(inventory[slot], "prefix"));
                        Equal(B(initialItem, "favorite"), B(inventory[slot], "favorite"));
                        Equal(slot == 0 ? actual.Use.RemainingStack : I(initialItem, "stack"),
                            I(inventory[slot], "stack"));
                    }
                    Equal(before.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                        expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()).ToArray());
                    Equal(before.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                        expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray());
                    selectedItem = actual.SelectedItem;
                    clocks = actual.Use.State;
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
        Equal(792, rows);
        Equal(15840, phases);
    }

    [Fact]
    public void Original_next_boundary_projects_permanent_boosters_and_existing_regeneration_owners()
    {
        int methods = 0;
        foreach (string name in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using var stream = typeof(SelectedPermanentUnlock1458Tests).Assembly
                .GetManifestResourceStream("PermanentUnlock" + name + "1458");
            Assert.NotNull(stream);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            foreach (JsonElement row in source.RootElement.EnumerateArray())
            {
                JsonElement boundary = row.GetProperty("nextBoundary");
                if (boundary.ValueKind == JsonValueKind.Null) continue;
                foreach (JsonElement step in boundary.GetProperty("steps").EnumerateArray())
                {
                    methods++;
                    JsonElement before = step.GetProperty("before");
                    JsonElement after = step.GetProperty("after");
                    var flags = Unlocks(before);
                    switch (step.GetProperty("method").GetString())
                    {
                        case "ResetEffects":
                            // Reset is an explicit previous owner checkpoint, not modeled by the use kernel.
                            // These source fields are its inputs to the existing consumers below.
                            Equal(0, I(after,"statDefense"));
                            Equal(0, I(after,"fishingSkill"));
                            Equal(BitConverter.SingleToInt32Bits(1f), Bits(after,"pickSpeed"));
                            break;
                        case "UpdatePermanentBoosters":
                            var boosts = new PlayerPermanentBoosterState1458(I(before,"statDefense"),
                                I(before,"fishingSkill"), F(before,"pickSpeed"), F(before,"tileSpeed"), F(before,"wallSpeed"));
                            Assert.True(VanillaSelectedConsumable1458.TryApplyPermanentBoosters(in boosts,in flags,out var actual));
                            Equal(I(after,"statDefense"), actual.Defense);
                            Equal(I(after,"fishingSkill"), actual.FishingSkill);
                            Equal(Bits(after,"pickSpeed"), BitConverter.SingleToInt32Bits(actual.PickSpeed));
                            Equal(Bits(after,"tileSpeed"), BitConverter.SingleToInt32Bits(actual.TileSpeed));
                            Equal(Bits(after,"wallSpeed"), BitConverter.SingleToInt32Bits(actual.WallSpeed));
                            break;
                        case "UpdateLifeRegen":
                            var health = new PlayerNpcHealthState1458(I(before,"statLife"), I(before,"lifeRegenCount"), F(before,"lifeRegenTime"));
                            var nextHealth = VanillaRemotePlayerHealth1458.Step(health,I(before,"statLifeMax2"),
                                false,false,false,true,B(row,"expert"),false,0,false,false,false,flags.AegisCrystal);
                            Assert.NotNull(nextHealth);
                            Equal(I(after,"statLife"),nextHealth.Value.Life);
                            Equal(I(after,"lifeRegenCount"),nextHealth.Value.RegenCount);
                            Equal(Bits(after,"lifeRegenTime"),BitConverter.SingleToInt32Bits(nextHealth.Value.RegenTime!.Value));
                            break;
                        case "UpdateManaRegen":
                            var mana = new PlayerManaRegenerationState1458(I(before,"statMana"),I(before,"statManaMax2"),
                                F(before,"manaRegenDelay"),I(before,"manaRegenCount"),0);
                            var facts = new PlayerManaRegenerationFacts1458(0,0,false,false,flags.ArcaneCrystal,0,0,0);
                            Assert.True(VanillaRemoteManaRegeneration1458.TryStep(mana,in facts,out var nextMana,out int rate));
                            Equal(I(after,"statMana"),nextMana.Mana);
                            Equal(I(after,"manaRegenCount"),nextMana.Count);
                            Equal(I(after,"manaRegen"),rate);
                            Equal(Bits(after,"manaRegenDelay"),BitConverter.SingleToInt32Bits(nextMana.Delay));
                            break;
                        case "RecalculateLuck":
                            var luck = new VanillaPlayerLuckComponents1458(0,0,0,false,false,F(before,"equipmentBasedLuckBonus"),0,0);
                            Equal(Bits(after,"luck"),BitConverter.SingleToInt32Bits(
                                VanillaPlayerLuckFacts1458.Recalculate(in luck,flags.GalaxyPearl,false,false)));
                            break;
                        default: throw new InvalidOperationException("Unknown source consumer");
                    }
                    Equal(step.GetProperty("beforeRandom").GetRawText(),step.GetProperty("afterRandom").GetRawText());
                    Equal(I(step,"beforeNext"),I(step,"afterNext"));
                }
            }
        }
        Equal(180,methods);
    }

    private static float F(JsonElement state,string field) => state.GetProperty(field).GetSingle();
    private static int Bits(JsonElement state,string field) => state.GetProperty("singleBits").GetProperty(field).GetInt32();

    [Fact]
    public void Original_paid_flags_reach_heart_slots_artisan_station_and_arcane_delay()
    {
        int rows = 0;
        foreach (string name in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using var stream = typeof(SelectedPermanentUnlock1458Tests).Assembly.GetManifestResourceStream("PermanentConsumers" + name + "1458");
            Assert.NotNull(stream);
            using var gzip = new GZipStream(stream,CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            foreach (JsonElement row in source.RootElement.EnumerateArray())
            {
                rows++;
                JsonElement consumer = row.GetProperty("consumer");
                JsonElement before = consumer.GetProperty("before"), after = consumer.GetProperty("after");
                var unlocks = Unlocks(before);
                switch (row.GetProperty("family").GetString())
                {
                    case "Heart":
                        int mode = I(row,"context");
                        var context = new VanillaPlayerCombatEquipmentContext(unlocks.DemonHeart,mode is 1 or 2,mode == 2)
                        { GameMenu = mode == 3 };
                        foreach (JsonElement slot in consumer.GetProperty("slots").EnumerateArray())
                        {
                            Assert.True(context.TryIsSlotUsable(I(slot,"slot"),out bool usable));
                            Equal(B(slot,"usable"),usable);
                        }
                        break;
                    case "Artisan":
                        // Exact configured source player geometry and Simple ranges retained in the pinned capture program.
                        Assert.True(VanillaSelectedConsumable1458.TryGetPermanentCraftingRegion(400,400,20,42,5,3,
                            in unlocks,out var region));
                        JsonElement expected = consumer.GetProperty(unlocks.ArtisanLoaf ? "expandedRegion" : "plainRegion");
                        Equal(I(expected,"Left"),region.Left);
                        Equal(I(expected,"Right"),region.Right);
                        Equal(I(expected,"Top"),region.Top);
                        Equal(I(expected,"Bottom"),region.Bottom);
                        var tile = consumer.GetProperty("probeTile");
                        bool includesStation = I(tile,"x") >= region.Left && I(tile,"x") <= region.Right &&
                            I(tile,"y") >= region.Top && I(tile,"y") <= region.Bottom;
                        Equal(consumer.GetProperty("adjTile")[I(tile,"type")].GetBoolean(),includesStation);
                        break;
                    case "Arcane":
                        var mana = new PlayerManaRegenerationState1458(I(before,"statMana"),I(before,"statManaMax2"),
                            F(before,"manaRegenDelay"),I(before,"manaRegenCount"),0);
                        var facts = new PlayerManaRegenerationFacts1458(0,0,false,false,unlocks.ArcaneCrystal,0,0,0);
                        Assert.True(VanillaRemoteManaRegeneration1458.TryStep(mana,in facts,out var next,out int rate));
                        Equal(I(after,"statMana"),next.Mana);
                        Equal(I(after,"manaRegenCount"),next.Count);
                        Equal(I(after,"manaRegen"),rate);
                        Equal(Bits(after,"manaRegenDelay"),BitConverter.SingleToInt32Bits(next.Delay));
                        break;
                    default: throw new InvalidOperationException("Unknown additive consumer");
                }
                Equal(consumer.GetProperty("beforeRandom").GetRawText(),consumer.GetProperty("afterRandom").GetRawText());
                Equal(I(consumer,"beforeNext"),I(consumer,"afterNext"));
            }
        }
        Equal(48,rows);
    }

    [Fact]
    public void Original_ambrosia_normalization_reaches_remote_placement_without_scaling_tool_animation()
    {
        int rows = 0, passivePhases = 0, meleePhases = 0;
        foreach (string name in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using var stream = typeof(SelectedPermanentUnlock1458Tests).Assembly.GetManifestResourceStream("AmbrosiaConsumers" + name + "1458");
            Assert.NotNull(stream);
            using var gzip = new GZipStream(stream,CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            foreach (JsonElement row in source.RootElement.EnumerateArray())
            {
                rows++;
                JsonElement raw = row.GetProperty("rawBoosted"), normalized = row.GetProperty("normalized");
                var boosts = new PlayerPermanentBoosterState1458(I(raw,"statDefense"),I(raw,"fishingSkill"),
                    F(raw,"pickSpeed"),F(raw,"tileSpeed"),F(raw,"wallSpeed"));
                Assert.True(VanillaSelectedConsumable1458.TryFinalizePermanentSpeeds(in boosts,out var speed));
                Equal(Bits(normalized,"pickSpeed"),BitConverter.SingleToInt32Bits(speed.PickSpeed));
                Equal(Bits(normalized,"tileSpeed"),BitConverter.SingleToInt32Bits(speed.TileAnimationMultiplier));
                Equal(Bits(normalized,"wallSpeed"),BitConverter.SingleToInt32Bits(speed.WallAnimationMultiplier));
                int item = I(row,"subsequentItem");
                JsonElement definition = row.GetProperty("itemMetadata");
                float multiplier = I(definition,"createTile") >= 0 ? speed.TileAnimationMultiplier : speed.WallAnimationMultiplier;
                JsonElement direct = row.GetProperty("directAnimation");
                int useAnimation = I(definition,"useAnimation");
                int animation = Math.Max(1,(int)(useAnimation * (item is 2 or 93 ? multiplier : 1f)));
                Equal(animation,I(direct,"itemAnimation"));
                Equal(animation,I(direct,"itemAnimationMax"));
                if (item is 3509 or 4711 && row.GetProperty("tool").ValueKind == JsonValueKind.Object)
                {
                    // Local mining/shovel ItemTime is separate from remote animation and generic positive-minimum ApplyItemTime.
                    int toolTime = (int)(I(definition,"useTime") * speed.PickSpeed);
                    Equal(toolTime,I(row.GetProperty("tool").GetProperty("after"),"itemTime"));
                }
                // Shovel is not in the existing admitted remote style1 catalog. Its direct local
                // source checkpoint above is not used to extend the remote owner silently.
                if (item == 4711) continue;
                JsonElement before = row.GetProperty("remoteBefore");
                var clocks = Clocks(before);
                var random = new VanillaUnifiedRandom1458(0);
                SetRandom(random,row.GetProperty("remoteBeforeRandom"));
                foreach (JsonElement phase in row.GetProperty("remotePhases").EnumerateArray())
                {
                    PlayerSelectedConsumableState1458 next;
                    if (item is 2 or 93)
                    {
                        var facts = new PlayerRemotePassiveItemFacts1458(new(item),B(before,"controlUseItem"),
                            B(before,"lastItemUseAttemptSuccess"),B(before,"cursed"),false,false);
                        Assert.True(VanillaRemotePassiveItemCheck1458.TryStep(clocks,in facts,random.Next,out var actual,multiplier));
                        next = actual.State;
                        passivePhases++;
                    }
                    else
                    {
                        var facts = new PlayerRemoteMeleeItemFacts1458(new(item),B(before,"controlUseItem"),
                            B(before,"lastItemUseAttemptSuccess"),B(before,"cursed"),false,false);
                        Assert.True(VanillaRemoteMeleeItemCheck1458.TryStep(clocks,in facts,random.Next,out var actual));
                        next = actual.State;
                        meleePhases++;
                    }
                    Equal(Clocks(phase.GetProperty("state")),next);
                    Equal(phase.GetProperty("random").GetProperty("cursor").GetUInt32(),Access.Cursor(random));
                    Equal(phase.GetProperty("random").GetProperty("state").EnumerateArray().Select(x=>x.GetInt32()).ToArray(),Access.Seed(random));
                    Equal(I(phase,"next"),random.Clone().Next());
                    clocks = next;
                }
            }
        }
        Equal(64,rows);
        Equal(640,passivePhases);
        Equal(320,meleePhases);
        int metadataRows = 0;
        foreach (string name in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using var stream = typeof(SelectedPermanentUnlock1458Tests).Assembly.GetManifestResourceStream("AmbrosiaPlacementMetadata"+name+"1458");
            Assert.NotNull(stream);
            using var gzip = new GZipStream(stream,CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            foreach (JsonElement row in source.RootElement.EnumerateArray())
            {
                metadataRows++;
                JsonElement metadata = row.GetProperty("metadata");
                Assert.True(VanillaRemotePassiveItemCatalog1458.TryGet(new(I(row,"id")),default,out var definition));
                Equal(true,definition.PermitsUse);
                Equal(I(metadata,"useAnimation"),definition.FreshAnimationTicks);
                Equal(B(metadata,"autoReuse"),definition.AutoReuse);
                Equal(false,B(metadata,"melee"));
                Equal(false,B(metadata,"summon"));
                Equal(0,I(metadata,"mana"));
                Equal(false,B(row,"noMeleeSpeedBonus"));
                Equal(false,B(row,"summonSpeed"));
                Equal(true,I(metadata,"createTile") >= 0 || I(metadata,"createWall") >= 0);
            }
        }
        Equal(13_472,metadataRows);
    }

    private static PlayerPermanentUnlockState1458 Unlocks(JsonElement state)
    {
        string[] fields = ["usedAegisCrystal", "usedAegisFruit", "usedArcaneCrystal", "usedGalaxyPearl",
            "usedGummyWorm", "usedAmbrosia", "ateArtisanBread", "extraAccessory"];
        byte flags = 0;
        for (int index = 0; index < fields.Length; index++)
            if (B(state, fields[index])) flags |= (byte)(1 << index);
        return new(flags);
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
