using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class InheritedEquipmentSource1458Tests
{
    [Fact]
    public void Original_local_and_dedicated_whole_equips_inherit_the_exact_selected_stats()
    {
        using Stream source = typeof(InheritedEquipmentSource1458Tests).Assembly.GetManifestResourceStream("InheritedEquipment1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034",
            document.RootElement.GetProperty("sourceSha256").GetString());
        var rows = document.RootElement.GetProperty("rows");
        Assert.Equal(34, rows.GetArrayLength());
        foreach (JsonElement row in rows.EnumerateArray())
        {
            var equipment = new List<PlayerEquipmentCommitRequest>();
            ItemTypeId head = default, body = default, legs = default;
            foreach (JsonElement item in row.GetProperty("equipment").EnumerateArray())
            {
                int loadout = item.GetProperty("loadout").GetInt32();
                int index = item.GetProperty("slot").GetInt32();
                int type = item.GetProperty("item").GetInt32();
                if (loadout == -2 || loadout == -1 && row.GetProperty("localSlot").GetInt32() == 0)
                {
                    if (index == 10) head = new(type);
                    if (index == 11) body = new(type);
                    if (index == 12) legs = new(type);
                }
                if (loadout == -2 && row.GetProperty("localSlot").GetInt32() != 0) continue;
                short slot = checked((short)(loadout < 0 ? VanillaPlayerItemSlotCatalog.ArmorStart + index :
                    VanillaPlayerItemSlotCatalog.LoadoutArmorStart + loadout * VanillaPlayerItemSlotCatalog.LoadoutStride + index));
                equipment.Add(new(default, slot, 1, item.GetProperty("prefix").GetByte(), checked((short)type),
                    item.GetProperty("favorite").GetBoolean() ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0));
            }
            int mode = row.GetProperty("mode").GetInt32();
            bool good = row.GetProperty("good").GetBoolean();
            var context = new VanillaPlayerCombatEquipmentContext(row.GetProperty("heart").GetBoolean(),
                mode > 0 || good, mode == 2 || mode == 1 && good)
            {
                LocalVanityArmor = new(head, body, legs)
            };
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment.ToArray(), in context, out var actual), row.GetProperty("name").GetString());
            Assert.Equal(row.GetProperty("defense").GetInt32(), actual.Defense);
            Assert.Equal(row.GetProperty("rangedDamage").GetSingle(), actual.RangedDamage);
            Assert.Equal(row.GetProperty("magicQuiver").GetBoolean(), actual.MagicQuiver);
            Assert.Equal(row.GetProperty("arrowDamageAdditiveStack").GetSingle(), actual.ArrowDamageAdditiveStack);
            Assert.Equal(906992634, row.GetProperty("next").GetInt32());
        }
    }

    [Fact]
    public void Unknown_selected_sharing_facts_refuse_without_rejecting_irrelevant_or_locked_favorites()
    {
        var unknown = new VanillaPlayerCombatEquipmentContext(false, false, false);
        var dedicated = unknown with { LocalVanityArmor = VanillaPlayerLocalVanityArmor1458.Empty };
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Favorite(0, 89)], in unknown, out _));
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Active(0, 89), Favorite(0, 999)], in unknown, out var masked));
        Assert.Equal(1, masked.Defense);
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Favorite(3, 999)], in dedicated, out _));
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Favorite(3, 491, prefix: 255)], in dedicated, out _));
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Favorite(8, 999, prefix: 255), Favorite(9, 999)], in unknown, out var locked));
        Assert.Equal(VanillaPlayerCombatSnapshot.Baseline, locked);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Favorite(3, 3508), Favorite(3, 491, loadout: 1)], in unknown, out var firstInvalid));
        Assert.Equal(VanillaPlayerCombatSnapshot.Baseline, firstInvalid);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Favorite(3, 491), Active(13, 999)], in unknown, out var irrelevant));
        Assert.Equal(1.15f, irrelevant.RangedDamage);
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Favorite(3, 2609), Active(13, 999), Favorite(4, 999)], in unknown, out _));
    }

    private static PlayerEquipmentCommitRequest Active(int index, int type) =>
        new(default, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + index)), 1, 0, checked((short)type), 0);

    private static PlayerEquipmentCommitRequest Favorite(int index, int type, int loadout = 0, byte prefix = 0) =>
        new(default, checked((short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart + loadout * VanillaPlayerItemSlotCatalog.LoadoutStride + index)),
            1, prefix, checked((short)type), PlayerEquipmentCommitRequest.FavoriteItemFlag);
}
