using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class AnkhEquipmentSharing1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(AnkhEquipmentSharing1458Tests).Assembly.GetManifestResourceStream("AnkhEquipmentSharingLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_original_favorite_loadout_immunity_uses_existing_effective_equipment_selector(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var row = doc.RootElement;
        int type = row.GetProperty("type").GetInt32();
        var context = new VanillaPlayerCombatEquipmentContext(false, false, false)
            { LocalVanityArmor = VanillaPlayerLocalVanityArmor1458.Empty };
        var favorite = new PlayerEquipmentCommitRequest(new(0),
            (short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart + 3), 1, 0, (short)type, 1);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([favorite], in context, out var combat));
        var source = row.GetProperty("after").GetProperty("player");
        var immune = source.GetProperty("buffImmune").EnumerateArray().Select(v => v.GetInt32()).ToHashSet();
        for (int id = 1; id < VanillaBuffIds.Count; id++)
            Assert.Equal(immune.Contains(id), combat.IsImmuneToNpcContactBuff(new(id)));
        Assert.Equal(type, row.GetProperty("effectiveType").GetInt32());
        Assert.Equal(source.GetProperty("statDefense").GetInt32(), combat.Defense);
        Assert.Equal(AnkhEquipmentImmunity1458Tests.F(source, "meleeDamage"), combat.MeleeDamage);
    }
}
