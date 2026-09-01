using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcActorIntentStateStepperTests
{
    [Fact]
    public void Follow_player_changes_bounded_motion_intent_without_teleporting()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate initial = CreateZombie(positionX: 100f, positionY: 100f);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot npc));
        var controls = new RuntimeNpcActorControlRegistry(store);
        PlayerHandle target = new(new PlayerSlotId(7), new PlayerSessionGeneration(3));
        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(npc.Handle, new ActorControllerId("test:follow"), out NpcActorControlLease? lease));
        Assert.True(lease!.TryFollowPlayer(target));
        controls.CommitPending();

        var lookup = new FixedPlayerLookup(CreatePlayer(target, positionX: 220f, positionY: 100f));
        var stepper = new RuntimeNpcActorIntentStateStepper(new NoOpStepper(), controls, lookup);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(npc.PositionX, next.PositionX);
        Assert.Equal(npc.PositionY, next.PositionY);
        Assert.Equal(NpcActorMotionOptions.Default.HorizontalAcceleration, next.VelocityX, 5);
        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(1, next.Simulation.DirectionX);
        Assert.Equal(1, next.Simulation.SpriteDirection);
        Assert.False(next.Simulation.NoGravity);
    }

    [Fact]
    public void Missing_exact_player_generation_brakes_instead_of_following_reused_slot()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate initial = CreateZombie(positionX: 100f, positionY: 100f) with { VelocityX = 1f };
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot npc));
        var controls = new RuntimeNpcActorControlRegistry(store);
        PlayerHandle oldTarget = new(new PlayerSlotId(7), new PlayerSessionGeneration(3));
        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(npc.Handle, new ActorControllerId("test:follow"), out NpcActorControlLease? lease));
        Assert.True(lease!.TryFollowPlayer(oldTarget));
        controls.CommitPending();

        PlayerHandle replacement = new(new PlayerSlotId(7), new PlayerSessionGeneration(4));
        var lookup = new FixedPlayerLookup(CreatePlayer(replacement, positionX: 500f, positionY: 100f));
        var stepper = new RuntimeNpcActorIntentStateStepper(new NoOpStepper(), controls, lookup);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.True(next.VelocityX < npc.VelocityX);
        Assert.Equal(npc.Target, next.Target);
        Assert.Equal(npc.PositionX, next.PositionX);
    }

    [Fact]
    public void Uncontrolled_npc_delegates_to_fallback()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate initial = CreateZombie(positionX: 100f, positionY: 100f);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot npc));
        var controls = new RuntimeNpcActorControlRegistry(store);
        var fallback = new FixedVelocityStepper(0.75f);
        var stepper = new RuntimeNpcActorIntentStateStepper(fallback, controls, new EmptyPlayerLookup());

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(1, fallback.Calls);
        Assert.Equal(0.75f, next.VelocityX);
    }

    [Fact]
    public void Controlled_follow_flows_through_world_motion_for_actual_position_and_gravity()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate initial = CreateZombie(positionX: 100f, positionY: 100f);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot npc));
        var controls = new RuntimeNpcActorControlRegistry(store);
        PlayerHandle target = new(new PlayerSlotId(5), new PlayerSessionGeneration(2));
        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(npc.Handle, new ActorControllerId("test:physics"), out NpcActorControlLease? lease));
        Assert.True(lease!.TryFollowPlayer(
            target,
            new NpcActorMotionOptions(
                StopDistance: 0f,
                MaximumHorizontalSpeed: 2f,
                HorizontalAcceleration: 0.5f,
                MaximumDistance: 0f)));
        controls.CommitPending();

        var lookup = new FixedPlayerLookup(CreatePlayer(target, positionX: 300f, positionY: 100f));
        var intent = new RuntimeNpcActorIntentStateStepper(new NoOpStepper(), controls, lookup);
        var physics = new VanillaNpcWorldMotionAiStepper(intent, tiles);

        Assert.True(intent.TryStepState(in npc, out NpcStateUpdate intentOnly));
        Assert.Equal(npc.PositionX, intentOnly.PositionX);
        Assert.Equal(npc.PositionY, intentOnly.PositionY);

        Assert.True(physics.TryStepState(in npc, out NpcStateUpdate simulated));
        Assert.True(simulated.PositionX > npc.PositionX);
        Assert.True(simulated.PositionY > npc.PositionY);
        Assert.Equal(0.5f, simulated.Simulation.OldVelocityX, 5);
        Assert.True(simulated.Simulation.OldVelocityY > 0f);
    }

    private static NpcStateUpdate CreateZombie(float positionX, float positionY) =>
        new(
            Type: VanillaNpcIds.Zombie.Value,
            NetId: checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 0,
                NoGravity = false
            });

    private static PlayerStateSnapshot CreatePlayer(
        PlayerHandle player,
        float positionX,
        float positionY) =>
        new(
            player,
            new PlayerStateRevision(1),
            Team: 0,
            ControlFlags: 0,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 0,
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            MountType: 0,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            CameraTargetX: 0f,
            CameraTargetY: 0f);

    private sealed class FixedPlayerLookup(PlayerStateSnapshot player) : IRuntimePlayerSnapshotLookup
    {
        private readonly PlayerStateSnapshot _player = player;

        public bool TryGetPlayer(PlayerHandle player, out PlayerStateSnapshot snapshot)
        {
            if (_player.Player == player)
            {
                snapshot = _player;
                return true;
            }

            snapshot = default;
            return false;
        }
    }

    private sealed class EmptyPlayerLookup : IRuntimePlayerSnapshotLookup
    {
        public bool TryGetPlayer(PlayerHandle player, out PlayerStateSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
    }

    private sealed class NoOpStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class FixedVelocityStepper(float velocityX) : INpcAiStateStepper
    {
        public int Calls { get; private set; }

        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            Calls++;
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                velocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            return true;
        }
    }
}
