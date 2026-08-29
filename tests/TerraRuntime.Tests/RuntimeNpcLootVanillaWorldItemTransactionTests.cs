using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcLootVanillaWorldItemTransactionTests
{
    [Fact]
    public void Blue_slime_streams_each_successful_rule_into_ItemNewItem_rng_before_next_rule()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        NpcSnapshot slime = Spawn(npcs, 10.9f, 20.9f);
        Kill(npcs, slime.Handle);
        var rolls = new ScriptedRollSource(
            luckResults: new[] { 0, 0 },
            randomResults: new[]
            {
                2,          // Gel stack.
                5, -20,     // Gel velocity.
                1,          // Slime Staff stack.
                1, 0,       // Prefix(-1): avoid no-prefix, choose summon prefix 85.
                -5, -30     // Slime Staff velocity.
            });
        var transaction = new RuntimeNpcLootWorldItemTransaction(npcs, items);
        Span<WorldItemSnapshot> spawned = stackalloc WorldItemSnapshot[2];

        Assert.True(transaction.TryFinalizeAndSpawn(
            slime.Handle,
            default,
            rolls,
            VanillaNpcLootWorldItemMaterializer.Instance,
            spawned,
            out NpcLootWorldItemTransactionResult result));

        Assert.Equal(2, result.SpawnedItemCount);
        Assert.False(npcs.TryGet(slime.Handle, out _));
        Assert.Equal(2, items.ActiveCount);

        Assert.True(spawned[0].TryGetItemType(out ItemTypeId firstType));
        Assert.Equal(VanillaItemIds.Gel, firstType);
        Assert.Equal(17f, spawned[0].PositionX);
        Assert.Equal(23f, spawned[0].PositionY);
        Assert.Equal(0.5f, spawned[0].VelocityX);
        Assert.Equal(-2f, spawned[0].VelocityY);
        Assert.Equal((byte)0, spawned[0].Prefix);

        Assert.True(spawned[1].TryGetItemType(out ItemTypeId secondType));
        Assert.Equal(VanillaItemIds.SlimeStaff, secondType);
        Assert.Equal(9f, spawned[1].PositionX);
        Assert.Equal(15f, spawned[1].PositionY);
        Assert.Equal(-0.5f, spawned[1].VelocityX);
        Assert.Equal(-3f, spawned[1].VelocityY);
        Assert.Equal((byte)85, spawned[1].Prefix);

        Assert.Equal(
            new[]
            {
                "luck:1",
                "rng:1:3",
                "rng:-30:31",
                "rng:-40:-15",
                "luck:10000",
                "rng:1:2",
                "rng:0:4",
                "rng:0:22",
                "rng:-30:31",
                "rng:-40:-15"
            },
            rolls.Calls);
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

    private sealed class ScriptedRollSource(
        IEnumerable<int> luckResults,
        IEnumerable<int> randomResults) : INpcLootRollSource
    {
        private readonly Queue<int> _luckResults = new(luckResults);
        private readonly Queue<int> _randomResults = new(randomResults);

        public List<string> Calls { get; } = [];

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
