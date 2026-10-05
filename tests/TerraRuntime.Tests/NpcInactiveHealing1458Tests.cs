using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;

namespace TerraRuntime.Tests;

public sealed class NpcInactiveHealing1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(NpcInactiveHealing1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.inactive-player-constructor-healing-tenth-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_FindClosest_and_constructor_healing_preserve_no_drops_and_next_rng(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Assert.Equal(0, row.GetProperty("closest").GetInt32());
        Assert.Equal(0, row.GetProperty("activePlayers").GetInt32());
        Assert.False(row.GetProperty("active").GetBoolean());
        Assert.Equal(100, row.GetProperty("life").GetInt32());
        Assert.Equal(100, row.GetProperty("derivedMaxLife").GetInt32());
        Assert.Equal(0, row.GetProperty("derivedMaxMana").GetInt32());
        int type = row.GetProperty("type").GetInt32();
        var context = new VanillaNpcHealingContext1458(new(type), new(type),
            row.GetProperty("npcLifeMax").GetInt32(), row.GetProperty("npcDamage").GetInt32(),
            false, false, false);
        var random = new Rolls(row.GetProperty("seed").GetInt32()); var sink = new Sink();
        var origin = new NpcLootWorldItemOrigin(1000, 1000);
        Assert.True(VanillaNpcHealingLoot1458.TryExecute(in context, in origin, random, sink));
        Assert.Equal(row.GetProperty("drops").GetArrayLength(), sink.Drops);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Random.Next());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Concrete_no_player_owner_admits_constructor_and_rejects_raw_slot_aba(bool reenter)
    {
        var players = new PlayerAuthority(null, null);
        var lookup = new RuntimePlayerSnapshotLookup(players, null);
        var raw = new RuntimeNpcRawPlayerSlots1458(lookup, () => players.MembershipSerial);
        players.BindNpcRawPlayerSlots(raw);
        var npcs = new RuntimeNpcStore(); var items = new RuntimeWorldItemStore();
        Assert.True(npcs.TrySpawnVanilla(new(3, 3, 600, 400, 0, 0, 0, default,
            NpcSimulationState.Initial with { MoneyValue = 0f }), out var npc));
        var random = new VanillaUnifiedRandom1458(1458); var before = random.Clone(); int reads = 0;
        bool? LowTiles()
        {
            if (++reads == 2 && reenter)
            {
                var handle = new PlayerHandle(new(0), new(1));
                Assert.True(raw.TryAttach(handle)); Assert.True(raw.TryReset(handle));
            }
            return false;
        }
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, lookup, players, () => 0, null,
            new(items), null, null, new(), false, false, lootRandom: random,
            npcSpecificLowTiles: LowTiles, requireOwnedPlayerHealth: true, rawPlayerSlots: raw);
        Assert.Equal(reenter ? RuntimeTownNpcMeleeDamageResult1458.Rejected : RuntimeTownNpcMeleeDamageResult1458.Killed,
            pipeline.TryStrikeEnvironment(npc.Handle, 100_000));
        if (reenter)
        {
            Assert.True(npcs.TryGet(npc.Handle, out var retained)); Assert.Equal(npc, retained);
            Assert.True(before.HasSameState(random)); Assert.Equal(0, items.ActiveCount);
        }
    }

    private sealed class Rolls(int seed) : INpcLootRollSource
    {
        internal readonly VanillaUnifiedRandom1458 Random = new(seed);
        public int RollLuck(int denominator) => Random.Next(denominator);
        public int NextInt32(int minimum, int maximum) => Random.Next(minimum, maximum);
    }
    private sealed class Sink : IBossRecoveryLootDeliverySink1458
    {
        internal int Drops;
        public bool CanDeliverWorldItem(ItemTypeId type) => true;
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        { Drops++; return true; }
    }
}
