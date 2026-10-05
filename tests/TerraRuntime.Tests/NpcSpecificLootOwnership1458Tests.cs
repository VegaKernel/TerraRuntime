using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class NpcSpecificLootOwnership1458Tests
{
    [Theory]
    [InlineData(3)] [InlineData(591)] [InlineData(331)] [InlineData(332)]
    [InlineData(132)] [InlineData(161)] [InlineData(186)] [InlineData(187)]
    [InlineData(188)] [InlineData(189)] [InlineData(200)] [InlineData(223)]
    [InlineData(319)] [InlineData(320)] [InlineData(321)] [InlineData(430)]
    [InlineData(431)] [InlineData(432)] [InlineData(433)] [InlineData(434)]
    [InlineData(435)] [InlineData(436)]
    public void Every_admitted_zombie_captures_the_closest_players_sickle(int type)
    {
        using var f = new Fixture(type);
        f.Equipment(0, VanillaNpcSpecificDropItemIds.Sickle.Value);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
            f.Pipeline(() => type is 188 or 189 or 434 or 435 ? true : null)
                .TryStrikeEnvironment(f.Npc.Handle, 100_000));
    }

    [Theory]
    [InlineData(188)] [InlineData(189)] [InlineData(434)] [InlineData(435)]
    public void Wood_predicate_requires_owned_low_tiles_even_when_sickle_proves_no_sickle_offer(int type)
    {
        using var f = new Fixture(type);
        f.Equipment(0, VanillaNpcSpecificDropItemIds.Sickle.Value);
        var before = f.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            f.Pipeline(() => null).TryStrikeEnvironment(f.Npc.Handle, 100_000));
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var after));
        Assert.Equal(f.Npc, after);
        Assert.True(before.HasSameState(f.Random));
        Assert.Equal(0, f.Items.ActiveCount);
    }

    [Theory]
    [InlineData(null, false, false, false)]
    [InlineData(null, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, true, true)]
    public void Actual_lethal_transaction_admits_only_known_sickle_predicates(
        bool? lowTiles, bool sickle, bool openBag, bool admitted)
    {
        using var f = new Fixture();
        if (sickle) f.Equipment(0, VanillaNpcSpecificDropItemIds.Sickle.Value);
        if (openBag) f.Equipment(1, VanillaNpcLootPredicateItemIds1458.OpenVoidBag.Value);
        var pipeline = f.Pipeline(() => lowTiles);
        var before = f.Random.Clone();
        var result = pipeline.TryStrikeEnvironment(f.Npc.Handle, 100_000);
        Assert.Equal(admitted ? RuntimeTownNpcMeleeDamageResult1458.Killed : RuntimeTownNpcMeleeDamageResult1458.Rejected, result);
        if (!admitted)
        {
            Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var after)); Assert.Equal(f.Npc, after);
            Assert.True(before.HasSameState(f.Random)); Assert.Equal(0, f.Items.ActiveCount);
        }
    }

    [Fact]
    public void Positive_open_void_bag_sickle_suppresses_offer_even_when_other_storage_is_unknown()
    {
        using var f = new Fixture();
        f.Equipment(0, VanillaNpcLootPredicateItemIds1458.OpenVoidBag.Value);
        f.Equipment(VanillaPlayerItemSlotCatalog.Bank4Start, VanillaNpcSpecificDropItemIds.Sickle.Value);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
            f.Pipeline(() => true).TryStrikeEnvironment(f.Npc.Handle, 100_000));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Reentrant_low_tiles_or_inventory_aba_rejects_before_damage_and_live_random(bool inventoryAba)
    {
        using var f = new Fixture();
        int reads = 0;
        bool? Read()
        {
            reads++;
            if (reads == 2 && inventoryAba)
            {
                f.Equipment(0, VanillaNpcSpecificDropItemIds.Sickle.Value);
                f.Equipment(0, 0);
            }
            return inventoryAba || reads == 1;
        }
        var pipeline = f.Pipeline(Read); var before = f.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, pipeline.TryStrikeEnvironment(f.Npc.Handle, 100_000));
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var after)); Assert.Equal(f.Npc, after);
        Assert.True(before.HasSameState(f.Random)); Assert.Equal(0, f.Items.ActiveCount);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly PlayerAuthority Players = new(null, null);
        internal readonly PlayerJoinSession Session;
        internal readonly ConnectionHandle Connection;
        internal readonly NpcSnapshot Npc;

        internal Fixture(int type = 3)
        {
            var pool = new PlayerSlotPool(1); Assert.True(pool.TryAcquireConnection(out var lease));
            Session = new(lease!); Session.ObserveWorldRequest(); Session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(9090), Session.Handle);
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, Session, new(Session.Slot, 40, 30, 0, 0, 0, 0, 0)));
            Assert.True(Npcs.TrySpawnVanilla(new(checked((short)type), checked((short)type), 600, 400, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = 100, LifeMax = 100, MoneyValue = 0f }), out Npc));
        }

        internal void Equipment(short slot, int type) => Players.TryApply(new PlayerEquipmentRuntimeCommand(
            Connection, new(Session.Slot, slot, type == 0 ? (short)0 : (short)1, 0, checked((short)type), 0)));

        internal RuntimeNpcNetworkCombatPipeline Pipeline(Func<bool?> lowTiles) => new(
            Npcs, Items, new RuntimePlayerSnapshotLookup(Players, null), Players, () => 0, null,
            new(Items), null, null, new(), false, false, lootRandom: Random, npcSpecificLowTiles: lowTiles);

        public void Dispose() => Session.Dispose();
    }
}
