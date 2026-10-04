using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcDeathPrelude1458Tests
{
    [Theory]
    [MemberData(nameof(CreditRows))]
    public void Every_original_net_identity_retains_source_credit_and_banner(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        if (row.GetProperty("type").GetInt32() == 0)
        { Assert.Throws<ArgumentOutOfRangeException>(() => new NpcTypeId(0)); return; }
        Assert.True(VanillaNpcDeathPreludeCatalog1458.TryGet(new(row.GetProperty("type").GetInt32()),
            new(row.GetProperty("netId").GetInt32()), out var facts));
        Assert.Equal(row.GetProperty("banner").GetInt32(), facts.BannerId);
        Assert.Equal(row.GetProperty("item").GetInt32(), facts.BannerItemId);
        Assert.Equal(row.GetProperty("threshold").GetInt32(), facts.KillsToBanner);
        Assert.Equal(row.GetProperty("excluded").GetBoolean(), facts.ExcludedFromTally);
        Assert.Equal(row.GetProperty("credit").GetString(), facts.BestiaryCreditId);
    }

    [Theory]
    [MemberData(nameof(DefaultRows))]
    public void Every_original_statue_default_is_preserved(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        if (row.GetProperty("type").GetInt32() == 0)
        { Assert.Throws<ArgumentOutOfRangeException>(() => new NpcTypeId(0)); return; }
        Assert.True(VanillaNpcDeathPreludeCatalog1458.TryGet(new(row.GetProperty("type").GetInt32()),
            new(row.GetProperty("netId").GetInt32()), out var facts));
        Assert.Equal(row.GetProperty("statueRarity").GetSingle(), facts.StatueDropRarity);
        Assert.Equal(row.GetProperty("noEarlyStatueLoot").GetBoolean(), facts.NoEarlyStatueLoot);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Statue_credit_commits_before_suppression_and_own_interactions_are_distinct(bool ownInteraction)
    {
        var owner = new RuntimeNpcDeathPrelude1458(); var preview = owner.CreatePreview();
        var npc = Dead(3, true); var random = new VanillaUnifiedRandom1458(14); var expected = random.Clone();
        expected.NextDouble();
        var context = Context(true) with { HasOwnInteractions = ownInteraction };
        Assert.True(preview.TryApply(npc, context, random, out bool allow));
        Assert.Equal(ownInteraction, allow); Assert.True(random.HasSameState(expected));
        Assert.Empty(owner.CaptureBestiary().Kills);
        Assert.True(owner.TryPublish(preview, owner.Revision));
        Assert.Single(owner.CaptureBestiary().Kills);
        Assert.Equal(1, owner.CaptureBanners().KillCounts.Sum());
        Assert.False(owner.TryPublish(preview, 1));
        Assert.False(preview.TryApply(npc, context, random, out _));
    }

    [Fact]
    public void Earlymode_statue_guard_short_circuits_random_but_retains_credit()
    {
        var owner = new RuntimeNpcDeathPrelude1458(); var preview = owner.CreatePreview();
        var random = new VanillaUnifiedRandom1458(42); var expected = random.Clone();
        Assert.True(preview.TryApply(Dead(82, true), Context(true), random, out bool allow));
        Assert.False(allow); Assert.True(random.HasSameState(expected));
        Assert.True(owner.TryPublish(preview, owner.Revision)); Assert.Single(owner.CaptureBestiary().Kills);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Dungeon_gate_precedes_all_credit_and_unknown_world_fact_rejects(bool known)
    {
        var owner = new RuntimeNpcDeathPrelude1458(); var random = new VanillaUnifiedRandom1458(42);
        var expected = random.Clone(); var context = Context(true) with { GoodWorld = true, OnlyShimmerOceanWorlds = known ? false : null };
        Assert.Equal(known, owner.TryApply(Dead(31), context, random, out bool allow));
        Assert.False(allow); Assert.Empty(owner.CaptureBestiary().Kills);
        Assert.Equal(0, owner.CaptureBanners().KillCounts.Sum()); Assert.True(random.HasSameState(expected));
    }

    [Fact]
    public void Source_threshold_creates_claimable_counter_and_never_a_world_item()
    {
        Assert.True(VanillaNpcDeathPreludeCatalog1458.TryGet(new(1), new(1), out var facts));
        var kills = new int[293]; kills[facts.BannerId] = facts.KillsToBanner - 1;
        var owner = new RuntimeNpcDeathPrelude1458(new(kills, new ushort[293]));
        var preview = owner.CreatePreview();
        Assert.True(preview.TryApply(Dead(1), Context(true), new(42), out bool allow)); Assert.True(allow);
        Assert.True(owner.TryPublish(preview, owner.Revision));
        Assert.Equal(facts.KillsToBanner, owner.CaptureBanners().KillCounts[facts.BannerId]);
        Assert.Equal(1, owner.CaptureBanners().ClaimableCounts[facts.BannerId]);
        Assert.True(owner.CaptureJoinFrames().Length >= 2);
    }

    public static IEnumerable<object[]> CreditRows() => Rows("credits");
    public static IEnumerable<object[]> DefaultRows() => Rows("facts");
    public static IEnumerable<object[]> TallyRows() => Rows("tally-behavior");
    public static IEnumerable<object[]> SuppressedDeaths() => Rows("suppressed-deaths");
    [Theory]
    [MemberData(nameof(SuppressedDeaths))]
    public void Whole_original_NPCLoot_suppressed_statue_deaths_match_credit_and_next_random(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var owner = new RuntimeNpcDeathPrelude1458(); var preview = owner.CreatePreview(); var random = new VanillaUnifiedRandom1458(42);
        var context = Context(row.GetProperty("rootInteraction").GetBoolean()) with
        { Hardmode = row.GetProperty("hard").GetBoolean(), HasOwnInteractions = row.GetProperty("own").GetBoolean() };
        Assert.True(preview.TryApply(Dead(row.GetProperty("type").GetInt32(), true), context, random, out bool allow));
        Assert.False(allow); Assert.True(owner.TryPublish(preview, owner.Revision));
        Assert.Equal(row.GetProperty("bannerKills").GetInt32(), owner.CaptureBanners().KillCounts[row.GetProperty("banner").GetInt32()]);
        Assert.Equal(row.GetProperty("bestiaryKills").GetInt32(), owner.CaptureBestiary().Kills.Sum(static entry => entry.KillCount));
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }
    [Theory]
    [MemberData(nameof(TallyRows))]
    public void Threshold_saturation_and_integer_overflow_match_original_AddNPCKillBy(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        int banner = row.GetProperty("banner").GetInt32(); int type = row.GetProperty("sourceNpc").GetInt32();
        var kills = new int[293]; var claims = new ushort[293];
        kills[banner] = row.GetProperty("initialKills").GetInt32(); claims[banner] = row.GetProperty("initialClaims").GetUInt16();
        var owner = new RuntimeNpcDeathPrelude1458(new(kills, claims)); var preview = owner.CreatePreview();
        Assert.True(preview.TryApply(Dead(type == 0 ? 4 : type), Context(true), new(42), out _));
        Assert.True(owner.TryPublish(preview, owner.Revision));
        Assert.Equal(row.GetProperty("kills").GetInt32(), owner.CaptureBanners().KillCounts[banner]);
        Assert.Equal(row.GetProperty("claims").GetUInt16(), owner.CaptureBanners().ClaimableCounts[banner]);
    }
    internal static IEnumerable<object[]> Rows(string name)
    {
        using var resource = typeof(NpcDeathPrelude1458Tests).Assembly.GetManifestResourceStream(
            $"TerraRuntime.Tests.Fixtures.npc-death-prelude-{name}-official.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }
    private static NpcSnapshot Dead(int type, bool statue = false) => new(new(0, new(1)), new(1), type,
        checked((short)type), 100, 100, 0, 0, 0, default, NpcSimulationState.Initial with { SpawnedFromStatue = statue });
    private static RuntimeNpcDeathPreludeContext1458 Context(bool interactions) =>
        new(interactions, false, false, false, false, true, false, false, default, HasOwnInteractions: interactions);
}
