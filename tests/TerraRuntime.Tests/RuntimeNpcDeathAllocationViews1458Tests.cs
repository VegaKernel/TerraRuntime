using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcDeathAllocationViews1458Tests
{
    // Independent selector captures (world-item-allocation-views-official.json.gz) distinguish the physical
    // player view center: inactive/base42 frees50, mounted62 frees51, active dead mounted still frees51.
    [Theory]
    [InlineData(false, false, false, 50, 51)]
    [InlineData(true, false, false, 50, 51)]
    [InlineData(true, true, false, 51, 50)]
    [InlineData(true, true, true, 51, 50)]
    public void Live_whole_death_uses_active_dead_and_mounted_player_views_for_first_source_transfer(
        bool active, bool mounted, bool dead, short allocated, short preserved)
    {
        var sink = new Sink(); var items = WorldItemSourceAllocation1458Tests.CreateVisibilityStore(sink);
        var npcs = new RuntimeNpcStore(); var random = new VanillaUnifiedRandom1458(1458);
        var players = new Players(active, mounted, dead);
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, players, new PlayerAuthority(null, null),
            static () => 0, null, new(items), null, new RuntimeWorldClock(0, true, 0, 0, 1), new(), false, false,
            lootRandom: random);
        Assert.True(npcs.TrySpawnVanilla(new(1, 1, 1000, 1000, 0, 0, 255, default, NpcSimulationState.Initial), out var npc));
        sink.Events.Clear();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        var firstDrop = sink.Events.First(x => x.Kind == WorldItemStateCommitKind.Drop && x.Item.ItemNetId == 23);
        Assert.Equal(allocated, firstDrop.Item.Handle.Slot); Assert.Equal(23, firstDrop.Item.ItemNetId);
        var transfer = sink.Events.First(x => x.Kind == WorldItemStateCommitKind.Drop && x.Item.Handle.Slot == preserved &&
            x.Item.ItemNetId == 2 && x.Item.Stack == 20);
        Assert.Equal(2, transfer.Item.ItemNetId); Assert.Equal(20, transfer.Item.Stack);
        Assert.True(sink.Events.IndexOf(transfer) < sink.Events.IndexOf(firstDrop));
        Assert.False(npcs.TryGet(npc.Handle, out _));
    }
    private sealed class Players(bool active, bool mounted, bool dead) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = default(PlayerStateSnapshot) with { Player = new(new(0), new(1)), Revision = new(1), PositionX = 0,
                PositionY = -20, HasMount = mounted, MountType = 0, IsDead = dead,
                HasHealth = true, Life = (short)(dead ? 0 : 400), MaxLife = 400 };
            return active && slot.Value == 0;
        }
    }
    private sealed class Sink : IWorldItemStateCommitSink
    {
        public List<(WorldItemStateCommitKind Kind, WorldItemSnapshot Item)> Events { get; } = [];
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item) => Events.Add((kind, item));
    }
}
