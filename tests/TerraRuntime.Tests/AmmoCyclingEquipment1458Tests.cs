using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using Xunit;

namespace TerraRuntime.Tests.AmmoCycling1458;

public sealed class AmmoCyclingEquipment1458Tests
{
    [Fact]
    public void Original_equipment_priorities_selector_state_and_random_match_both_editions()
    {
        using var metadata = Source("AmmoCyclingMetadata1458");
        var ammoFacts = metadata.RootElement.EnumerateArray().ToDictionary(
            x => x.GetProperty("type").GetInt32(),
            x => (Family: x.GetProperty("ammo").GetInt32(), Consumable: x.GetProperty("consumable").GetBoolean()));
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            CompareEquipmentCombinations(platform);
            using var source = Source("AmmoCyclingRows" + platform + "1458");
            Assert.Equal(1737, source.RootElement.GetArrayLength());
            foreach (var row in source.RootElement.EnumerateArray())
            {
                switch (row.GetProperty("kind").GetString())
                {
                    case "equipment": CompareEquipment(row); break;
                    case "selection": CompareSelection(row, ammoFacts); break;
                    case "offset-boundary": CompareBoundary(row); break;
                    default: Assert.Fail("Unknown original source row kind."); break;
                }
            }
        }
    }

    private static void CompareEquipmentCombinations(string platform)
    {
        using var document = Source("AmmoCyclingEquipment" + platform + "1458");
        Assert.Equal(752, document.RootElement.GetArrayLength());
        foreach (var row in document.RootElement.EnumerateArray())
        {
            int index = 0;
            var equipment = row.GetProperty("full").GetProperty("armor").EnumerateArray().Select(item =>
                new PlayerEquipmentCommitRequest(new(0), checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + index++)),
                    item.GetProperty("stack").GetInt16(), item.GetProperty("prefix").GetByte(),
                    item.GetProperty("type").GetInt16(), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0)).ToArray();
            int gameMode = row.GetProperty("context").GetInt32();
            Assert.False(row.GetProperty("gameMenu").GetBoolean());
            Assert.Equal(gameMode>=1,row.GetProperty("usableSlots")[8].GetBoolean());
            Assert.Equal(gameMode>=2,row.GetProperty("usableSlots")[9].GetBoolean());
            var context = new VanillaPlayerCombatEquipmentContext(true, gameMode >= 1, gameMode >= 2)
            {
                LocalVanityArmor = VanillaPlayerLocalVanityArmor1458.Empty,
                EnableAmmoCyclingAccessories = true
            };
            var buffs = row.GetProperty("full").GetProperty("buffType").EnumerateArray()
                .Select(x => new BuffTypeId(x.GetUInt16())).Where(x => x.Value != 0).ToArray();
            var baseline = VanillaPlayerCombatSnapshot.Baseline;
            Assert.True(VanillaPlayerCombatBuffs1458.TryApply(in baseline, buffs, out var afterBuffs));
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, in context, in afterBuffs, out var actual, out int manaBonus));
            var expected = row.GetProperty("after");
            Assert.Equal(expected.GetProperty("ammoCyclingMode").GetInt32(), (int)actual.AmmoCyclingMode);
            Assert.Equal(expected.GetProperty("accAmmoCyclerDamage").GetBoolean(), actual.AmmoCyclerDamage);
            Assert.Equal(expected.GetProperty("rangedDamageBits").GetUInt32(), BitConverter.SingleToUInt32Bits(actual.RangedDamage));
            Assert.Equal(expected.GetProperty("meleeDamageBits").GetUInt32(), BitConverter.SingleToUInt32Bits(actual.MeleeDamage));
            Assert.Equal(expected.GetProperty("magicDamageBits").GetUInt32(), BitConverter.SingleToUInt32Bits(actual.MagicDamage));
            Assert.Equal(expected.GetProperty("meleeSpeedBits").GetUInt32(), BitConverter.SingleToUInt32Bits(actual.MeleeAttackSpeed));
            Assert.Equal(expected.GetProperty("statDefense").GetInt32(), actual.Defense);
            Assert.Equal(expected.GetProperty("rangedCrit").GetInt32(), actual.RangedCrit);
            Assert.Equal(expected.GetProperty("meleeCrit").GetInt32(), actual.MeleeCrit);
            Assert.Equal(expected.GetProperty("magicCrit").GetInt32(), actual.MagicCrit);
            Assert.Equal(expected.GetProperty("manaCostBits").GetUInt32(), BitConverter.SingleToUInt32Bits(actual.ManaCostMultiplier));
            Assert.Equal(expected.GetProperty("statManaMax2").GetInt32() - row.GetProperty("before").GetProperty("statManaMax2").GetInt32(), manaBonus);
            if (row.GetProperty("single").GetBoolean())
            {
                Assert.Equal(expected.GetProperty("moveSpeedBits").GetUInt32(), BitConverter.SingleToUInt32Bits(actual.MoveSpeed));
                Assert.Equal(expected.GetProperty("minionDamageBits").GetUInt32(), BitConverter.SingleToUInt32Bits(actual.MinionDamage));
                Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment,
                    context with { EnableAmmoCyclingAccessories = false }, out _));
            }
            // The full legacy skyStone/summon/mining projection is not expanded
            // by this ranged-equipment block. Its raw source fields stay in the fixture.
        }
    }

    private static JsonDocument Source(string name)
    {
        using var stream = typeof(AmmoCyclingEquipment1458Tests).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing numeric original cycling resource.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static AmmoCyclingState1458 State(JsonElement value) =>
        new(value.GetProperty("offset").GetInt32(), value.GetProperty("cooldown").GetInt32());

    private static void CompareEquipment(JsonElement row)
    {
        var items = row.GetProperty("ids").EnumerateArray().Select(x => new ItemTypeId(x.GetInt32())).ToArray();
        VanillaAmmoCycling1458.ProjectEffectiveAccessories(items, out var mode, out bool bonus);
        var after = row.GetProperty("after");
        Assert.Equal(after.GetProperty("mode").GetInt32(), (int)mode);
        Assert.Equal(after.GetProperty("damage").GetBoolean(), bonus);
        var combat = VanillaAmmoCycling1458.ApplyEquipmentDamageBonus(VanillaPlayerCombatSnapshot.Baseline, bonus);
        Assert.Equal(after.GetProperty("rangedDamageBits").GetUInt32(), BitConverter.SingleToUInt32Bits(combat.RangedDamage));
        var requests=items.Select((item,index)=>new PlayerEquipmentCommitRequest(new(0),
            checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart+3+index)),1,0,checked((short)item.Value),0)).ToArray();
        var context=new VanillaPlayerCombatEquipmentContext(false,false,false){EnableAmmoCyclingAccessories=true};
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(requests,in context,out var projected,out _));
        Assert.Equal((int)mode,(int)projected.AmmoCyclingMode);Assert.Equal(bonus,projected.AmmoCyclerDamage);
        Assert.Equal(after.GetProperty("rangedDamageBits").GetUInt32(),BitConverter.SingleToUInt32Bits(projected.RangedDamage));
        var state = State(row.GetProperty("before"));
        Assert.Equal(state, State(after)); // ResetEffects/equipment do not reset persistent fields.
        foreach (var update in row.GetProperty("updates").EnumerateArray())
        {
            state = VanillaAmmoCycling1458.AdvanceLocalUpdate(in state);
            Assert.Equal(State(update.GetProperty("state")), state);
            Assert.Equal(0, update.GetProperty("state").GetProperty("mode").GetInt32());
            Assert.False(update.GetProperty("state").GetProperty("damage").GetBoolean());
        }
    }

    private static void CompareSelection(JsonElement row,
        Dictionary<int, (int Family, bool Consumable)> facts)
    {
        int weaponType = row.GetProperty("weaponId").GetInt32();
        int family = row.GetProperty("definition").GetProperty("useAmmo").GetInt32();
        int animation = row.GetProperty("definition").GetProperty("useAnimation").GetInt32();
        var mode = (AmmoCyclingMode1458)row.GetProperty("mode").GetInt32();
        bool dontConsume = row.GetProperty("dontConsume").GetBoolean();
        var items = row.GetProperty("before").GetProperty("inventory").EnumerateArray()
            .Take(58).Select(x => (Type: x.GetProperty("type").GetInt32(), Stack: x.GetProperty("stack").GetInt32())).ToArray();
        var state = State(row.GetProperty("beforeCycle"));
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        bool representedWeapon = VanillaProjectileWeaponCombatCatalog.TryGetWeapon(new(weaponType), out var weapon);
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            Assert.Equal(State(step.GetProperty("entry")), state);
            // The two selector exceptions are outside the retained launch catalog.
            // Supply their captured per-call input for the selector/prefix only;
            // do not claim our existing conservation/launch path implements them.
            if (!representedWeapon) RestoreConfiguredRandom(step.GetProperty("entryRandom"), random);
            SameRandom(step.GetProperty("entryRandom"), random);
            var inventory = items.Select(x => new AmmoCyclingSlot1458(facts[x.Type].Family, x.Stack)).ToArray();
            Assert.True(VanillaAmmoCycling1458.TrySelect(weaponType, family, mode, in state, inventory, out int selected));
            Assert.Equal(step.GetProperty("chosenSlot").GetInt32(), selected);
            bool compareRemaining = representedWeapon;
            if (selected >= 0)
            {
                Assert.True(VanillaAmmoCycling1458.TryAdvanceSelectedAmmo(animation, dontConsume, mode,
                    in state, random.Next, out state));
                if (representedWeapon)
                {
                    if (!VanillaProjectileWeaponCombatCatalog.TryGetAmmo(weapon.AmmoFamily,
                        new(items[selected].Type), out var ammo))
                    {
                        // Selector coverage includes compatible source ammo not
                        // admitted for runtime births. Conservation consumes only
                        // Family/Consumable; its unused launch scalars stay zero.
                        // This detached projection grants no launch admission.
                        ammo = new(new(items[selected].Type), default, weapon.AmmoFamily,
                            0, 0f, 0f, facts[items[selected].Type].Consumable);
                    }
                    var combat = VanillaPlayerCombatSnapshot.Baseline;
                    bool conserved = VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(in weapon,
                        in ammo, in combat, random.Next);
                    if (!dontConsume && !conserved && facts[items[selected].Type].Consumable)
                        items[selected] = items[selected].Stack == 1 ? (0, 0) :
                            (items[selected].Type, items[selected].Stack - 1);
                }
            }
            Assert.Equal(State(step.GetProperty("state")), state);
            var after = step.GetProperty("inventory").EnumerateArray().Take(58).ToArray();
            for (int slot = 0; slot < items.Length; slot++)
            {
                var sourceItem = after[slot].GetProperty("slot");
                var expected = (sourceItem.GetProperty("type").GetInt32(), sourceItem.GetProperty("stack").GetInt32());
                if (compareRemaining) Assert.Equal(expected, items[slot]);
                else items[slot] = expected;
            }
            if (representedWeapon)
            {
                SameRandom(step.GetProperty("random"), random);
                Assert.Equal(step.GetProperty("next").GetInt32(), random.Clone().Next());
            }
            state = VanillaAmmoCycling1458.AdvanceLocalUpdate(in state);
        }
        Assert.Equal(State(row.GetProperty("afterCycle")), state);
    }

    private static void CompareBoundary(JsonElement row)
    {
        var mode = (AmmoCyclingMode1458)row.GetProperty("mode").GetInt32();
        var state = State(row.GetProperty("before"));
        var inventory = new AmmoCyclingSlot1458[58];
        inventory[54] = new(40, 3); inventory[55] = new(40, 3);
        Assert.True(VanillaAmmoCycling1458.TrySelect(39, 40, mode, in state, inventory, out int slot));
        Assert.Equal(row.GetProperty("chosenSlot").GetInt32(), slot);
        var random = new VanillaUnifiedRandom1458(0);
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(new(39), out var bow));
        if (slot >= 0)
            Assert.True(VanillaAmmoCycling1458.TryAdvanceSelectedAmmo(bow.AnimationTicks, false, mode, in state, random.Next, out state));
        Assert.Equal(State(row.GetProperty("after")), state);
        SameRandom(row.GetProperty("random"), random);
    }

    private static void SameRandom(JsonElement source, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(source.GetProperty("cursor").GetUInt32(), Cursor(random));
        Assert.Equal(source.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), Seeds(random));
    }
    private static void RestoreConfiguredRandom(JsonElement source, VanillaUnifiedRandom1458 random)
    {
        Cursor(random) = source.GetProperty("cursor").GetUInt32();
        source.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo(Seeds(random), 0);
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 value);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] Seeds(VanillaUnifiedRandom1458 value);
}
