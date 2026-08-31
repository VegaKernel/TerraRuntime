using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaKingSlimeNormalLootTests
{
    [Fact]
    public void Catalog_matches_source_order_and_branch_capacity()
    {
        Assert.True(VanillaKingSlimeNormalLootCatalog.IsValid);
        Assert.Equal(7, VanillaKingSlimeNormalLootCatalog.MaximumDropCount);
        ReadOnlySpan<VanillaKingSlimeNormalLootRule> rules = VanillaKingSlimeNormalLootCatalog.Rules;
        Assert.Equal(7, rules.Length);
        Assert.Equal(VanillaKingSlimeItemIds.KingSlimeTrophy, rules[0].PrimaryItem);
        Assert.Equal(10, rules[0].ChanceDenominator);
        Assert.Equal(VanillaKingSlimeItemIds.SlimySaddle, rules[1].PrimaryItem);
        Assert.Equal(4, rules[1].ChanceDenominator);
        Assert.Equal(VanillaKingSlimeItemIds.KingSlimeMask, rules[2].PrimaryItem);
        Assert.Equal(7, rules[2].ChanceDenominator);
        Assert.Equal(VanillaKingSlimeNormalLootRuleKind.OneFromThreeOptions, rules[3].Kind);
        Assert.Equal(3, rules[3].PotentialItemCount);
        Assert.Equal(VanillaKingSlimeItemIds.NinjaHood, rules[3].GetPotentialItem(0));
        Assert.Equal(VanillaKingSlimeItemIds.NinjaShirt, rules[3].GetPotentialItem(1));
        Assert.Equal(VanillaKingSlimeItemIds.NinjaPants, rules[3].GetPotentialItem(2));
        Assert.Equal(VanillaKingSlimeNormalLootRuleKind.NotScalingWithLuckWithCommonFallback, rules[4].Kind);
        Assert.Equal(VanillaKingSlimeItemIds.SlimeHook, rules[4].PrimaryItem);
        Assert.Equal(VanillaKingSlimeItemIds.SlimeGun, rules[4].SecondaryItem);
        Assert.Equal(3, rules[4].ChanceDenominator);
        Assert.Equal(VanillaKingSlimeItemIds.Solidifier, rules[5].PrimaryItem);
        Assert.Equal(1, rules[5].ChanceDenominator);
        Assert.Equal(VanillaKingSlimeItemIds.SlimeStaff, rules[6].PrimaryItem);
        Assert.Equal(30, rules[6].ChanceDenominator);
    }

    [Fact]
    public void Hook_success_uses_raw_rng_and_does_not_consume_luck()
    {
        VanillaKingSlimeNormalLootRule rule = VanillaKingSlimeNormalLootCatalog.Rules[4];
        var rolls = new ScriptedRollSource([], [0, 1]);

        Assert.True(VanillaKingSlimeNormalLootEvaluator.TryEvaluateRule(
            in rule, rolls, out bool dropped, out NpcLootDrop drop));

        Assert.True(dropped);
        Assert.Equal(new NpcLootDrop(VanillaKingSlimeItemIds.SlimeHook, 1), drop);
        Assert.Equal(new[] { "rng:0:3", "rng:1:2" }, rolls.Calls);
    }

    [Fact]
    public void Hook_failure_chains_to_common_slime_gun_and_luck()
    {
        VanillaKingSlimeNormalLootRule rule = VanillaKingSlimeNormalLootCatalog.Rules[4];
        var rolls = new ScriptedRollSource([0], [2, 1]);

        Assert.True(VanillaKingSlimeNormalLootEvaluator.TryEvaluateRule(
            in rule, rolls, out bool dropped, out NpcLootDrop drop));

        Assert.True(dropped);
        Assert.Equal(new NpcLootDrop(VanillaKingSlimeItemIds.SlimeGun, 1), drop);
        Assert.Equal(new[] { "rng:0:3", "luck:1", "rng:1:2" }, rolls.Calls);
    }

    [Fact]
    public void Normal_transaction_spawns_source_order_and_finalizes_generation()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        NpcSnapshot king = SpawnKing(npcs);
        Kill(npcs, king.Handle);
        var rolls = new ScriptedRollSource(
            [0, 0, 0, 0, 0, 0],
            [1, 1, 1, 1, 0, 1, 1, 1]);
        ItemTypeId[] supported = AllPotentialItems();
        var materializer = new FakeMaterializer(supported);
        var transaction = new RuntimeNpcLootWorldItemTransaction(npcs, items);
        Span<WorldItemSnapshot> spawned = stackalloc WorldItemSnapshot[7];

        Assert.True(transaction.TryFinalizeAndSpawn(
            king.Handle,
            default,
            rolls,
            materializer,
            spawned,
            out NpcLootWorldItemTransactionResult result));

        Assert.True(result.IsValid);
        Assert.Equal(VanillaNpcIds.KingSlime, result.Type);
        Assert.Equal(7, result.SpawnedItemCount);
        Assert.False(npcs.TryGet(king.Handle, out _));
        Assert.Equal(7, items.ActiveCount);
        Assert.Equal(
            new[]
            {
                VanillaKingSlimeItemIds.KingSlimeTrophy,
                VanillaKingSlimeItemIds.SlimySaddle,
                VanillaKingSlimeItemIds.KingSlimeMask,
                VanillaKingSlimeItemIds.NinjaShirt,
                VanillaKingSlimeItemIds.SlimeHook,
                VanillaKingSlimeItemIds.Solidifier,
                VanillaKingSlimeItemIds.SlimeStaff
            },
            materializer.MaterializedTypes);
    }

    [Fact]
    public void Missing_possible_branch_fails_before_rng_and_preserves_dead_npc()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        NpcSnapshot king = SpawnKing(npcs);
        Kill(npcs, king.Handle);
        var rolls = ScriptedRollSource.Empty();
        ItemTypeId[] supported = AllPotentialItems().Where(x => x != VanillaKingSlimeItemIds.NinjaPants).ToArray();
        var transaction = new RuntimeNpcLootWorldItemTransaction(npcs, items);
        Span<WorldItemSnapshot> spawned = stackalloc WorldItemSnapshot[7];

        Assert.False(transaction.TryFinalizeAndSpawn(
            king.Handle, default, rolls, new FakeMaterializer(supported), spawned, out _));

        Assert.Empty(rolls.Calls);
        Assert.Equal(0, items.ActiveCount);
        Assert.True(npcs.TryGet(king.Handle, out NpcSnapshot dead));
        Assert.Equal(0, dead.Simulation.Life);
    }

    [Fact]
    public void Expert_transaction_remains_unsupported_without_flattening_boss_bag()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        NpcSnapshot king = SpawnKing(npcs);
        Kill(npcs, king.Handle);
        var rolls = ScriptedRollSource.Empty();
        var context = new VanillaNpcLootContext(IsExpertMode: true, DropExtraGel: false);
        Span<WorldItemSnapshot> spawned = stackalloc WorldItemSnapshot[7];
        var transaction = new RuntimeNpcLootWorldItemTransaction(npcs, items);

        Assert.False(transaction.TryFinalizeAndSpawn(
            king.Handle, in context, rolls, new FakeMaterializer(AllPotentialItems()), spawned, out _));
        Assert.Empty(rolls.Calls);
        Assert.True(npcs.TryGet(king.Handle, out _));

        var lifecycle = new RuntimeNpcDeathLifecycleFinalizer(npcs);
        Assert.True(lifecycle.TryFinalizeWhenLootUnsupported(king.Handle, in context, out NpcDeathLifecycleResult result));
        Assert.True(result.WasBoss);
        Assert.False(npcs.TryGet(king.Handle, out _));
    }

    [Fact]
    public void Normal_lifecycle_fallback_cannot_bypass_imported_king_slime_loot()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot king = SpawnKing(npcs);
        Kill(npcs, king.Handle);
        var lifecycle = new RuntimeNpcDeathLifecycleFinalizer(npcs);

        Assert.False(lifecycle.TryFinalizeWhenLootUnsupported(king.Handle, out _));
        Assert.True(npcs.TryGet(king.Handle, out NpcSnapshot dead));
        Assert.Equal(0, dead.Simulation.Life);
    }

    private static NpcSnapshot SpawnKing(RuntimeNpcStore store)
    {
        var update = new NpcStateUpdate(
            Type: VanillaNpcIds.KingSlime.Value,
            NetId: checked((short)VanillaNpcIds.KingSlime.Value),
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot npc));
        return npc;
    }

    private static void Kill(RuntimeNpcStore store, NpcHandle handle)
    {
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(handle, DamageSource.Environment, BaseDamage: int.MaxValue);
        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.True(result.Lethal);
    }

    private static ItemTypeId[] AllPotentialItems() =>
    [
        VanillaKingSlimeItemIds.KingSlimeTrophy,
        VanillaKingSlimeItemIds.SlimySaddle,
        VanillaKingSlimeItemIds.KingSlimeMask,
        VanillaKingSlimeItemIds.NinjaHood,
        VanillaKingSlimeItemIds.NinjaShirt,
        VanillaKingSlimeItemIds.NinjaPants,
        VanillaKingSlimeItemIds.SlimeHook,
        VanillaKingSlimeItemIds.SlimeGun,
        VanillaKingSlimeItemIds.Solidifier,
        VanillaKingSlimeItemIds.SlimeStaff
    ];

    private sealed class FakeMaterializer(params ItemTypeId[] supported) : INpcLootWorldItemMaterializer
    {
        private readonly HashSet<ItemTypeId> _supported = new(supported);
        public List<ItemTypeId> MaterializedTypes { get; } = [];

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
            worldItem = new WorldItemDropStateUpdate(
                origin.CenterX - 1f,
                origin.CenterY - 1f,
                0f,
                0f,
                drop.Stack,
                Prefix: 0,
                Ownership: WorldItemOwnershipMode.None,
                ItemNetId: checked((short)drop.ItemType.Value),
                Shimmered: false,
                ShimmerTime: 0f,
                EnemyGrabDelayTime: 0);
            return true;
        }
    }

    private sealed class ScriptedRollSource(
        IEnumerable<int> luckResults,
        IEnumerable<int> randomResults) : INpcLootRollSource
    {
        private readonly Queue<int> _luck = new(luckResults);
        private readonly Queue<int> _random = new(randomResults);
        public List<string> Calls { get; } = [];

        public static ScriptedRollSource Empty() => new([], []);

        public int RollLuck(int chanceDenominator)
        {
            Calls.Add($"luck:{chanceDenominator}");
            return _luck.Dequeue();
        }

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Calls.Add($"rng:{inclusiveMin}:{exclusiveMax}");
            int value = _random.Dequeue();
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
