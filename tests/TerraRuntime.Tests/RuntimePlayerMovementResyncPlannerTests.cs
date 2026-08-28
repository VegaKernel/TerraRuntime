using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerMovementResyncPlannerTests
{
    [Fact]
    public void Planner_requires_real_snapshots_for_each_resync_direction()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(2);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        var first = new PlayerSlotId(1);
        var second = new PlayerSlotId(2);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawnRequest(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawnRequest(second);
        registry.PlayerSpawned(firstSource, in firstSpawn);
        registry.PlayerSpawned(secondSource, in secondSpawn);

        PlayerMovementCommitRequest firstMovement = CreateMovementRequest(first, 100f, 200f);
        registry.PlayerMoved(firstSource, in firstMovement);

        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[1];
        entered[0] = second;
        Span<RuntimePlayerMovementResyncOperation> operations = stackalloc RuntimePlayerMovementResyncOperation[2];
        RuntimePlayerMovementResyncPlan plan = registry.PlanPlayerMovementResyncs(first, entered, operations);

        Assert.Equal(new RuntimePlayerMovementResyncPlan(1, 1, 0), plan);
        Assert.Equal(new RuntimePlayerMovementResyncOperation(second, first), operations[0]);

        PlayerMovementCommitRequest secondMovement = CreateMovementRequest(second, 300f, 400f);
        registry.PlayerMoved(secondSource, in secondMovement);
        plan = registry.PlanPlayerMovementResyncs(first, entered, operations);

        Assert.Equal(new RuntimePlayerMovementResyncPlan(2, 0, 0), plan);
        Assert.Equal(new RuntimePlayerMovementResyncOperation(second, first), operations[0]);
        Assert.Equal(new RuntimePlayerMovementResyncOperation(first, second), operations[1]);
    }

    [Fact]
    public void Planned_resync_reuses_cached_frame_and_targets_only_recipient_queue()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(10);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(20);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        var first = new PlayerSlotId(10);
        var second = new PlayerSlotId(20);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawnRequest(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawnRequest(second);
        registry.PlayerSpawned(firstSource, in firstSpawn);
        registry.PlayerSpawned(secondSource, in secondSpawn);

        PlayerMovementCommitRequest firstMovement = CreateMovementRequest(first, 123f, 456f);
        registry.PlayerMoved(firstSource, in firstMovement);
        int firstBefore = firstOutbound.QueuedFrames;
        int secondBefore = secondOutbound.QueuedFrames;

        var operation = new RuntimePlayerMovementResyncOperation(second, first);
        Assert.True(registry.TryEnqueuePlayerMovementResync(in operation));

        Assert.Equal(firstBefore, firstOutbound.QueuedFrames);
        Assert.Equal(secondBefore + 1, secondOutbound.QueuedFrames);
        Assert.Equal(1, registry.MovementResyncFrames);
    }

    [Fact]
    public void Planner_drops_operations_when_an_entered_endpoint_has_unregistered()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(100);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(200);
        var first = new PlayerSlotId(5);
        var second = new PlayerSlotId(6);

        Assert.True(registry.TryRegister(firstSource, CreateOutbound()));
        Assert.True(registry.TryRegister(secondSource, CreateOutbound()));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawnRequest(first);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawnRequest(second);
        registry.PlayerSpawned(firstSource, in firstSpawn);
        registry.PlayerSpawned(secondSource, in secondSpawn);
        PlayerMovementCommitRequest firstMovement = CreateMovementRequest(first, 10f, 20f);
        PlayerMovementCommitRequest secondMovement = CreateMovementRequest(second, 30f, 40f);
        registry.PlayerMoved(firstSource, in firstMovement);
        registry.PlayerMoved(secondSource, in secondMovement);

        Assert.True(registry.TryUnregister(secondSource, out _));

        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[1];
        entered[0] = second;
        Span<RuntimePlayerMovementResyncOperation> operations = stackalloc RuntimePlayerMovementResyncOperation[2];
        RuntimePlayerMovementResyncPlan plan = registry.PlanPlayerMovementResyncs(first, entered, operations);

        Assert.Equal(new RuntimePlayerMovementResyncPlan(0, 0, 2), plan);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static PlayerSpawnCommitRequest CreateSpawnRequest(PlayerSlotId slot) =>
        new(
            slot,
            SpawnX: 100,
            SpawnY: 200,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 0,
            SpawnContext: 0);

    private static PlayerMovementCommitRequest CreateMovementRequest(PlayerSlotId slot, float x, float y) =>
        new(
            slot,
            ControlFlags: 0x03,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 4,
            PositionX: x,
            PositionY: y,
            HasVelocity: false,
            VelocityX: 0f,
            VelocityY: 0f,
            HasMount: false,
            MountType: 0,
            HasPotionOfReturnPositions: false,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            HasCameraTarget: false,
            CameraTargetX: 0f,
            CameraTargetY: 0f);
}
