using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerMount1458Tests
{
    [Theory]
    [InlineData(true, 0, 62f)]
    [InlineData(true, 1, 62f)]
    [InlineData(false, 0, 42f)]
    public void Accepted_movement_keeps_mount_activation_in_snapshot_replication_and_npc_geometry(
        bool mounted, ushort mountType, float height)
    {
        // MessageBuffer packet 13 tests bit 7 independently of ReadUInt16's value.
        // Mount.Initialize/SetMount gives Rudolph (type zero) a 20-pixel height boost.
        var state = new ServerRuntimeState();
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = Join(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(914), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0)));
        var movement = Movement(session.Slot, mounted, mountType);
        state.Apply(new PlayerMovementRuntimeCommand(connection, movement));
        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out PlayerStateSnapshot player));
        Assert.Equal(mounted, player.HasMount);
        Assert.Equal(mountType, player.MountType);

        byte[] frame = TerrariaPlayerReplicationFrameEncoder.EncodeMovement(in player);
        Assert.Equal(mounted, (frame[5] & 0x80) != 0);
        Assert.Equal(mounted ? 27 : 25, frame.Length);
        if (mounted)
            Assert.Equal(mountType, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(25)));

        state.Tick();
        var composition = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var candidates = (VanillaNpcTargetCandidate[])typeof(NpcAuthority)
            .GetField("targetCandidates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(composition.Npcs)!;
        Assert.Equal(20f, candidates[0].HitboxWidth);
        Assert.Equal(height, candidates[0].HitboxHeight);
        Assert.Equal(3200f + height / 2f, candidates[0].CenterY);

        movement = movement with { HasMount = false, MovementFlags = 0 };
        state.Apply(new PlayerMovementRuntimeCommand(connection, movement));
        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out player));
        Assert.False(player.HasMount);
        Assert.Equal((ushort)0, player.MountType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Transfer_preserves_mount_zero_only_when_world_position_is_preserved(bool preserve)
    {
        var source = new PlayerAuthority(events: null, worldTiles: null);
        var destination = new PlayerAuthority(events: null,
            worldTiles: new WorldTileStore(new WorldDimensions(500, 500)));
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = Join(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(915), session.Handle);
        Assert.True(source.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0))));
        Assert.True(source.TryApply(new PlayerMovementRuntimeCommand(connection, Movement(session.Slot, true, 0))));
        var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
        Assert.True(source.TryApply(new PlayerTransferDetachRuntimeCommand(connection, detached)));
        RuntimePlayerTransferState transfer = Assert.IsType<RuntimePlayerTransferState>(await detached.Task);
        Assert.True(transfer.Player.HasMount);
        var attached = new TaskCompletionSource<bool>();
        Assert.True(destination.TryApply(new PlayerTransferAttachRuntimeCommand(
            connection, transfer, 100, 200, preserve, false, attached)));
        Assert.True(await attached.Task);
        Assert.True(destination.TryCapture(connection.Player, out PlayerStateSnapshot player));
        Assert.Equal(preserve, player.HasMount);
        Assert.Equal((ushort)0, player.MountType);
        byte[] frame = TerrariaPlayerReplicationFrameEncoder.EncodeMovement(in player);
        Assert.Equal(preserve, (frame[5] & 0x80) != 0);
    }

    private static PlayerMovementCommitRequest Movement(PlayerSlotId slot, bool mounted, ushort mountType) =>
        new(slot, 0, mounted ? VanillaPlayerMovementNormalizer.MovementMountPresentFlag : (byte)0,
            0, 0, 0, 1600f, 3200f, false, 0f, 0f, mounted, mountType,
            false, 0f, 0f, 0f, 0f, false, 0f, 0f);

    private static PlayerJoinSession Join(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out var lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        return session;
    }
}
