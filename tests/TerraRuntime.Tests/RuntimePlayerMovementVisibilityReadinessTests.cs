using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerMovementVisibilityReadinessTests
{
    [Fact]
    public void Readiness_is_directional_and_pair_clear_resets_both_directions()
    {
        var readiness = new RuntimePlayerMovementVisibilityReadiness();
        var first = new PlayerSlotId(1);
        var second = new PlayerSlotId(2);

        Assert.True(readiness.MarkReady(first, second));
        Assert.True(readiness.IsReady(first, second));
        Assert.False(readiness.IsReady(second, first));
        Assert.False(readiness.MarkReady(first, second));

        Assert.True(readiness.MarkReady(second, first));
        Assert.Equal(2, readiness.Snapshot.ReadyDirections);
        Assert.Equal(2, readiness.ClearPair(first, second));
        Assert.False(readiness.IsReady(first, second));
        Assert.False(readiness.IsReady(second, first));
        Assert.Equal(0, readiness.Snapshot.ReadyDirections);
    }

    [Fact]
    public void Successful_resync_marks_only_the_delivered_direction_ready()
    {
        var registry = CreateRegistry(out GameCommandSourceId firstSource, out GameCommandSourceId secondSource, out PlayerSlotId first, out PlayerSlotId second);
        PlayerMovementCommitRequest firstMovement = CreateMovementRequest(first, PixelsAtSection(0), 200f);
        PlayerMovementCommitRequest secondMovement = CreateMovementRequest(second, PixelsAtSection(0) + 20f, 220f);
        registry.PlayerMoved(firstSource, in firstMovement);
        registry.PlayerMoved(secondSource, in secondMovement);

        Assert.False(registry.IsPlayerMovementVisibilityReady(first, second));
        Assert.False(registry.IsPlayerMovementVisibilityReady(second, first));

        var operation = new RuntimePlayerMovementResyncOperation(first, second);
        Assert.True(registry.TryEnqueuePlayerMovementResync(in operation));

        Assert.True(registry.IsPlayerMovementVisibilityReady(first, second));
        Assert.False(registry.IsPlayerMovementVisibilityReady(second, first));
        Assert.Equal(1, registry.PlayerMovementVisibilityReadinessSnapshot.ReadyDirections);
    }

    [Fact]
    public void Leaving_visibility_radius_clears_previously_ready_directions()
    {
        var registry = CreateRegistry(out GameCommandSourceId firstSource, out GameCommandSourceId secondSource, out PlayerSlotId first, out PlayerSlotId second);
        PlayerMovementCommitRequest firstMovement = CreateMovementRequest(first, PixelsAtSection(0), 200f);
        PlayerMovementCommitRequest secondMovement = CreateMovementRequest(second, PixelsAtSection(0) + 20f, 220f);
        registry.PlayerMoved(firstSource, in firstMovement);
        registry.PlayerMoved(secondSource, in secondMovement);

        MarkBothDirectionsReady(registry, first, second);
        Assert.True(registry.IsPlayerMovementVisibilityReady(first, second));
        Assert.True(registry.IsPlayerMovementVisibilityReady(second, first));

        PlayerMovementCommitRequest leave = CreateMovementRequest(second, PixelsAtSection(5), 220f);
        registry.PlayerMoved(secondSource, in leave);

        Assert.False(registry.IsPlayerMovementVisibilityReady(first, second));
        Assert.False(registry.IsPlayerMovementVisibilityReady(second, first));
        Assert.Equal(0, registry.PlayerMovementVisibilityReadinessSnapshot.ReadyDirections);
        Assert.Equal(0, registry.PlayerVisibilitySnapshot?.VisiblePairs);
    }

    [Fact]
    public void Unregister_clears_ready_directions_before_slot_reuse()
    {
        var registry = CreateRegistry(out GameCommandSourceId firstSource, out GameCommandSourceId secondSource, out PlayerSlotId first, out PlayerSlotId second);
        PlayerMovementCommitRequest firstMovement = CreateMovementRequest(first, PixelsAtSection(0), 200f);
        PlayerMovementCommitRequest secondMovement = CreateMovementRequest(second, PixelsAtSection(0) + 20f, 220f);
        registry.PlayerMoved(firstSource, in firstMovement);
        registry.PlayerMoved(secondSource, in secondMovement);
        MarkBothDirectionsReady(registry, first, second);
        Assert.Equal(2, registry.PlayerMovementVisibilityReadinessSnapshot.ReadyDirections);

        Assert.True(registry.TryUnregister(secondSource, out PlayerSlotId? released));
        Assert.Equal(second, released);
        Assert.False(registry.IsPlayerMovementVisibilityReady(first, second));
        Assert.False(registry.IsPlayerMovementVisibilityReady(second, first));
        Assert.Equal(0, registry.PlayerMovementVisibilityReadinessSnapshot.ReadyDirections);
    }

    private static void MarkBothDirectionsReady(
        RuntimeConnectionRegistry registry,
        PlayerSlotId first,
        PlayerSlotId second)
    {
        var firstSeesSecond = new RuntimePlayerMovementResyncOperation(first, second);
        var secondSeesFirst = new RuntimePlayerMovementResyncOperation(second, first);
        Assert.True(registry.TryEnqueuePlayerMovementResync(in firstSeesSecond));
        Assert.True(registry.TryEnqueuePlayerMovementResync(in secondSeesFirst));
    }

    private static RuntimeConnectionRegistry CreateRegistry(
        out GameCommandSourceId firstSource,
        out GameCommandSourceId secondSource,
        out PlayerSlotId first,
        out PlayerSlotId second)
    {
        var registry = new RuntimeConnectionRegistry(
            new InterestManagementControl(enabled: true),
            new WorldDimensions(2_000, 600));
        firstSource = GameCommandSourceId.FromConnection(1);
        secondSource = GameCommandSourceId.FromConnection(2);
        first = new PlayerSlotId(1);
        second = new PlayerSlotId(2);

        Assert.True(registry.TryRegister(firstSource, CreateOutbound()));
        Assert.True(registry.TryRegister(secondSource, CreateOutbound()));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawnRequest(first, 100, 200);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawnRequest(second, 120, 200);
        registry.PlayerSpawned(firstSource, in firstSpawn);
        registry.PlayerSpawned(secondSource, in secondSpawn);
        Assert.Equal(1, registry.PlayerVisibilitySnapshot?.VisiblePairs);
        return registry;
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 32_768, maxFrameBytes: 1_024));

    private static PlayerSpawnCommitRequest CreateSpawnRequest(
        PlayerSlotId slot,
        short spawnX,
        short spawnY) =>
        new(
            slot,
            SpawnX: spawnX,
            SpawnY: spawnY,
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

    private static float PixelsAtSection(int section) =>
        ((section * TerrariaSectionGeometry.WidthTiles) + 10) * 16f;
}
