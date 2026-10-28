using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class AnkhEquipmentImmunity1458Tests
{
    public static IEnumerable<object[]> Equipment() => Rows("equipment-addbuff");
    public static IEnumerable<object[]> Status() => Rows("status-from-npc");

    [Theory, MemberData(nameof(Equipment))]
    public void Actual_original_equipment_grants_and_AddBuff_immunity_preserve_common_prefix_fields(int seed, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var row = doc.RootElement;
        var combat = Build(row.GetProperty("type").GetInt32(), row.GetProperty("prefix").GetByte(), out int mana);
        var grant = row.GetProperty("granted").GetProperty("player");
        Assert.Equal(grant.GetProperty("statDefense").GetInt32(), combat.Defense);
        Assert.Equal(grant.GetProperty("statManaMax2").GetInt32() - 20, mana);
        Assert.Equal(grant.GetProperty("noKnockback").GetBoolean(), combat.NoKnockback);
        Assert.Equal(F(grant, "meleeDamage"), combat.MeleeDamage);
        Assert.Equal(F(grant, "rangedDamage"), combat.RangedDamage);
        Assert.Equal(F(grant, "magicDamage"), combat.MagicDamage);
        Assert.Equal(F(grant, "minionDamage"), combat.MinionDamage);
        Assert.Equal(F(grant, "meleeSpeed"), combat.MeleeAttackSpeed);
        Assert.Equal(grant.GetProperty("meleeCrit").GetInt32(), combat.MeleeCrit);
        Assert.Equal(grant.GetProperty("rangedCrit").GetInt32(), combat.RangedCrit);
        Assert.Equal(grant.GetProperty("magicCrit").GetInt32(), combat.MagicCrit);
        var immune = grant.GetProperty("buffImmune").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
        for (int id = 1; id < VanillaBuffIds.Count; id++)
            Assert.Equal(immune.Contains(id), combat.IsImmuneToNpcContactBuff(new(id)));
        var buffs = new PlayerBuffState();
        foreach (int id in new[] { 20, 22, 23, 30, 31, 32, 33, 35, 36, 156, 24 })
            buffs.TryApplyNpcContactBuff(new(id), 120, combat.IsImmuneToNpcContactBuff(new(id)));
        Slots(row.GetProperty("after").GetProperty("player"), buffs);
        var random = new VanillaUnifiedRandom1458(seed);
        RandomState(row.GetProperty("before").GetProperty("rng"), random);
        RandomState(row.GetProperty("granted").GetProperty("rng"), random);
        RandomState(row.GetProperty("after").GetProperty("rng"), random);
    }

    [Theory, MemberData(nameof(Status))]
    public void Actual_original_StatusFromNPC_spends_same_proc_entropy_before_equipment_immunity(int seed, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var row = doc.RootElement;
        var combat = Build(row.GetProperty("type").GetInt32(), 0, out _);
        var random = new VanillaUnifiedRandom1458(seed);
        RandomState(row.GetProperty("before").GetProperty("rng"), random);
        Span<NpcContactBuff1458> offers = stackalloc NpcContactBuff1458[4];
        int count = VanillaNpcContactBuffs1458.Evaluate(1, 1, 366f, false, false, false, new FishingRandom1458(random), offers, false);
        var buffs = new PlayerBuffState();
        foreach (var offer in offers[..count])
            buffs.TryApplyNpcContactBuff(offer.Type, offer.DurationTicks, combat.IsImmuneToNpcContactBuff(offer.Type));
        Slots(row.GetProperty("after").GetProperty("player"), buffs);
        RandomState(row.GetProperty("after").GetProperty("rng"), random);
    }

    internal static VanillaPlayerCombatSnapshot Build(int type, byte prefix, out int mana)
    {
        PlayerEquipmentCommitRequest[] items = type == 0 ? [] : [new(new(0), 62, 1, prefix, (short)type, 0)];
        var context = new VanillaPlayerCombatEquipmentContext(false, false, false)
            { LocalVanityArmor = VanillaPlayerLocalVanityArmor1458.Empty };
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(items, in context, out var combat, out mana));
        return combat;
    }

    internal static IEnumerable<object[]> Rows(string kind)
    {
        using var stream = typeof(AnkhEquipmentImmunity1458Tests).Assembly.GetManifestResourceStream("AnkhEquipmentLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var capture in document.RootElement.GetProperty("captures").EnumerateArray())
            foreach (var row in capture.GetProperty("rows").EnumerateArray())
                if (row.GetProperty("kind").GetString() == kind)
                    yield return [capture.GetProperty("seed").GetInt32(), row.GetRawText()];
    }
    internal static float F(JsonElement state, string key) =>
        BitConverter.UInt32BitsToSingle(Convert.ToUInt32(state.GetProperty(key).GetString(), 16));
    internal static void Slots(JsonElement expected, PlayerBuffState buffs)
    {
        buffs.CaptureSlots(out var types, out var times);
        Assert.Equal(expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()), types.Select(t => t.Value));
        Assert.Equal(expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()), times);
    }
    internal static void RandomState(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
    }
}
