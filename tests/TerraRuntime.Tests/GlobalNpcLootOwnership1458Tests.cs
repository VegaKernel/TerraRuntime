using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class GlobalNpcLootOwnership1458Tests
{
    public static IEnumerable<object[]> Calendar() => Rows("calendar");
    public static IEnumerable<object[]> Metadata() => Rows("metadata");
    private static IEnumerable<object[]> Rows(string kind)
    {
        using var stream = typeof(GlobalNpcLootOwnership1458Tests).Assembly.GetManifestResourceStream(
            $"TerraRuntime.Tests.Fixtures.global-loot-ninth-{kind}-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Calendar))]
    public void Original_calendar_methods_with_only_date_input_substituted_match_leap_year_and_forced_flags(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var date = new DateTime(row.GetProperty("year").GetInt32(), row.GetProperty("month").GetInt32(),
            row.GetProperty("day").GetInt32()); int mask = row.GetProperty("mask").GetInt32();
        Assert.Equal(row.GetProperty("halloween").GetBoolean(), VanillaSeasonalCalendar1458.IsHalloween(date, (mask & 3) != 0));
        Assert.Equal(row.GetProperty("christmas").GetBoolean(), VanillaSeasonalCalendar1458.IsChristmas(date, (mask & 12) != 0));
    }

    [Theory, MemberData(nameof(Metadata))]
    public void Generalized_metadata_preserves_actual_positive_npc_boss_and_face_fields(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Assert.True(VanillaNpcSourceMetadata1458.TryGet(new(row.GetProperty("type").GetInt32()), out bool boss, out byte face));
        Assert.Equal(row.GetProperty("boss").GetBoolean(), boss);
        Assert.Equal(row.GetProperty("face").GetInt32(), face);
    }

    [Fact]
    public void Missing_fact_rejects_only_a_rule_whose_offer_it_can_change_without_rng()
    {
        var context = new VanillaNpcGlobalLootContext1458(VanillaNpcIds.SandSlime, 25, 10, 5, 100,
            false, false, 0, 16000, 8000, 47, 1, 4200, 1200, true, false)
        { Halloween = false, Christmas = false, Zones = default(PlayerZoneSnapshot1458), RockLayer = 200,
            WorldSurface = 140, SkeletronDowned = false, AnyMechDowned = false };
        Assert.True(VanillaNpcGlobalLoot1458.TryValidateContext(in context));
        var unknownZones = context with { Zones = null };
        Assert.False(VanillaNpcGlobalLoot1458.TryValidateContext(in unknownZones));
        var harmlessUnknownZones = unknownZones with { NpcValue = 0f };
        Assert.True(VanillaNpcGlobalLoot1458.TryValidateContext(in harmlessUnknownZones));
        var harmlessDifficulty = context with { Difficulty = null };
        Assert.True(VanillaNpcGlobalLoot1458.TryValidateContext(in harmlessDifficulty));
        var activeUnknownDifficulty = harmlessDifficulty with { Halloween = true };
        Assert.False(VanillaNpcGlobalLoot1458.TryValidateContext(in activeUnknownDifficulty));
        var activeUnknownCalendar = context with { Halloween = null };
        Assert.False(VanillaNpcGlobalLoot1458.TryValidateContext(in activeUnknownCalendar));
        var falseHolidayUnknownFriendly = context with { NpcFriendly = null, NpcValue = 0.5f, HardMode = false };
        Assert.True(VanillaNpcGlobalLoot1458.TryValidateContext(in falseHolidayUnknownFriendly));
        var activeUnknownFriendly = context with { NpcFriendly = null, Zones = new(32, 0, 0, 0, 0, 0) };
        Assert.False(VanillaNpcGlobalLoot1458.TryValidateContext(in activeUnknownFriendly));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(17)]
    public void Invalid_rule_index_rejects_before_any_roll(int index)
    {
        var context = new VanillaNpcGlobalLootContext1458(VanillaNpcIds.SandSlime, 0, 0, 0, 100,
            false, false, 0, 0, 0, 47, 1, 4200, 1200, false, false);
        Assert.False(VanillaNpcGlobalLoot1458.TryEvaluateRule(index, in context, new RejectDraw(), out _, out _));
    }

    [Fact]
    public void Missing_friendly_fact_rejects_the_otherwise_eligible_living_fire_offer_before_luck()
    {
        var context = new VanillaNpcGlobalLootContext1458(VanillaNpcIds.SandSlime, 25, 10, 5, 100,
            null, false, 0, 16000, 16001, 47, 1, 4200, 1200, true, false);
        Assert.False(VanillaNpcGlobalLoot1458.TryGetEligibility((int)VanillaNpcGlobalLootRule1458.LivingFire, in context, out _));
        Assert.False(VanillaNpcGlobalLoot1458.TryEvaluateRule((int)VanillaNpcGlobalLootRule1458.LivingFire,
            in context, new RejectDraw(), out _, out _));
    }

    private sealed class RejectDraw : INpcLootRollSource
    {
        public int RollLuck(int denominator) => throw new InvalidOperationException("Rejected context consumed luck");
        public int NextInt32(int minimum, int maximum) => throw new InvalidOperationException("Rejected context consumed stack RNG");
    }
}
