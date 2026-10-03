using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeNaturalSpawnBuff1458Tests
{
    // Independently captured from original Player.UpdateBuffs + NPC.Spawner.GetSpawnRate.
    [Theory]
    [InlineData(new[] { 10 }, 432)]
    [InlineData(new[] { 13 }, 180)]
    [InlineData(new[] { 106 }, 594)]
    [InlineData(new[] { 146 }, 432)]
    [InlineData(new[] { 10, 106, 146, 13 }, 427)]
    [InlineData(new[] { 86, 157 }, 360)] // Candle buff icons do not set source scene zones.
    public void Remote_buff_snapshot_reaches_real_tick_in_source_order(int[] buffs, int rate)
    {
        using var fixture = new Fixture(rate);
        fixture.Buffs(buffs);
        fixture.State.Tick();
        fixture.Random.AssertCalls(1);
    }

    [Theory]
    [InlineData(13, 5, 300, 1)] // Battle raises the cap from five to ten after occupancy.
    [InlineData(106, 3, 891, 0)] // Calming truncates the cap to three and stops before RNG.
    [InlineData(10, 4, 720, 0)]
    [InlineData(146, 4, 720, 0)]
    public void Source_buff_cap_is_applied_before_the_spawn_roll(int buff, int occupants, int rate, int calls)
    {
        using var fixture = new Fixture(rate, occupants);
        fixture.Buffs([buff]);
        fixture.State.Tick();
        fixture.Random.AssertCalls(calls);
    }

    [Fact]
    public void Replacing_snapshot_removes_modifier_and_remote_timer_does_not_expire()
    {
        using var fixture = new Fixture(180);
        fixture.Buffs([13]);
        for (int i = 0; i < 61; i++) fixture.State.Tick();
        fixture.Random.AssertCalls(61);
        fixture.Buffs([]);
        fixture.Random.Rate = 360;
        fixture.State.Tick();
        fixture.Random.AssertCalls(62);
    }

    [Theory]
    [InlineData(49, 135)]
    [InlineData(372, 234)]
    public void Battle_precedes_server_owned_scene_candle(int candle, int rate)
    {
        using var fixture = new Fixture(rate, candle: candle);
        fixture.Buffs([13]);
        fixture.State.Tick();
        fixture.Random.AssertCalls(1);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        private readonly ConnectionHandle connection;
        public ServerRuntimeState State { get; }
        public RateRandom Random { get; }
        public Fixture(int rate, int occupants = 0, int candle = 0)
        {
            var npcs = new RuntimeNpcStore();
            for (int i = 0; i < occupants; i++)
            {
                var initial = new NpcStateUpdate(1, 1, 3200, 4700, 0, 0, 255, default, NpcSimulationState.Initial);
                Assert.True(npcs.TrySpawnVanilla(in initial, out _));
            }
            var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
            if (candle != 0) tiles.Tiles[tiles.GetUncheckedIndex(120, 250)] = new WorldTile
                { Type = (ushort)candle, Flags = WorldTileFlags.Active, FrameX = 0 };
            Random = new RateRandom(rate);
            State = new ServerRuntimeState(npcs: npcs, npcAiStepper: new IdleStepper(), worldTiles: tiles,
                worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 350, RockLayer = 600 },
                townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458), naturalSpawnRandom: Random);
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new PlayerJoinSession(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9201), session.Handle);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session,
                new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));
        }
        public void Buffs(int[] buffs) => State.Apply(new PlayerBuffTypesRuntimeCommand(connection,
            new PlayerBuffTypesCommitRequest(session.Handle.Slot, buffs.Select(value => new BuffTypeId(value)).ToArray())));
        public void Dispose() => session.Dispose();
    }
    private sealed class RateRandom(int rate) : IVanillaNpcRandom
    {
        public int Rate { get; set; } = rate;
        private int calls;
        public int NextInt32(int minimum, int maximum)
        {
            Assert.Equal(0, minimum); Assert.Equal(Rate, maximum); calls++; return 1;
        }
        public float NextSingle() => throw new InvalidOperationException("Unexpected float roll.");
        public void AssertCalls(int expected) => Assert.Equal(expected, calls);
    }
    private sealed class IdleStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
