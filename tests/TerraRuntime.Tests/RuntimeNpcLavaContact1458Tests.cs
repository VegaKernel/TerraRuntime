using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcLavaContact1458Tests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(21)]
    [InlineData(22)]
    public void Real_tick_applies_defense_mitigated_environment_damage_without_client_packets(ushort type)
    {
        var fixture = new Fixture(type);
        fixture.State.Tick();
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var damaged));
        int defense = type switch { 1 or 2 => 2, 3 => 6, 21 => 8, 22 => 30, _ => throw new InvalidOperationException() };
        Assert.Equal(300 - (50 - defense / 2), damaged.Simulation.Life);
    }

    [Fact]
    public void Direct_contact_strikes_wait_thirty_updates_and_reused_slot_does_not_inherit_immunity()
    {
        var fixture = new Fixture(21);
        fixture.State.Tick();
        for (int i = 1; i < 30; i++) fixture.State.Tick();
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var before));
        Assert.Equal(254, before.Simulation.Life);
        fixture.State.Tick();
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var after));
        Assert.Equal(208, after.Simulation.Life);
        Assert.True(fixture.Npcs.TryDespawn(after.Handle));
        Assert.True(fixture.Npcs.TrySpawn(after.Handle.Slot, fixture.Initial, out var replacement));
        fixture.State.Tick();
        Assert.True(fixture.Npcs.TryGet(replacement.Handle, out var damaged));
        Assert.Equal(254, damaged.Simulation.Life);
    }

    [Fact]
    public void Lethal_environment_damage_uses_existing_death_finalization()
    {
        var fixture = new Fixture(3, life: 45);
        fixture.State.Tick();
        Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
        Assert.Equal(0, fixture.Npcs.ActiveCount);
    }

    [Theory]
    [InlineData(WorldLiquidKind.Water)]
    [InlineData(WorldLiquidKind.Honey)]
    [InlineData(WorldLiquidKind.Shimmer)]
    public void Non_lava_does_not_cause_a_lava_strike(WorldLiquidKind kind)
    {
        var fixture = new Fixture(21, liquid: kind);
        fixture.State.Tick();
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var npc));
        Assert.Equal(300, npc.Simulation.Life);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(151)]
    [InlineData(440)]
    public void Hell_creatures_and_unadmitted_dynamic_types_are_not_assumed_vulnerable(ushort type)
    {
        var fixture = new Fixture(type);
        fixture.State.Tick();
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var npc));
        Assert.Equal(300, npc.Simulation.Life);
    }

    [Fact]
    public void Invulnerable_presentation_actor_is_preserved()
    {
        var fixture = new Fixture(21, invulnerable: true);
        fixture.State.Tick();
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var npc));
        Assert.Equal(300, npc.Simulation.Life);
    }

    [Fact]
    public void Unsupported_remix_contact_does_not_apply_ordinary_fifty_damage()
    {
        var fixture = new Fixture(21, remix: true);
        fixture.State.Tick();
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var npc));
        Assert.Equal(300, npc.Simulation.Life);
    }

    private sealed class Fixture
    {
        public RuntimeNpcStore Npcs { get; } = new(8);
        public ServerRuntimeState State { get; }
        public NpcSnapshot Npc { get; }
        public NpcStateUpdate Initial { get; }

        public Fixture(ushort type, int life = 300, WorldLiquidKind liquid = WorldLiquidKind.Lava, bool invulnerable = false, bool remix = false)
        {
            var tiles = new WorldTileStore(new WorldDimensions(80, 200));
            // Enclosed full liquid cells remain stable under the real liquid tick. No client Wet input.
            for (int x = 8; x <= 15; x++)
            for (int y = 8; y <= 15; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = x is 8 or 15 || y is 8 or 15
                    ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active }
                    : new WorldTile { LiquidAmount = 255, LiquidKind = liquid };
            Initial = new NpcStateUpdate(type, (short)type, 160, 160, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = life, LifeMax = life, DontTakeDamage = invulnerable });
            Assert.True(Npcs.TrySpawn(0, Initial, out var npc));
            Npc = npc;
            RuntimeTownCommerceWorldFacts1458 facts = default;
            State = new ServerRuntimeState(npcs: Npcs, worldTiles: tiles, npcAiStepper: new StationaryNpc(),
                townCommerceWorldFacts: facts with { RemixWorld = remix });
        }
    }

    private sealed class StationaryNpc : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
