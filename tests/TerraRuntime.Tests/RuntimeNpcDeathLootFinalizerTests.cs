using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcDeathLootFinalizerTests
{
    [Fact]
    public void Dead_blue_slime_finalizes_exact_generation_and_returns_ordered_loot()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcSnapshot slime = Spawn(store, slot: 0, VanillaNpcIds.BlueSlime.Value, 48f, 64f);
        Kill(store, slime.Handle);
        var rolls = new ScriptedLootRollSource(
            luckResults: new[] { 0, 0 },
            randomResults: new[] { 2, 1 });
        var finalizer = new RuntimeNpcDeathLootFinalizer(store);
        Span<NpcLootDrop> drops = stackalloc NpcLootDrop[2];

        Assert.True(finalizer.TryFinalize(
            slime.Handle,
            new VanillaNpcLootContext(IsExpertMode: false, DropExtraGel: false),
            rolls,
            drops,
            out NpcDeathLootResult result));

        Assert.True(result.IsValid);
        Assert.Equal(slime.Handle, result.Target);
        Assert.Equal(new NpcRevision(2), result.FinalRevision);
        Assert.Equal(VanillaNpcIds.BlueSlime, result.Type);
        Assert.Equal(48f, result.PositionX);
        Assert.Equal(64f, result.PositionY);
        Assert.Equal(2, result.DropCount);
        Assert.Equal(VanillaItemIds.Gel, drops[0].ItemType);
        Assert.Equal((short)2, drops[0].Stack);
        Assert.Equal(VanillaItemIds.SlimeStaff, drops[1].ItemType);
        Assert.Equal((short)1, drops[1].Stack);
        Assert.False(store.TryGet(slime.Handle, out _));
        Assert.Equal(
            new[] { "luck:1", "rng:1:3", "luck:10000", "rng:1:2" },
            rolls.Calls);
    }

    [Fact]
    public void Successful_finalization_is_exactly_once_for_one_npc_generation()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot slime = Spawn(store, slot: 0, VanillaNpcIds.BlueSlime.Value, 0f, 0f);
        Kill(store, slime.Handle);
        var firstRolls = new ScriptedLootRollSource(
            luckResults: new[] { 0, 1 },
            randomResults: new[] { 1 });
        var finalizer = new RuntimeNpcDeathLootFinalizer(store);
        Span<NpcLootDrop> firstDrops = stackalloc NpcLootDrop[2];

        Assert.True(finalizer.TryFinalize(
            slime.Handle,
            default,
            firstRolls,
            firstDrops,
            out NpcDeathLootResult first));
        Assert.Equal(1, first.DropCount);

        var secondRolls = EmptyRolls();
        Span<NpcLootDrop> secondDrops = stackalloc NpcLootDrop[2];
        Assert.False(finalizer.TryFinalize(
            slime.Handle,
            default,
            secondRolls,
            secondDrops,
            out _));
        Assert.Empty(secondRolls.Calls);
    }

    [Fact]
    public void Live_npc_is_not_finalized_and_consumes_no_loot_rng()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot slime = Spawn(store, slot: 0, VanillaNpcIds.BlueSlime.Value, 0f, 0f);
        var rolls = EmptyRolls();
        var finalizer = new RuntimeNpcDeathLootFinalizer(store);
        Span<NpcLootDrop> drops = stackalloc NpcLootDrop[2];

        Assert.False(finalizer.TryFinalize(slime.Handle, default, rolls, drops, out _));

        Assert.Empty(rolls.Calls);
        Assert.True(store.TryGet(slime.Handle, out NpcSnapshot stillAlive));
        Assert.True(stillAlive.Simulation.Life > 0);
    }

    [Fact]
    public void Stale_generation_cannot_finalize_replacement_in_reused_slot()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot stale = Spawn(store, slot: 0, VanillaNpcIds.BlueSlime.Value, 0f, 0f);
        Assert.True(store.TryDespawn(stale.Handle));
        NpcSnapshot replacement = Spawn(store, slot: 0, VanillaNpcIds.BlueSlime.Value, 16f, 16f);
        Kill(store, replacement.Handle);
        var rolls = EmptyRolls();
        var finalizer = new RuntimeNpcDeathLootFinalizer(store);
        Span<NpcLootDrop> drops = stackalloc NpcLootDrop[2];

        Assert.False(finalizer.TryFinalize(stale.Handle, default, rolls, drops, out _));

        Assert.Empty(rolls.Calls);
        Assert.True(store.TryGet(replacement.Handle, out NpcSnapshot current));
        Assert.Equal(0, current.Simulation.Life);
        Assert.NotEqual(stale.Handle.Generation, replacement.Handle.Generation);
    }

    [Fact]
    public void Unsupported_dead_npc_fails_closed_without_despawn_or_rng()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot zombie = Spawn(store, slot: 0, VanillaNpcIds.Zombie.Value, 0f, 0f);
        Kill(store, zombie.Handle);
        var rolls = EmptyRolls();
        var finalizer = new RuntimeNpcDeathLootFinalizer(store);
        Span<NpcLootDrop> drops = stackalloc NpcLootDrop[2];

        Assert.False(finalizer.TryFinalize(zombie.Handle, default, rolls, drops, out _));

        Assert.Empty(rolls.Calls);
        Assert.True(store.TryGet(zombie.Handle, out NpcSnapshot current));
        Assert.Equal(0, current.Simulation.Life);
    }

    [Fact]
    public void Short_destination_preserves_dead_npc_and_consumes_no_rng()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot slime = Spawn(store, slot: 0, VanillaNpcIds.BlueSlime.Value, 0f, 0f);
        Kill(store, slime.Handle);
        var rolls = EmptyRolls();
        var finalizer = new RuntimeNpcDeathLootFinalizer(store);
        Span<NpcLootDrop> drops = stackalloc NpcLootDrop[1];

        Assert.False(finalizer.TryFinalize(slime.Handle, default, rolls, drops, out _));

        Assert.Empty(rolls.Calls);
        Assert.True(store.TryGet(slime.Handle, out NpcSnapshot current));
        Assert.Equal(0, current.Simulation.Life);
    }

    private static ScriptedLootRollSource EmptyRolls() =>
        new(Array.Empty<int>(), Array.Empty<int>());

    private static NpcSnapshot Spawn(
        RuntimeNpcStore store,
        byte slot,
        int type,
        float positionX,
        float positionY)
    {
        var update = new NpcStateUpdate(
            Type: type,
            NetId: checked((short)type),
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

        Assert.True(store.TrySpawn(slot, in update, out NpcSnapshot snapshot));
        return snapshot;
    }

    private static void Kill(RuntimeNpcStore store, NpcHandle handle)
    {
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            handle,
            DamageSource.Environment,
            BaseDamage: 1000);
        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.True(result.Lethal);
    }

    private sealed class ScriptedLootRollSource(
        IEnumerable<int> luckResults,
        IEnumerable<int> randomResults) : INpcLootRollSource
    {
        private readonly Queue<int> _luckResults = new(luckResults);
        private readonly Queue<int> _randomResults = new(randomResults);

        public List<string> Calls { get; } = new();

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
