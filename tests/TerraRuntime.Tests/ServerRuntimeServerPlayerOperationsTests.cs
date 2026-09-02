using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeServerPlayerOperationsTests
{
    [Fact]
    public async Task Create_and_despawn_hold_shared_slot_and_advance_generation_on_reuse()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            serverPlayers: new ServerPlayerAuthority(states, identities));
        var id = new ServerPlayerId("test:host-fake");

        var create = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, 120f, 240f, create));
        ServerPlayerCreateResult created = await create.Task;

        Assert.Equal(ServerPlayerCreateStatus.Created, created.Status);
        Assert.True(created.IsCreated);
        PlayerStateSnapshot? captured = await CaptureAsync(runtime, created.Player);
        Assert.True(captured.HasValue);
        PlayerStateSnapshot createdSnapshot = captured.GetValueOrDefault();
        Assert.Equal(120f, createdSnapshot.PositionX);
        Assert.Equal(240f, createdSnapshot.PositionY);
        Assert.False(slots.TryAcquireConnection(out _));

        var despawn = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerDespawnRuntimeCommand(id, despawn));
        Assert.True(await despawn.Task);
        Assert.Null(await CaptureAsync(runtime, created.Player));

        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? connection));
        Assert.NotNull(connection);
        Assert.Equal(created.Player.Slot, connection.Handle.Slot);
        Assert.True(connection.Handle.Generation.Value > created.Player.Generation.Value);
        connection.Dispose();
    }

    [Fact]
    public async Task Duplicate_id_is_rejected_without_consuming_another_slot()
    {
        var slots = new PlayerSlotPool(2);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            serverPlayers: new ServerPlayerAuthority(states, identities));
        var id = new ServerPlayerId("test:duplicate-fake");

        ServerPlayerCreateResult first = await CreateAsync(runtime, id, 10f, 20f);
        ServerPlayerCreateResult duplicate = await CreateAsync(runtime, id, 30f, 40f);

        Assert.Equal(ServerPlayerCreateStatus.Created, first.Status);
        Assert.Equal(ServerPlayerCreateStatus.AlreadyExists, duplicate.Status);
        Assert.Equal(1, slots.ServerOwnedLeasedCount);
        Assert.Equal(1, slots.LeasedCount);
    }

    [Fact]
    public async Task Connection_lease_can_exhaust_server_player_capacity()
    {
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? connection));
        Assert.NotNull(connection);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            serverPlayers: new ServerPlayerAuthority(states, identities));

        ServerPlayerCreateResult result = await CreateAsync(
            runtime,
            new ServerPlayerId("test:no-slot"),
            0f,
            0f);

        Assert.Equal(ServerPlayerCreateStatus.NoAvailableSlot, result.Status);
        Assert.False(result.Player.IsAssigned);
        connection.Dispose();
    }

    [Fact]
    public async Task Invalid_identity_and_position_do_not_allocate_slots()
    {
        var slots = new PlayerSlotPool(2);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            serverPlayers: new ServerPlayerAuthority(states, identities));

        ServerPlayerCreateResult invalidId = await CreateAsync(runtime, default, 0f, 0f);
        ServerPlayerCreateResult invalidPosition = await CreateAsync(
            runtime,
            new ServerPlayerId("test:invalid-position"),
            float.NaN,
            0f);

        Assert.Equal(ServerPlayerCreateStatus.InvalidId, invalidId.Status);
        Assert.Equal(ServerPlayerCreateStatus.InvalidPosition, invalidPosition.Status);
        Assert.Equal(0, slots.LeasedCount);
    }

    [Fact]
    public async Task Stable_id_can_be_recreated_only_after_despawn_with_new_generation()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            serverPlayers: new ServerPlayerAuthority(states, identities));
        var id = new ServerPlayerId("test:recreate-fake");

        ServerPlayerCreateResult first = await CreateAsync(runtime, id, 1f, 2f);
        var despawn = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerDespawnRuntimeCommand(id, despawn));
        Assert.True(await despawn.Task);
        ServerPlayerCreateResult second = await CreateAsync(runtime, id, 3f, 4f);

        Assert.Equal(ServerPlayerCreateStatus.Created, first.Status);
        Assert.Equal(ServerPlayerCreateStatus.Created, second.Status);
        Assert.Equal(first.Player.Slot, second.Player.Slot);
        Assert.True(second.Player.Generation.Value > first.Player.Generation.Value);
        Assert.Null(await CaptureAsync(runtime, first.Player));
        PlayerStateSnapshot? current = await CaptureAsync(runtime, second.Player);
        Assert.True(current.HasValue);
        PlayerStateSnapshot currentSnapshot = current.GetValueOrDefault();
        Assert.Equal(3f, currentSnapshot.PositionX);
        Assert.Equal(4f, currentSnapshot.PositionY);
    }

    private static async Task<ServerPlayerCreateResult> CreateAsync(
        ServerRuntimeState runtime,
        ServerPlayerId id,
        float positionX,
        float positionY)
    {
        var completion = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, positionX, positionY, completion));
        return await completion.Task;
    }

    private static async Task<PlayerStateSnapshot?> CaptureAsync(
        ServerRuntimeState runtime,
        PlayerHandle player)
    {
        var completion = new TaskCompletionSource<PlayerStateSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new PlayerStateSnapshotRuntimeCommand(player, completion));
        return await completion.Task;
    }
}
