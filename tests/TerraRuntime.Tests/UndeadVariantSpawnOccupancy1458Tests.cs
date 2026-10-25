using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class UndeadVariantSpawnOccupancy1458Tests
{
    // Original 1.4.5.8 NewNPC observations: -13 retains one slot, -14 owns two,
    // and the sized Zombie/Skeleton variants multiply their base slot by final scale.
    // The real tick below observes those weights before the source rate roll.
    [Theory]
    [InlineData(-13, 31, 1, 0, false, 420, 1)]
    [InlineData(-14, 31, 1, 0, false, 480, 1)]
    [InlineData(-26, 3, 1, 0, false, 360, 1)]
    [InlineData(-27, 3, 1, 0, false, 420, 1)]
    [InlineData(-48, 201, 1, 0, false, 360, 1)]
    [InlineData(-49, 201, 1, 0, false, 420, 1)]
    [InlineData(-50, 202, 1, 0, false, 360, 1)]
    [InlineData(-51, 202, 1, 0, false, 420, 1)]
    [InlineData(-52, 203, 1, 0, false, 360, 1)]
    [InlineData(-53, 203, 1, 0, false, 420, 1)]
    [InlineData(201, 201, 1, 0, false, 420, 1)]
    [InlineData(202, 202, 1, 0, false, 420, 1)]
    [InlineData(203, 203, 1, 0, false, 420, 1)]
    [InlineData(635, 635, 1, 0, false, 420, 1)]
    [InlineData(-13, 31, 1, 3, false, 600, 1)]
    [InlineData(-14, 31, 1, 3, false, 600, 0)]
    [InlineData(-14, 31, 3, 0, false, 600, 0)]
    [InlineData(-26, 3, 5, 0, false, 600, 1)]
    [InlineData(-27, 3, 5, 0, false, 600, 0)]
    [InlineData(-26, 3, 6, 0, false, 600, 0)]
    [InlineData(-13, 31, 5, 0, true, 480, 1)]
    [InlineData(-13, 31, 6, 0, true, 480, 0)]
    [InlineData(-14, 31, 3, 0, true, 480, 0)]
    [InlineData(-26, 3, 7, 0, true, 480, 1)]
    [InlineData(-27, 3, 5, 0, true, 480, 1)]
    [InlineData(-27, 3, 6, 0, true, 480, 0)]
    [InlineData(-50, 202, 1, 0, true, 288, 1)]
    [InlineData(-49, 201, 1, 0, true, 336, 1)]
    public void Source_variant_weights_reach_real_spawn_rate_and_pre_rng_cap(
        int netId, int type, int variants, int ordinary, bool goodWorld, int expectedRate, int expectedCalls)
    {
        var npcs = new RuntimeNpcStore();
        var birthRandom = new BirthRandom();
        npcs.SetVanillaSpawnContextSource(() => new(goodWorld ? 2f : 1f, 1, goodWorld));
        npcs.SetVanillaSpawnRandomSource(birthRandom);
        for (int index = 0; index < variants + ordinary; index++)
        {
            int requested = index < variants ? netId : 21;
            int npcType = index < variants ? type : 21;
            var initial = new NpcStateUpdate(npcType, checked((short)requested), 3200, 4700,
                0, 0, 255, default, NpcSimulationState.Initial);
            Assert.True(npcs.TrySpawnVanilla(in initial, out var born));
            Assert.Equal(requested, born.NetId);
            if (requested == -26 && goodWorld)
                Assert.Equal(.85499996f, born.Simulation.Scale);
            if (requested == -27 && goodWorld)
                Assert.Equal(1.155f, born.Simulation.Scale);
        }
        Assert.Equal(goodWorld ? variants + ordinary : 0, birthRandom.Calls);

        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        var random = new RateRandom(expectedRate);
        var state = new ServerRuntimeState(npcs: npcs, npcAiStepper: new IdleStepper(), worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0, getGoodWorld: goodWorld),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
            { WorldSurface = 350, RockLayer = 600, GoodWorld = goodWorld },
            townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458), naturalSpawnRandom: random);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9821), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        Assert.Equal(expectedCalls, random.Calls);
        Assert.Equal(variants + ordinary, npcs.ActiveCount);
    }

    private sealed class BirthRandom : IVanillaNpcRandom
    {
        public int Calls { get; private set; }
        public int NextInt32(int minimum, int maximum)
        {
            Assert.Equal((0, 3), (minimum, maximum));
            Calls++;
            return 0;
        }
        public float NextSingle() => throw new InvalidOperationException("Unexpected birth float roll.");
    }

    private sealed class RateRandom(int rate) : IVanillaNpcRandom
    {
        public int Calls { get; private set; }
        public int NextInt32(int minimum, int maximum)
        {
            Assert.Equal((0, rate), (minimum, maximum));
            Calls++;
            return 1;
        }
        public float NextSingle() => throw new InvalidOperationException("Unexpected spawn float roll.");
    }

    private sealed class IdleStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
