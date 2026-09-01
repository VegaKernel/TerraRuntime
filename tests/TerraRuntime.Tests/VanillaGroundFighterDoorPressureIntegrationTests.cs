using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaGroundFighterDoorPressureIntegrationTests
{
    [Fact]
    public void Blood_moon_world_state_reaches_fighter_door_opening_sink()
    {
        WorldTileStore tiles = CreateWorld();
        var targeting = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        targeting.SetWorldConditions(dayTime: false, slimeRainActive: false);
        targeting.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 220f,
                CenterY: 100f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        var clock = new RuntimeWorldClock(
            time: 1_000d,
            dayTime: false,
            moonPhase: VanillaMoonPhase.Full,
            slimeRainTime: 0d,
            dayRate: 1,
            bloodMoonActive: true);
        var sink = new CapturingDoorSink();
        var stepper = new VanillaNpcWorldMotionAiStepper(
            targeting,
            tiles,
            worldSurfaceTiles: 100d,
            worldEvents: clock,
            doorRandom: new FixedDoorRandom(false),
            doorOpeningSink: sink);
        NpcSnapshot zombie = CreateZombie();

        Assert.True(stepper.TryStepState(in zombie, out NpcStateUpdate next));

        Assert.True(sink.Captured.HasValue);
        Assert.Equal(VanillaTileIds.ClosedDoor, sink.Captured.Value.ClosedType);
        Assert.Equal(0f, next.Ai.Ai1, 5);
    }

    private static NpcSnapshot CreateZombie() =>
        new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.Zombie.Value,
            NetId: checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: 96f,
            PositionY: 80f,
            VelocityX: 0.5f,
            VelocityY: 0f,
            Target: 7,
            Ai: new NpcAiState(0f, 5f, 59f, 0f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                OldPositionX = 95f,
                OldPositionY = 80f,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1f
            });

    private static WorldTileStore CreateWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(240, 240));
        tiles.Set(6, 7, new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Stone.Value),
            Flags = WorldTileFlags.Active
        });
        tiles.Set(7, 5, new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.ClosedDoor.Value),
            Flags = WorldTileFlags.Active
        });
        return tiles;
    }

    private sealed class CapturingDoorSink : IVanillaGroundFighterDoorOpeningSink
    {
        public VanillaGroundFighterDoorOpeningIntent? Captured { get; private set; }

        public bool TryOpen(in VanillaGroundFighterDoorOpeningIntent intent)
        {
            Captured = intent;
            return true;
        }
    }

    private sealed class FixedDoorRandom(bool result) : IVanillaGroundFighterDoorRandom
    {
        public bool NextGraveyardProgress() => result;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
