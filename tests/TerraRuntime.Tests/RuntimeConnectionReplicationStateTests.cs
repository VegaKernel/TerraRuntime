using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class RuntimeConnectionReplicationStateTests
{
    [Fact]
    public void Server_player_replica_replacement_is_exact_generation_and_rejects_stale_updates()
    {
        var store = new ServerPlayerReplicaStore();
        PlayerSlotId slot = new(7);
        PlayerHandle firstHandle = new(slot, new PlayerSessionGeneration(1));
        PlayerHandle secondHandle = new(slot, new PlayerSessionGeneration(2));
        PlayerStateSnapshot first = Snapshot(firstHandle, revision: 1, x: 10f);
        PlayerStateSnapshot second = Snapshot(secondHandle, revision: 1, x: 20f);

        Assert.True(store.TryCreate(in first, out _, out _));
        var firstVitals = new ServerPlayerVitalsState(90, 100, 10, 20);
        Assert.True(store.TryUpdateVitals(firstHandle, in firstVitals, out _, out _));
        Assert.True(store.TryGetHealthFrame(firstHandle, out _));

        Assert.True(store.TryCreate(in second, out _, out _));
        Assert.False(store.TryGetHealthFrame(firstHandle, out _));

        var staleVitals = new ServerPlayerVitalsState(1, 100, 0, 20);
        Assert.False(store.TryUpdateVitals(firstHandle, in staleVitals, out _, out _));
        Assert.False(store.TryRemove(firstHandle, out _));

        var currentVitals = new ServerPlayerVitalsState(80, 100, 15, 20);
        Assert.True(store.TryUpdateVitals(secondHandle, in currentVitals, out _, out _));
        Assert.True(store.TryGetHealthFrame(secondHandle, out _));
    }

    [Fact]
    public void Stale_movement_snapshot_cannot_overwrite_current_generation_replica()
    {
        var store = new ServerPlayerReplicaStore();
        PlayerSlotId slot = new(8);
        PlayerHandle oldHandle = new(slot, new PlayerSessionGeneration(10));
        PlayerHandle currentHandle = new(slot, new PlayerSessionGeneration(11));
        PlayerStateSnapshot oldSnapshot = Snapshot(oldHandle, revision: 1, x: 1f);
        PlayerStateSnapshot currentSnapshot = Snapshot(currentHandle, revision: 1, x: 2f);

        Assert.True(store.TryCreate(in currentSnapshot, out _, out _));
        Assert.False(store.TryUpdateMovement(in oldSnapshot, out _));
        Assert.True(store.TryGetMovementFrame(currentHandle, out _));
        Assert.False(store.TryGetMovementFrame(oldHandle, out _));
    }

    private static PlayerStateSnapshot Snapshot(
        PlayerHandle player,
        ulong revision,
        float x) =>
        new(
            Player: player,
            Revision: new PlayerStateRevision(revision),
            Team: 0,
            ControlFlags: 0,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 0,
            PositionX: x,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            MountType: 0,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            CameraTargetX: 0f,
            CameraTargetY: 0f);
}
