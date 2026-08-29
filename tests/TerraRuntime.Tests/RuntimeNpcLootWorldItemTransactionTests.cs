using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcLootWorldItemTransactionTests
{
    [Fact]
    public void Dead_blue_slime_spawns_rolled_items_and_despawns_exact_generation()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        NpcSnapshot slime = Spawn(npcs, 10.9f, 20.9f);
        Kill(npcs, slime.Handle);
        var rolls = new ScriptedRollSource(
            luckResults: new[] { 0, 1 },
            randomResults: new[] { 2 });
        var materializer = new FakeMaterializer(VanillaItemIds.Gel, VanillaItemIds.SlimeStaff);
        var transaction = new RuntimeNpcLootWorldItemTransaction(npcs, items);
        Span<WorldItemSnapshot> spawned = stackalloc WorldItemSnapshot[2];

        Assert.True(transaction.TryFinalizeAndSpawn(
            slime.Handle,
            default,
            rolls,
            materializer,
            spawned,
            out NpcLootWorldItemTransactionResult result));

        Assert.True(result.IsValid);
        Assert.Equal(slime.Handle, result.Target);
        Assert.Equal(new NpcRevision(2), result.FinalRevision);
        Assert.Equal(VanillaNpcIds.BlueSlime, result.Type);
        Assert.Equal(22f, result.Origin.CenterX);
        Assert.Equal(29f, result.Origin.CenterY);
        Assert.Equal(1, result.SpawnedItemCount);
        Assert.False(npcs.TryGet(slime.Handle, out _));
        Assert.Equal(1, items.ActiveCount);
        Assert.True(spawned[0].TryGetItemType(out ItemTypeId itemType));
        Assert.Equal(VanillaItemIds.Gel, itemType);
        Assert.Equal((short)2, spawned[0].Stack);
        Assert.Equal(new[] { VanillaItemIds.Gel }, materializer.MaterializedTypes);
        Assert.Equal(new[] { "luck:1", "rng:1:3", "luck:10000" }, rolls.Calls);
    }

    [Fact]
    public void Successful_transaction_is_exactly_once_for_npc_generation()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        NpcSnapshot slime = Spawn(npcs, 0f, 0f);
        Kill(npcs, slime.Handle);
        var rolls = new ScriptedRollSource(
            luckResults: new[] { 0, 0 },
            randomResults: new[] { 1, 1 });
        var materializer = new FakeMaterializer(VanillaItemIds.Gel, VanillaItemIds.SlimeStaff);
        var transaction = new RuntimeNpcLootWorldItemTransaction(npcs, items);
        Span<WorldItemSnapshot> spawned = stackalloc WorldItemSnapshot[2];

        Assert.True(transaction.TryFinalizeAndSpawn(
            slime.Handle,
            default,
            rolls,
            materializer,
            spawned,
            out NpcLootWorldItemTransactionResult first));
        Assert.Equal(2, first.SpawnedItemCount);
        Assert.Equal(2, items.ActiveCount);

        var secondRolls = ScriptedRollSource.Empty();
        Assert.False(transaction.TryFinalizeAndSpawn(
            slime.Handle,
            default,
            secondRolls,
            materializer,
            spawned,
            out _));
        Assert.Empty(secondRolls.Calls);
        Assert.Equal(2, items.ActiveCount);
    }

    [Fact]
    public void Unsupported_potential_drop_fails_before_capacity_or_rng()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        NpcSnapshot slime = Spawn(npcs, 0f, 0f);
        Kill(npcs, slime.Handle);
        var rolls = ScriptedRollSource.Empty();
        var materializer = new FakeMaterializer(VanillaItemIds.Gel);
        var transaction = new RuntimeNpcLootWorldItemTransaction(npcs, items);
        Span<WorldItemSnapshot> spawned = stackalloc WorldItemSnapshot[2];

        Assert.False(transaction.TryFinalizeAndSpawn(
            slime.Handle,
            default,
            rolls,
            materializer,
            spawned,
            out _));

        Assert.Empty(rolls.Calls);
        Assert.Empty(materializer.MaterializedTypes);
        Assert.Equal(0, items.ActiveCount);
        Assert.True(npcs.TryGet(slime.Handle, out NpcSnapshot dead));
        Assert.Equal(0, dead.Simulation.Life);
    }

    [Fact]
    public void Insufficient_world_item_capacity_fails_before_loot_rng_and_preserves_npc()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        for (int index = 0; index < RuntimeWorldItemStore.VanillaCapacity - 1; index++)
        {
            WorldItemDropStateUpdate filler = CreateDrop(
                VanillaItemIds.DirtBlock,
                stack: 1,
                positionX: index,
                positionY: 0f);
            Assert.True(items.TryAllocateDrop(in filler, out _));
        }

        NpcSnapshot slime = Spawn(npcs, 0f, 0f);
        Kill(npcs, slime.Handle);
        var rolls = ScriptedRollSource.Empty();
        var materializer = new FakeMaterializer(VanillaItemIds.Gel, VanillaItemIds.SlimeStaff);
        var transaction = new RuntimeNpcLootWorldItemTransaction(npcs, items);
        Span<WorldItemSnapshot> spawned = stackalloc WorldItemSnapshot[2];

        Assert.False(transaction.TryFinalizeAndSpawn(
            slime.Handle,
            default,
            rolls,
            materializer,
            spawned,
            out _));

        Assert.Empty(rolls.Calls);
        Assert.Empty(materializer.MaterializedTypes);
        Assert.Equal(RuntimeWorldItemStore.VanillaCapacity - 1, items.ActiveCount);
        Assert.True(npcs.TryGet(slime.Handle, out NpcSnapshot dead));
        Assert.Equal(0, dead.Simulation.Life);

        Assert.True(items.TryReserveDropSlot(out WorldItemDropReservation remainingCapacity));
        Assert.True(items.TryReleaseDropReservation(in remainingCapacity));
    }

    [Fact]
    public void Live_or_stale_npc_fails_before_materializer_and_rng()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        NpcSnapshot live = Spawn(npcs, 0f, 0f);
        var materializer = new FakeMaterializer(VanillaItemIds.Gel, VanillaItemIds.SlimeStaff);
        var transaction = new RuntimeNpcLootWorldItemTransaction(npcs, items);
        Span<WorldItemSnapshot> spawned = stackalloc WorldItemSnapshot[2];
        var liveRolls = ScriptedRollSource.Empty();

        Assert.False(transaction.TryFinalizeAndSpawn(
            live.Handle,
            default,
            liveRolls,
            materializer,
            spawned,
            out _));
        Assert.Empty(liveRolls.Calls);

        Assert.True(npcs.TryDespawn(live.Handle));
        NpcSnapshot replacement = Spawn(npcs, 0f, 0f);
        Kill(npcs, replacement.Handle);
        var staleRolls = ScriptedRollSource.Empty();

        Assert.False(transaction.TryFinalizeAndSpawn(
            live.Handle,
            default,
            staleRolls,
            materializer,
            spawned,
            out _));
        Assert.Empty(staleRolls.Calls);
        Assert.True(npcs.TryGet(replacement.Handle, out NpcSnapshot dead));
        Assert.Equal(0, dead.Simulation.Life);
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore store, float positionX, float positionY)
    {
        var update = new NpcStateUpdate(
            Type: VanillaNpcIds.BlueSlime.Value,
            NetId: checked((short)VanillaNpcIds.BlueSlime.Value),
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(slot: 0, in update, out NpcSnapshot snapshot));
        return snapshot;
    }

    private static void Kill(RuntimeNpcStore store, NpcHandle handle)
    {
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(handle, DamageSource.Environment, BaseDamage: 1000);
        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.True(result.Lethal);
    }

    private static WorldItemDropStateUpdate CreateDrop(
        ItemTypeId itemType,
        short stack,
        float positionX,
        float positionY) =>
        new(
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: stack,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: checked((short)itemType.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);

    private sealed class FakeMaterializer(params ItemTypeId[] supported) : INpcLootWorldItemMaterializer
    {
        private readonly HashSet<ItemTypeId> _supported = new(supported);

        public List<ItemTypeId> MaterializedTypes { get; } = new();

        public bool CanMaterialize(ItemTypeId itemType) => _supported.Contains(itemType);

        public bool TryMaterialize(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            INpcLootRollSource random,
            out WorldItemDropStateUpdate worldItem)
        {
            if (!CanMaterialize(drop.ItemType))
            {
                worldItem = default;
                return false;
            }

            MaterializedTypes.Add(drop.ItemType);
            worldItem = CreateDrop(
                drop.ItemType,
                drop.Stack,
                positionX: origin.CenterX - 1f,
                positionY: origin.CenterY - 1f);
            return true;
        }
    }

    private sealed class ScriptedRollSource(
        IEnumerable<int> luckResults,
        IEnumerable<int> randomResults) : INpcLootRollSource
    {
        private readonly Queue<int> _luckResults = new(luckResults);
        private readonly Queue<int> _randomResults = new(randomResults);

        public List<string> Calls { get; } = new();

        public static ScriptedRollSource Empty() =>
            new(Array.Empty<int>(), Array.Empty<int>());

        public int RollLuck(int chanceDenominator)
        {
            Calls.Add($"luck:{chanceDenominator}");
            return _luckResults.Dequeue();
        }

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Calls.Add($"rng:{inclusiveMin}:{exclusiveMax}");
            int value = _randomResults.Dequeue();
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
