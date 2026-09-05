using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerAuthorityWorldBoundsTests
{
    [Fact]
    public void Movement_outside_vanilla_world_border_is_rejected_without_mutating_position()
    {
        var tiles = new WorldTileStore(new WorldDimensions(4200, 1200));
        var authority = new PlayerAuthority(events: null, tiles);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(91), session.Handle);
        var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
        Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot before));

        PlayerMovementCommitRequest escapedRight = Movement(
            session.Slot,
            tiles.Dimensions.WidthTiles * 16f - VanillaPlayerWorldBounds1458.BorderPixels,
            before.PositionY);
        PlayerMovementCommitRequest escapedBottom = Movement(
            session.Slot,
            before.PositionX,
            tiles.Dimensions.HeightTiles * 16f - VanillaPlayerWorldBounds1458.BorderPixels);

        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, escapedRight)));
        Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, escapedBottom)));
        Assert.True(authority.TryCapture(connection.Player, out PlayerStateSnapshot after));
        Assert.Equal(before.PositionX, after.PositionX);
        Assert.Equal(before.PositionY, after.PositionY);
        Assert.Equal(2, authority.RejectedMovements);
        Assert.Equal(0, authority.AppliedMovements);
    }

    private static PlayerMovementCommitRequest Movement(PlayerSlotId slot, float x, float y) =>
        new(
            slot,
            0,
            0,
            0,
            0,
            0,
            x,
            y,
            false,
            0f,
            0f,
            false,
            0,
            false,
            0f,
            0f,
            0f,
            0f,
            false,
            0f,
            0f);

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }
}
