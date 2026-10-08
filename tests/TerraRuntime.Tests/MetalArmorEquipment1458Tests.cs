using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class MetalArmorEquipment1458Tests
{
    [Fact]
    public void Every_metal_piece_matches_original_defaults_and_granted_defense()
    {
        using JsonDocument facts = LoadFacts();
        JsonElement pieces = facts.RootElement.GetProperty("pieces");
        Assert.Equal(26, pieces.GetArrayLength());
        foreach (JsonElement piece in pieces.EnumerateArray())
        {
            int id = piece.GetProperty("id").GetInt32();
            int slot = piece.GetProperty("headSlot").GetInt32() >= 0 ? 0 :
                piece.GetProperty("bodySlot").GetInt32() >= 0 ? 1 : 2;
            Assert.Equal(18, piece.GetProperty("width").GetInt32());
            Assert.Equal(18, piece.GetProperty("height").GetInt32());
            Assert.Equal(piece.GetProperty("defense").GetInt32(), piece.GetProperty("grantedDefense").GetInt32());
            Assert.Equal(0, piece.GetProperty("lifeRegen").GetInt32());
            Assert.Equal(906992634, piece.GetProperty("next").GetInt32());
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equipment(slot, id)], out var actual));
            Assert.Equal(VanillaPlayerCombatSnapshot.Baseline with
            {
                Defense = piece.GetProperty("grantedDefense").GetInt32()
            }, actual);
            Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equipment((slot + 1) % 3, id)], out _));
            Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equipment(slot, id, 65)], out _));
        }
    }

    [Fact]
    public void Ten_complete_sets_match_original_registered_effect_and_share_pvp_mitigation()
    {
        using JsonDocument facts = LoadFacts();
        JsonElement sets = facts.RootElement.GetProperty("sets");
        Assert.Equal(10, sets.GetArrayLength());
        foreach (JsonElement set in sets.EnumerateArray())
        {
            int[] ids = set.GetProperty("ids").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
                [Equipment(0, ids[0]), Equipment(1, ids[1]), Equipment(2, ids[2])], out var actual));
            int defense = set.GetProperty("defense").GetInt32();
            Assert.Equal(VanillaPlayerCombatSnapshot.Baseline with { Defense = defense }, actual);
            Assert.Equal(0, set.GetProperty("lifeRegen").GetInt32());
            Assert.Equal(906992634, set.GetProperty("next").GetInt32());
            var handle = new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1));
            var attack = new AuthoritativeAttackDamage(DamageSource.FromPlayerItem(handle), 40, 0, false, 1f, 1);
            Assert.True(VanillaCombatDamagePipeline.TryResolvePvp(in attack, in actual, false, out var classic));
            Assert.Equal((int)(40 - defense * 0.5f), classic.Damage);
            Assert.True(VanillaCombatDamagePipeline.TryResolvePvp(in attack, in actual, false, out var master,
                expertMode: true, masterMode: true));
            Assert.Equal(40 - defense, master.Damage);
        }
    }

    [Fact]
    public void Mixed_and_incomplete_sets_only_gain_their_individual_defense()
    {
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [Equipment(0, 696), Equipment(1, 83), Equipment(2, 79)], out var mixed));
        Assert.Equal(VanillaPlayerCombatSnapshot.Baseline with { Defense = 14 }, mixed);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [Equipment(0, 954), Equipment(1, 81)], out var incomplete));
        Assert.Equal(VanillaPlayerCombatSnapshot.Baseline with { Defense = 5 }, incomplete);
    }

    [Fact]
    public void Existing_unknown_unlock_endgame_and_accessory_guards_are_preserved()
    {
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equipment(0, 999)], out _));
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equipment(8, 696)],
            new VanillaPlayerCombatEquipmentContext(null, true, false), out _));
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [Equipment(0, VanillaItemIds.SolarFlareHelmet.Value),
             Equipment(1, VanillaItemIds.SolarFlareBreastplate.Value),
             Equipment(2, VanillaItemIds.SolarFlareLeggings.Value)], out _));
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [Equipment(0, 696), Equipment(1, 697), Equipment(2, 698), Equipment(3, 156, 65)], out var shield));
        Assert.Equal(VanillaPlayerCombatSnapshot.Baseline with { Defense = 25, NoKnockback = true }, shield);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [new PlayerEquipmentCommitRequest(new PlayerSlotId(0), VanillaPlayerItemSlotCatalog.VanityArmorStart,
                Stack: 1, Prefix: 65, ItemNetId: 696, ItemFlags: 0)], out var vanity));
        Assert.Equal(VanillaPlayerCombatSnapshot.Baseline, vanity);
    }

    private static PlayerEquipmentCommitRequest Equipment(int armorIndex, int id, byte prefix = 0) =>
        new(new PlayerSlotId(0), checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + armorIndex)),
            Stack: 1, Prefix: prefix, ItemNetId: checked((short)id), ItemFlags: 0);

    private static JsonDocument LoadFacts()
    {
        using Stream stream = typeof(MetalArmorEquipment1458Tests).Assembly.GetManifestResourceStream("MetalArmorEquipment1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
