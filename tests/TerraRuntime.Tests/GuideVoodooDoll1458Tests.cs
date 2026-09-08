using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GuideVoodooDoll1458Tests
{
    [Fact]
    public void Real_tick_burns_whole_stack_then_kills_guide_then_spawns_wall()
    {
        var f = new Fixture();
        var guide = f.Npc(22);
        var merchant = f.Npc(17);
        var doll = f.Doll(stack: 2);
        f.Events.Clear();
        f.State.Tick();
        Assert.False(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.False(f.Npcs.TryGet(guide.Handle, out _));
        Assert.False(f.Npcs.TryGet(merchant.Handle, out _));
        var wall = Assert.Single(f.ActiveNpcs(), npc => npc.Type == 113);
        Assert.Equal(255, wall.Target);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(113), out var def));
        Assert.Equal(2560 - def.Width / 2f, wall.PositionX);
        Assert.Equal(246 * 16 - def.Height, wall.PositionY);
        Assert.Equal("item:Remove:267", f.Events[0]);
        Assert.True(f.Events.IndexOf("npc:Despawn:22") < f.Events.IndexOf("npc:Spawn:113"));
        Assert.True(f.Events.IndexOf("npc:Spawn:113") < f.Events.IndexOf("npc:Despawn:17"));
    }

    [Fact]
    public void One_doll_strikes_all_guides_but_creates_only_one_wall()
    {
        var f = new Fixture();
        var first = f.Npc(22);
        var second = f.Npc(22);
        var merchant = f.Npc(17);
        f.Doll();
        f.State.Tick();
        Assert.False(f.Npcs.TryGet(first.Handle, out _));
        Assert.False(f.Npcs.TryGet(second.Handle, out _));
        Assert.True(f.Npcs.TryGet(merchant.Handle, out _));
        Assert.Single(f.ActiveNpcs(), n => n.Type == 113);
    }

    [Fact]
    public void No_guide_still_consumes_doll_but_spares_town_npcs()
    {
        var f = new Fixture();
        var merchant = f.Npc(17);
        var item = f.Doll(stack: 99);
        f.State.Tick();
        Assert.False(f.Items.TryGetActive(item.Handle.Slot, out _));
        Assert.True(f.Npcs.TryGet(merchant.Handle, out _));
        Assert.DoesNotContain(f.ActiveNpcs(), n => n.Type == 113);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_wall_or_surface_lava_does_not_spawn_another_boss(bool surface)
    {
        var f = new Fixture(surface ? 50 : 250);
        var guide = f.Npc(22);
        if (!surface) f.Npc(113);
        var doll = f.Doll();
        f.State.Tick();
        Assert.False(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.False(f.Npcs.TryGet(guide.Handle, out _));
        Assert.Equal(surface ? 0 : 1, f.ActiveNpcs().Count(n => n.Type == 113));
    }

    [Theory]
    [InlineData(WorldLiquidKind.Water)]
    [InlineData(WorldLiquidKind.Honey)]
    [InlineData(WorldLiquidKind.Shimmer)]
    public void Other_liquids_do_not_burn_or_summon(WorldLiquidKind liquid)
    {
        var f = new Fixture(liquid: liquid);
        var guide = f.Npc(22);
        var doll = f.Doll();
        f.State.Tick();
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.True(f.Npcs.TryGet(guide.Handle, out _));
    }

    [Fact]
    public void Client_reserved_doll_cannot_burn_until_server_owns_it()
    {
        var f = new Fixture();
        var guide = f.Npc(22);
        var doll = f.Doll(owner: 10);
        f.State.Tick();
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.True(f.Npcs.TryGet(guide.Handle, out _));
        // Existing writer owner boundary, not a client packet22 claim.
        Assert.True(f.Items.TryApplyOwner(doll.Handle.Slot, new WorldItemOwnerStateUpdate(255, 0, 255, 0, 2560, 4000), out _));
        f.State.Tick();
        Assert.False(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.False(f.Npcs.TryGet(guide.Handle, out _));
    }

    [Fact]
    public void Dropped_doll_falls_into_lava_in_real_runtime_without_per_tick_drop_replication()
    {
        var f = new Fixture();
        f.Npc(22);
        var doll = f.Doll(y: 240 * 16);
        f.Events.Clear();
        for (int i = 0; i < 100 && f.Items.TryGetActive(doll.Handle.Slot, out _); i++) f.State.Tick();
        Assert.False(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.Contains(f.ActiveNpcs(), n => n.Type == 113);
        Assert.DoesNotContain("item:Drop:267", f.Events);
    }

    [Fact]
    public void Unknown_world_and_other_item_type_do_not_gain_doll_semantics()
    {
        var f = new Fixture(known: false);
        var guide = f.Npc(22);
        var doll = f.Doll();
        f.State.Tick();
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.True(f.Npcs.TryGet(guide.Handle, out _));
        var g = new Fixture();
        var ordinary = g.Doll(type: 2);
        g.State.Tick();
        Assert.True(g.Items.TryGetActive(ordinary.Handle.Slot, out _));
    }

    [Fact]
    public void Reused_item_generation_cannot_inherit_lava_contact()
    {
        var f = new Fixture();
        var burned = f.Doll();
        f.State.Tick();
        var replacement = f.Doll(y: 240 * 16);
        Assert.Equal(burned.Handle.Slot, replacement.Handle.Slot);
        Assert.NotEqual(burned.Handle.Generation, replacement.Handle.Generation);
        f.State.Tick();
        Assert.True(f.Items.TryGetActive(replacement.Handle.Slot, out _));
    }

    private sealed class Fixture : IWorldItemStateCommitSink, INpcStateCommitSink
    {
        public List<string> Events { get; } = [];
        public RuntimeNpcStore Npcs { get; }
        public RuntimeWorldItemStore Items { get; }
        public ServerRuntimeState State { get; }
        private readonly int poolY;
        public Fixture(int poolY = 250, WorldLiquidKind liquid = WorldLiquidKind.Lava, bool known = true)
        {
            this.poolY = poolY;
            Npcs = new RuntimeNpcStore(commitSink: this);
            Items = new RuntimeWorldItemStore(this);
            var tiles = new WorldTileStore(new WorldDimensions(400, 400));
            for (int x = 150; x <= 170; x++)
            for (int y = poolY - 3; y <= poolY + 8; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = x is 150 or 170 || y == poolY + 8
                    ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active }
                    : new WorldTile { LiquidAmount = 255, LiquidKind = liquid };
            State = new ServerRuntimeState(npcs: Npcs, worldItems: Items, worldTiles: tiles,
                npcAiStepper: new Stationary(), townCommerceWorldFacts: known ? default(RuntimeTownCommerceWorldFacts1458) : null);
        }
        public NpcSnapshot Npc(ushort type)
        {
            var update = new NpcStateUpdate(type, (short)type, 800, 800, 0, 0, 255, default, NpcSimulationState.Initial);
            Assert.True(Npcs.TrySpawnVanilla(in update, out var npc));
            return npc;
        }
        public WorldItemSnapshot Doll(short stack = 1, byte owner = 255, float? y = null, short type = 267)
        {
            var update = new WorldItemStateUpdate(2560, y ?? poolY * 16, 0, 0, stack, 0, WorldItemOwnershipMode.None,
                type, false, 0, 0, owner, 0, 255, 0);
            Assert.True(Items.TryAllocate(in update, out var item));
            return item;
        }
        public NpcSnapshot[] ActiveNpcs()
        {
            var buffer = new NpcSnapshot[Npcs.Capacity];
            return buffer[..Npcs.CopyActive(buffer)];
        }
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item) => Events.Add($"item:{kind}:{item.ItemNetId}");
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) => Events.Add($"npc:{kind}:{npc.Type}");
    }
    private sealed class Stationary : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
