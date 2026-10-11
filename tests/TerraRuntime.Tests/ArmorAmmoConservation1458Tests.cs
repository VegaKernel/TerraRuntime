using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class ArmorAmmoConservation1458Tests
{
    [Fact]
    public void Original_armor_flags_and_ordered_ammo_rolls_match_both_official_binaries()
    {
        string[] resources =
        [
            "ArmorAmmoLinuxFirst1458", "ArmorAmmoLinuxRepeat1458",
            "ArmorAmmoWindowsFirst1458", "ArmorAmmoWindowsRepeat1458"
        ];
        foreach (string resource in resources)
        {
            using Stream compressed = typeof(ArmorAmmoConservation1458Tests).Assembly
                .GetManifestResourceStream(resource)!;
            using var stream = new GZipStream(compressed, CompressionMode.Decompress);
            using JsonDocument document = JsonDocument.Parse(stream);
            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                VerifyOriginalRow(row);
            }
        }
        VerifyStandaloneBoundary();
    }

    private static void VerifyStandaloneBoundary()
    {
        foreach (string resource in new[] { "StandaloneCheckpointLinuxFirst1458", "StandaloneCheckpointLinuxRepeat1458", "StandaloneCheckpointWindowsFirst1458", "StandaloneCheckpointWindowsRepeat1458", "StandaloneComposedLinuxFirst1458", "StandaloneComposedLinuxRepeat1458", "StandaloneComposedWindowsFirst1458", "StandaloneComposedWindowsRepeat1458" })
        {
            using Stream compressed = typeof(ArmorAmmoConservation1458Tests).Assembly.GetManifestResourceStream(resource)!;
            using var stream = new GZipStream(compressed, CompressionMode.Decompress);
            using JsonDocument document = JsonDocument.Parse(stream);
            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetStandaloneWeapon(new ItemTypeId((short)Int(row, "itemId")), out var weapon));
                JsonElement metadata = row.GetProperty("metadata");
                Assert.Equal(Bool(metadata, "ranged"), weapon.Ranged);
                Assert.True(Bool(metadata, "canConsume"));
                Assert.Equal(JsonValueKind.Null, metadata.GetProperty("forceConsumption").ValueKind);
                int mask = Int(row, "mask");
                var snapshot = VanillaPlayerCombatSnapshot.Baseline with
                {
                    HuntressAmmoCost90 = (mask & 1) != 0,
                    ChloroAmmoCost80 = (mask & 2) != 0,
                    AmmoCost80 = (mask & 4) != 0,
                    AmmoCost75 = (mask & 8) != 0
                };
                JsonElement checkpoint = row.GetProperty("consumptionBeforeRandom");
                var random = new VanillaUnifiedRandom1458(0);
                checkpoint.GetProperty("state").EnumerateArray().Select(value => value.GetInt32()).ToArray().CopyTo(RandomSeeds(random), 0);
                RandomCursor(random) = checkpoint.GetProperty("cursor").GetUInt32();
                Assert.Equal(Int(row, "consumptionBeforeNext"), random.Clone().Next());
                bool conserved = VanillaProjectileWeaponCombatCatalog.PrepareStandaloneConservation(in weapon, in snapshot, random.Next);
                JsonElement before = row.GetProperty("before").GetProperty("inventory");
                JsonElement after = row.GetProperty("after").GetProperty("inventory");
                Assert.Equal(Int(before[0], "stack") - (conserved ? 0 : 1), Int(after[0], "stack"));
                for (int slot = 0; slot < 59; slot++)
                {
                    Assert.Equal(Int(before[slot], "type"), Int(after[slot], "type"));
                    Assert.Equal(Int(before[slot], "prefix"), Int(after[slot], "prefix"));
                    Assert.Equal(Bool(before[slot], "favorite"), Bool(after[slot], "favorite"));
                    if (slot != 0) Assert.Equal(Int(before[slot], "stack"), Int(after[slot], "stack"));
                }
                JsonElement expected = row.GetProperty("afterRandom");
                Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(value => value.GetInt32()).ToArray(), RandomSeeds(random));
                Assert.Equal(expected.GetProperty("cursor").GetUInt32(), RandomCursor(random));
                Assert.Equal(Int(row, "afterNext"), random.Clone().Next());
            }
        }
    }

    private static void VerifyOriginalRow(JsonElement row)
    {
        int piece = Int(row, "piece");
        int mask = Int(row, "configuredFlagMask");
        JsonElement expected = row.GetProperty("beforeCombat");
        VanillaPlayerCombatSnapshot snapshot = VanillaPlayerCombatSnapshot.Baseline;
        if (piece > 0)
        {
            var equipment = new PlayerEquipmentCommitRequest(new PlayerSlotId(0),
                (short)(piece == 1002 ? 59 : 60), 1, 0, (short)piece, 0);
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([equipment], out snapshot));
        }
        if (mask >= 0)
        {
            snapshot = snapshot with
            {
                HuntressAmmoCost90 = (mask & 1) != 0,
                ChloroAmmoCost80 = (mask & 2) != 0,
                AmmoCost80 = (mask & 4) != 0,
                AmmoCost75 = (mask & 8) != 0
            };
        }
        Assert.Equal(Bool(expected, "huntressAmmoCost90"), snapshot.HuntressAmmoCost90);
        Assert.Equal(Bool(expected, "chloroAmmoCost80"), snapshot.ChloroAmmoCost80);
        Assert.Equal(Bool(expected, "ammoCost80"), snapshot.AmmoCost80);
        Assert.Equal(Bool(expected, "ammoCost75"), snapshot.AmmoCost75);
        Assert.Equal(Single(expected, "rangedDamage"), snapshot.RangedDamage);
        Assert.Equal(Single(expected, "minionDamage"), snapshot.MinionDamage);
        Assert.Equal(Single(expected, "meleeDamage"), snapshot.MeleeDamage);
        Assert.Equal(Single(expected, "magicDamage"), snapshot.MagicDamage);
        Assert.Equal(Int(expected, "rangedCrit"), snapshot.RangedCrit);

        snapshot = snapshot with { MagicQuiver = Bool(expected, "magicQuiver") };
        int weaponType = Int(row, "weapon");
        int ammoType = Int(row, "ammo");
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(
            new ItemTypeId(weaponType), out var weapon));
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetAmmo(
            weapon.AmmoFamily, new ItemTypeId(ammoType), out var ammo));
        Assert.True(Bool(row, "canShoot"));
        Assert.Equal(ammoType, Int(row, "usedAmmo"));
        // This literal is the source PickAmmo input, not its private conservation decision.
        Assert.False(Bool(row, "dontConsume"));

        var random = new VanillaUnifiedRandom1458(Int(row, "seed"));
        bool conserved = VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(
            in weapon, in ammo, in snapshot, random.Next, ammoBox: true, ammoPotion: true);
        JsonElement beforeInventory = row.GetProperty("before").GetProperty("inventory");
        JsonElement afterInventory = row.GetProperty("after").GetProperty("inventory");
        Assert.Equal(59, beforeInventory.GetArrayLength());
        Assert.Equal(59, afterInventory.GetArrayLength());
        int beforeStack = Int(beforeInventory[54], "stack");
        int expectedStack = beforeStack - (!conserved && ammo.Consumable ? 1 : 0);
        Assert.Equal(expectedStack, Int(afterInventory[54], "stack"));
        // The detached Game owner returns a decision, not an inventory journal. These
        // checks also certify that the original direct PickAmmo changes no other tuple.
        for (int slot = 0; slot < 59; slot++)
        {
            Assert.Equal(Int(beforeInventory[slot], "type"), Int(afterInventory[slot], "type"));
            Assert.Equal(Int(beforeInventory[slot], "prefix"), Int(afterInventory[slot], "prefix"));
            Assert.Equal(Bool(beforeInventory[slot], "favorite"), Bool(afterInventory[slot], "favorite"));
            if (slot != 54)
            {
                Assert.Equal(Int(beforeInventory[slot], "stack"), Int(afterInventory[slot], "stack"));
            }
        }

        JsonElement expectedRandom = row.GetProperty("afterRandom");
        int[] sourceSeeds = expectedRandom.GetProperty("state").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        Assert.Equal(sourceSeeds, RandomSeeds(random));
        Assert.Equal(expectedRandom.GetProperty("cursor").GetUInt32(), RandomCursor(random));
        Assert.Equal(Int(row, "afterNext"), random.Clone().Next());
    }

    [Fact]
    public void Armor_roles_preserve_static_benefits_and_reject_wrong_functional_slots()
    {
        (int Id, int Index, int Defense)[] pieces =
        [
            (3804, 1, 17), (4983, 1, 14), (1002, 0, 13),
            (3878, 1, 24), (1549, 1, 24), (2758, 1, 28)
        ];
        foreach (var piece in pieces)
        {
            var request = new PlayerEquipmentCommitRequest(new PlayerSlotId(0),
                (short)(59 + piece.Index), 1, 0, (short)piece.Id, 0);
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([request], out var actual));
            Assert.Equal(piece.Defense, actual.Defense);
            var inherited = request with
            {
                SlotId = (short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart + piece.Index),
                ItemFlags = PlayerEquipmentCommitRequest.FavoriteItemFlag
            };
            var context = new VanillaPlayerCombatEquipmentContext(false, false, false)
            {
                LocalVanityArmor = VanillaPlayerLocalVanityArmor1458.Empty
            };
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
                [inherited], in context, out var selected));
            Assert.Equal(actual, selected);
            var blocked = context with
            {
                LocalVanityArmor = new VanillaPlayerLocalVanityArmor1458(
                    new ItemTypeId(piece.Id), default, default)
            };
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
                [inherited], in blocked, out var ignored));
            Assert.Equal(VanillaPlayerCombatSnapshot.Baseline, ignored);
            foreach (int wrongSlot in Enumerable.Range(0, 3).Where(slot => slot != piece.Index))
            {
                Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild(
                    [request with { SlotId = (short)(59 + wrongSlot) }], out _));
            }
        }

        VanillaPlayerCombatSnapshot baseline = VanillaPlayerCombatSnapshot.Baseline;
        Assert.False(baseline.HuntressAmmoCost90 || baseline.ChloroAmmoCost80 ||
            baseline.AmmoCost80 || baseline.AmmoCost75);
        // The existing complete Vortex stealth gate remains fail-closed.
        PlayerEquipmentCommitRequest[] fullSet =
        [
            new(new PlayerSlotId(0), 59, 1, 0, 2757, 0),
            new(new PlayerSlotId(0), 60, 1, 0, 2758, 0),
            new(new PlayerSlotId(0), 61, 1, 0, 2759, 0)
        ];
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild(fullSet, out _));
    }

    private static int Int(JsonElement element, string name) => element.GetProperty(name).GetInt32();
    private static float Single(JsonElement element, string name) => element.GetProperty(name).GetSingle();
    private static bool Bool(JsonElement element, string name) => element.GetProperty(name).GetBoolean();

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] RandomSeeds(VanillaUnifiedRandom1458 random);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint RandomCursor(VanillaUnifiedRandom1458 random);
}
