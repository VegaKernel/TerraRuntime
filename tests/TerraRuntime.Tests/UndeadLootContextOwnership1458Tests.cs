using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class UndeadLootContextOwnership1458Tests
{
    public static IEnumerable<object[]> LoadedHardStatue()
    {
        using var stream = typeof(UndeadLootContextOwnership1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.npc-loaded-hard-statue-global-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(LoadedHardStatue))]
    public void Source_loaded_hardmode_reaches_the_known_first_global_after_the_statue_gate(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var npcs = new RuntimeNpcStore(); var items = new RuntimeWorldItemStore();
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var player = default(PlayerStateSnapshot) with
        {
            Player = new(new(0), new(1)), Revision = new(1),
            PositionX = row.GetProperty("x").GetSingle(), PositionY = row.GetProperty("y").GetSingle(),
            HasHealth = true, Life = 400, MaxLife = 400, DerivedLifeMax = 400, NpcLifeCurrent = true,
            HasMana = true, Mana = 200, MaxMana = 200, Zones = default(PlayerZoneSnapshot1458)
        };
        short type = row.GetProperty("type").GetInt16(); bool hard = row.GetProperty("hard").GetBoolean();
        var simulation = NpcSimulationState.Initial with
        {
            Life = row.GetProperty("lifeMax").GetInt32(), LifeMax = row.GetProperty("lifeMax").GetInt32(),
            DamageOverride = row.GetProperty("damage").GetInt32(), DefenseOverride = row.GetProperty("defense").GetInt32(),
            MoneyValue = row.GetProperty("value").GetSingle(), ExtraMoneyValue = 0,
            SpawnedFromStatue = true, HitboxOverride = new(row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32())
        };
        Assert.True(npcs.TrySpawnVanilla(new(type, type, player.PositionX, player.PositionY, 0, 0, 0, default, simulation), out var npc));
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, new FixedPlayer(player), new PlayerAuthority(null, null),
            () => 0, null, new(items), null, null, new(), false, false, lootRandom: random,
            seasonalItemContext: () => new(true, false, false), mechanicalLootBaseline: new(0, hard, true, true, true),
            globalLootWorld: new(4200, 1200, 1f, hard, false, true, false, 200d, 140d, false, true),
            onlyShimmerOceanWorlds: false, requireOwnedPlayerHealth: true);
        Assert.True(pipeline.Interactions.TryMark(npc.Handle, player.Player));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, pipeline.TryStrikeEnvironment(npc.Handle, 100_000));
        var active = new WorldItemSnapshot[items.Capacity]; int count = items.CopyActive(active);
        // Only this independently captured first global is owned here. NPC82's individual
        // table and its subsequent full RNG stream remain outside this fixture's claim.
        Assert.Equal(row.GetProperty("expectedGoodieCount").GetInt32(),
            active.Take(count).Count(item => item.ItemNetId == VanillaGlobalNpcDropItemIds.GoodieBag.Value));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Source_selected_heart_requires_a_report_current_at_the_npc_phase(bool playerPhase) =>
        UndeadLootDeath1458Tests.AssertCurrentLifeBoundary(playerPhase);

    public static IEnumerable<object[]> OriginalBoundaries()
    {
        foreach (var data in UndeadLootDeath1458Tests.OriginalAdmittedDeaths())
        {
            using var document = JsonDocument.Parse((string)data[0]);
            var row = document.RootElement; var profile = row.GetProperty("profile");
            if (profile.GetProperty("Name").GetString()!.StartsWith("loaded-", StringComparison.Ordinal) ||
                (profile.GetProperty("ExtraGel").GetBoolean() && row.GetProperty("seed").GetInt32() == 1458 &&
                    row.GetProperty("luck").GetSingle() == 0 && !row.GetProperty("injured").GetBoolean()))
                yield return data;
        }
    }

    [Theory, MemberData(nameof(OriginalBoundaries))]
    public void Source_selected_saved_world_and_extra_gel_contexts_reach_the_live_death_owner(string json) =>
        UndeadLootDeath1458Tests.AssertOriginalDeath(json);

    private sealed class FixedPlayer(PlayerStateSnapshot player) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = player;
            return slot.Value == 0;
        }
    }
}
