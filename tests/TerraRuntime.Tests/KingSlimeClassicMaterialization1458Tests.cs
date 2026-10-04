using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class KingSlimeClassicMaterialization1458Tests
{
    [Theory]
    [InlineData(256, 18, 12)]
    [InlineData(257, 18, 18)]
    [InlineData(258, 18, 18)]
    [InlineData(998, 26, 20)]
    [InlineData(2430, 16, 30)]
    [InlineData(2489, 30, 30)]
    [InlineData(2493, 28, 20)]
    [InlineData(2585, 18, 28)]
    [InlineData(2610, 38, 10)]
    public void Original_defaults_and_nonprefixable_items_are_admitted_for_world_drop(int id, int width, int height)
    {
        // Independent original Item.SetDefaults + CanHavePrefixes execution, not a table copied from runtime.
        var type = new ItemTypeId(id);
        Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(type, out var definition));
        Assert.Equal(width, definition.Width);
        Assert.Equal(height, definition.Height);
        Assert.Equal(VanillaItemPrefixFamily.None, definition.PrefixFamily);
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(type));
        var random = new NoPrefixRandom();
        Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(type, random, out var prefix));
        Assert.Equal(VanillaPrefixIds.None, prefix);
    }

    [Fact]
    public void Every_potential_classic_rule_item_can_materialize_before_any_roll()
    {
        foreach (var rule in VanillaKingSlimeNormalLootCatalog.Rules)
            for (int item = 0; item < rule.PotentialItemCount; item++)
                Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(rule.GetPotentialItem(item)));
    }

    [Fact]
    public void Real_classic_lethal_request_materializes_required_rewards_and_duplicate_does_not_deliver_again()
    {
        var npcs = new RuntimeNpcStore(capacity: 1);
        var items = new RuntimeWorldItemStore();
        var state = new ServerRuntimeState(npcs: npcs, worldItems: items);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        var owner = new ConnectionHandle(GameCommandSourceId.FromConnection(99), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(owner, session, new PlayerSpawnCommitRequest(session.Slot, 10, 10, 0, 0, 0, 0, 0)));
        Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);
        // This request exercises death/loot delivery through the existing compatibility path, not melee math.
        state.Apply(new PlayerEquipmentRuntimeCommand(owner, new PlayerEquipmentCommitRequest(owner.Player.Slot, 0, 1, 0, 39, 0)));
        var spawn = new NpcStateUpdate(50, 50, 100f, 100f, 0f, 0f, 255, default,
            NpcSimulationState.Initial with { Life = 1, LifeMax = 1, Scale = 1f });
        Assert.True(npcs.TrySpawn(0, in spawn, out var king));
        var wire = new TerrariaNpcDamageState(king.Handle.Slot,
            RuntimeNpcPacketProjection.ToProtocolGeneration(king.Handle.Generation), 1, 0f, 2, 0);
        state.Apply(new ClientNpcDamageRuntimeCommand(owner, wire));
        Assert.Equal(1, state.AppliedClientNpcDamage);
        Assert.False(npcs.TryGet(king.Handle, out _));
        Span<WorldItemSnapshot> drops = stackalloc WorldItemSnapshot[
            items.Capacity];
        int count = items.CopyActive(drops);
        Assert.Equal(items.ActiveCount, count);
        var recovery = drops[..count].ToArray();
        var potion = Assert.Single(recovery, drop => drop.ItemNetId == VanillaBossRecoveryItemIds1458.LesserHealingPotion.Value);
        Assert.InRange(potion.Stack, 5, 15);
        Assert.InRange(recovery.Count(drop => drop.ItemNetId == VanillaWallOfFleshItemIds.Heart.Value), 5, 9);
        Assert.All(recovery.Where(drop => drop.ItemNetId == VanillaWallOfFleshItemIds.Heart.Value), drop => Assert.Equal((short)1, drop.Stack));
        Assert.InRange(recovery.Count(drop => drop.ItemNetId is not (28 or 58) && !VanillaCoinFacts.TryGetValue(new(drop.ItemNetId), out _)), 3, 7);
        Assert.Contains(recovery, drop => VanillaCoinFacts.TryGetValue(new(drop.ItemNetId), out _));
        var ids = drops[..count].ToArray().Select(drop => drop.ItemNetId).ToArray();
        Assert.Single(ids, id => id is 256 or 257 or 258);
        Assert.Single(ids, id => id is 2585 or 2610);
        Assert.Contains((short)998, ids);
        state.Apply(new ClientNpcDamageRuntimeCommand(owner, wire));
        Assert.Equal(1, state.AppliedClientNpcDamage);
        Assert.Equal(count, items.ActiveCount);
    }

    private sealed class NoPrefixRandom : INpcLootRollSource
    {
        public int NextInt32(int minimumInclusive, int maximumExclusive) =>
            throw new InvalidOperationException("Nonprefixable source items must not consume prefix RNG.");

        public int RollLuck(int range) => throw new InvalidOperationException("Prefix roll must not consume luck RNG.");
    }
}
