using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MechSpawnersDeath1458Tests
{
    public static IEnumerable<object[]> OriginalDeaths()
    {
        using var stream = typeof(MechSpawnersDeath1458Tests).Assembly.GetManifestResourceStream("MechSpawnersCoupled1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            yield return [row.GetRawText(), false];
            yield return [row.GetRawText(), true];
        }
    }

    [Theory, MemberData(nameof(OriginalDeaths))]
    public void Actual_lethal_commit_matches_original_global_contents_specific_money_healing_and_rng(string json, bool journal)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        int mask = row.GetProperty("mask").GetInt32(); bool hard = row.GetProperty("hard").GetBoolean();
        var baseline = journal ? default : new VanillaMechBossSpawnersContext1458(0f, hard,
            (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
        var fixture = new SlimeContainedDeath1458Tests.Fixture(row.GetProperty("seed").GetInt32(), baseline);
        if (journal)
        {
            if (hard) fixture.Progression.MarkCompleted(VanillaWorldProgressionId.Hardmode);
            if ((mask & 1) != 0) fixture.Progression.MarkCompleted(VanillaWorldProgressionId.Destroyer);
            if ((mask & 2) != 0) fixture.Progression.MarkCompleted(VanillaWorldProgressionId.Twins);
            if ((mask & 4) != 0) fixture.Progression.MarkCompleted(VanillaWorldProgressionId.SkeletronPrime);
        }
        int body = row.GetProperty("body").GetInt32();
        var npc = fixture.Spawn(1, 2, body == 0 ? 24 : 47, body == 0 ? 18 : 35,
            row.GetProperty("positive").GetBoolean() ? 25f : 0f);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, fixture.Hit(npc));
        var items = fixture.Items(); var expected = row.GetProperty("fullDrops").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            var actual = items[i]; var original = expected[i];
            Assert.Equal(original.GetProperty("id").GetInt32(), actual.ItemNetId);
            Assert.Equal(original.GetProperty("stack").GetInt32(), actual.Stack);
            Assert.Equal(original.GetProperty("prefix").GetInt32(), actual.Prefix);
            Assert.Equal(original.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(original.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(original.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(original.GetProperty("vy").GetSingle(), actual.VelocityY);
        }
        Assert.Equal(row.GetProperty("fullNext").GetInt32(), fixture.Random.Next());
        var after = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, fixture.Hit(npc));
        Assert.True(after.HasSameState(fixture.Random));
    }

    [Fact]
    public void Progression_changed_during_preview_rejects_before_damage_items_and_live_rng()
    {
        var fixture = new SlimeContainedDeath1458Tests.Fixture(1458, new(0f, true, false, false, false));
        var npc = fixture.Spawn(1, value: 25f); var before = fixture.Random.Clone();
        var lookup = new ChangingLookup(fixture);
        var pipeline = new RuntimeNpcNetworkCombatPipeline(fixture.Npcs, fixture.Store, lookup, new PlayerAuthority(null, null),
            static () => 0, null, new(fixture.Store), null, fixture.Clock, fixture.Progression, false, false,
            lootRandom: fixture.Random, mechanicalLootBaseline: new(0f, true, false, false, false));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, pipeline.TryStrikeEnvironment(npc.Handle, 100_000));
        Assert.True(lookup.Changed);
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var retained)); Assert.Equal(npc, retained);
        Assert.True(before.HasSameState(fixture.Random)); Assert.Empty(fixture.Items());
    }

    [Fact]
    public void Held_allocation_preserves_successful_mechanical_roll_for_retry()
    {
        // Independently captured successful mechanical offer seed, selected from the original fixture.
        using var stream = typeof(MechSpawnersDeath1458Tests).Assembly.GetManifestResourceStream("MechSpawnersCoupled1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress); using var document = JsonDocument.Parse(gzip);
        var row = document.RootElement.EnumerateArray().First(x => x.GetProperty("hard").GetBoolean() &&
            x.GetProperty("positive").GetBoolean() && x.GetProperty("mask").GetInt32() == 0 &&
            x.GetProperty("fullDrops")[0].GetProperty("id").GetInt32() is 544 or 556 or 557);
        var fixture = new SlimeContainedDeath1458Tests.Fixture(row.GetProperty("seed").GetInt32(), new(0f, true, false, false, false));
        var npc = fixture.Spawn(1, value: 25f); var before = fixture.Random.Clone();
        Assert.True(fixture.Store.TryReserveDropSlot(out var held));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, fixture.Hit(npc));
        Assert.True(before.HasSameState(fixture.Random)); Assert.Empty(fixture.Items());
        Assert.True(fixture.Store.TryReleaseDropReservation(in held));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, fixture.Hit(npc));
        Assert.Equal(row.GetProperty("fullDrops")[0].GetProperty("id").GetInt32(), fixture.Items()[0].ItemNetId);
    }

    private sealed class ChangingLookup(SlimeContainedDeath1458Tests.Fixture fixture) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool Changed { get; private set; }
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            if (!Changed && slot.Value == 0)
            { fixture.Progression.MarkCompleted(VanillaWorldProgressionId.Destroyer); Changed = true; }
            return fixture.TryGetPlayer(slot, out snapshot);
        }
    }
}
