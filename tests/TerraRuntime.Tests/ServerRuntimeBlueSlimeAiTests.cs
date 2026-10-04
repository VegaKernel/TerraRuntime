using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeBlueSlimeAiTests
{
    [Fact]
    public void World_backed_tick_commits_source_jump_gravity_and_motion_from_owned_plan()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SolidTile());
        tiles.Set(7, 6, SolidTile());
        var sink = new Sink();
        var state = new ServerRuntimeState(npcs: new RuntimeNpcStore(200, sink), worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000d, true, default, 0d, 0),
            townCommerceWorldFacts: WorldFacts());
        using var player = JoinPlayer(state);
        NpcSnapshot slime = Spawn(
            state,
            slot: 5,
            new NpcStateUpdate(
                Type: 1,
                NetId: 1,
                PositionX: 96f,
                PositionY: 78f,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: player.Handle.Slot.Value,
                Ai: new NpcAiState(0f, -1f, 1f, 0f),
                Simulation: NpcSimulationState.Initial with
                {
                    DirectionX = 1,
                    DirectionY = 1
                }));

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(slime.Handle, out NpcSnapshot updated));
        // The retained plan validates an unpublished placeholder, then adopts one final update.
        // Runtime revisions are internal; the source-facing state is published only once.
        Assert.Equal(new NpcRevision(3), updated.Revision);
        Assert.Equal(updated, Assert.Single(sink.Updates));
        Assert.Equal(2f, updated.VelocityX, 5);
        Assert.Equal(-5.925f, updated.VelocityY, 5);
        Assert.Equal(98f, updated.PositionX, 5);
        Assert.Equal(72.075f, updated.PositionY, 5);
        // Actual original NPC.UpdateNPC with this initialized-empty content/player/body fixture:
        // position(98,72.075), velocity(2,-5.925), ai(-1120,-1,1,0).
        Assert.Equal(-1120f, updated.Ai.Ai0);
        Assert.False(updated.Simulation.NoGravity);
        Assert.Equal(2f, updated.Simulation.OldVelocityX, 5);
        Assert.Equal(-5.925f, updated.Simulation.OldVelocityY, 5);
    }

    [Fact]
    public void Blue_slime_consumes_night_state_before_same_tick_dawn_transition()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SolidTile());
        tiles.Set(7, 6, SolidTile());
        var clock = new RuntimeWorldClock(
            time: RuntimeWorldClock.NightLength,
            dayTime: false,
            moonPhase: VanillaMoonPhase.HalfAtLeft,
            slimeRainTime: 0d,
            dayRate: 1);
        var state = new ServerRuntimeState(worldTiles: tiles, worldClock: clock,
            townCommerceWorldFacts: WorldFacts());
        using var player = JoinPlayer(state);
        NpcSnapshot slime = Spawn(
            state,
            slot: 5,
            new NpcStateUpdate(
                Type: 1,
                NetId: 1,
                PositionX: 96f,
                PositionY: 78f,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: player.Handle.Slot.Value,
                Ai: new NpcAiState(-2f, -1f, 1f, 0f),
                Simulation: NpcSimulationState.Initial with
                {
                    DirectionX = 1,
                    DirectionY = 1
                }));

        state.Tick();

        Assert.True(state.TryCaptureNpcSnapshot(slime.Handle, out NpcSnapshot updated));
        Assert.Equal(-1120f, updated.Ai.Ai0);
        Assert.Equal(2f, updated.VelocityX, 5);
        Assert.True(clock.DayTime);
        Assert.Equal(0d, clock.Time);
        Assert.Equal(VanillaMoonPhase.QuarterAtLeft, clock.MoonPhase);
    }

    [Fact]
    public void Blue_slime_remains_disabled_without_world_collision_context()
    {
        var state = new ServerRuntimeState();
        NpcSnapshot slime = Spawn(
            state,
            slot: 2,
            new NpcStateUpdate(
                Type: 1,
                NetId: 1,
                PositionX: 96f,
                PositionY: 78f,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget,
                Ai: new NpcAiState(0f, 0f, 1f, 0f),
                Simulation: NpcSimulationState.Initial));

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 0, 0, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(slime.Handle, out NpcSnapshot unchanged));
        Assert.Equal(new NpcRevision(1), unchanged.Revision);
    }

    private static NpcSnapshot Spawn(ServerRuntimeState state, byte slot, NpcStateUpdate update)
    {
        var completion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcSpawnRuntimeCommand(slot, update, completion));
        NpcSnapshot? snapshot = completion.Task.GetAwaiter().GetResult();
        Assert.True(snapshot.HasValue);
        return snapshot.Value;
    }

    private static WorldTileStore CreateWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(4200, 1200));
        Assert.True(tiles.TryAttachWorldSurface(140d));
        return tiles;
    }

    private static RuntimeTownCommerceWorldFacts1458 WorldFacts() =>
        default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 140d, RockLayer = 200d };

    private static PlayerJoinSession JoinPlayer(ServerRuntimeState state)
    {
        // Movement-only source fixture: ai[1]=-1 already denotes initialized empty contents.
        // TargetClosest still requires the real generation-owned player body and world facts.
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(871), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 10, 6, 0, 0, 0, 0, 0)));
        return session;
    }

    private sealed class Sink : INpcStateCommitSink
    {
        internal List<NpcSnapshot> Updates { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        {
            if (kind == NpcStateCommitKind.Update) Updates.Add(snapshot);
        }
    }

    private static WorldTile SolidTile() =>
        new()
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        };
}
