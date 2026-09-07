using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerBuffRelayTests
{
    [Fact]
    public void Pre_spawn_buff_snapshots_are_cached_and_exchanged_on_join()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(5201);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(5202);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        PlayerSlotId firstSlot = new(5);
        PlayerSlotId secondSlot = new(6);
        ConnectionHandle first = Connection(firstSource, firstSlot);
        ConnectionHandle second = Connection(secondSource, secondSlot);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerBuffTypesCommitRequest firstBuffs = Request(firstSlot, [VanillaBuffIds.OnFire]);
        PlayerBuffTypesCommitRequest secondBuffs = Request(secondSlot, [VanillaBuffIds.Slow]);
        registry.PlayerBuffTypesUpdated(first, in firstBuffs);
        registry.PlayerBuffTypesUpdated(second, in secondBuffs);
        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(0, secondOutbound.QueuedFrames);

        PlayerSpawnCommitRequest firstSpawn = Spawn(firstSlot);
        registry.PlayerSpawned(first, in firstSpawn);
        PlayerSpawnCommitRequest secondSpawn = Spawn(secondSlot);
        registry.PlayerSpawned(second, in secondSpawn);

        Assert.Equal(2, registry.BuffBaselineFrames);
        Assert.True(registry.TryGetLatestPlayerBuffFrame(firstSlot, out OutboundFrame firstFrame));
        Assert.True(registry.TryGetLatestPlayerBuffFrame(secondSlot, out OutboundFrame secondFrame));
        AssertBuffFrame(firstFrame, firstSlot.Value, [VanillaBuffIds.OnFire]);
        AssertBuffFrame(secondFrame, secondSlot.Value, [VanillaBuffIds.Slow]);
    }

    [Fact]
    public void Playing_buff_update_relays_only_to_peers_and_suppresses_identical_snapshot()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(5211);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(5212);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        PlayerSlotId firstSlot = new(7);
        PlayerSlotId secondSlot = new(8);
        ConnectionHandle first = Connection(firstSource, firstSlot);
        ConnectionHandle second = Connection(secondSource, secondSlot);
        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = Spawn(firstSlot);
        PlayerSpawnCommitRequest secondSpawn = Spawn(secondSlot);
        registry.PlayerSpawned(first, in firstSpawn);
        registry.PlayerSpawned(second, in secondSpawn);
        Drain(firstOutbound);
        Drain(secondOutbound);

        PlayerBuffTypesCommitRequest request = Request(firstSlot, [VanillaBuffIds.OnFire, VanillaBuffIds.Slow]);
        registry.PlayerBuffTypesUpdated(first, in request);
        registry.PlayerBuffTypesUpdated(first, in request);

        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(1, secondOutbound.QueuedFrames);
        Assert.Equal(1, registry.RelayedBuffFrames);
        Assert.Equal(1, registry.SuppressedDuplicateBuffFrames);
        AssertBuffFrame(Dequeue(secondOutbound), firstSlot.Value, [VanillaBuffIds.OnFire, VanillaBuffIds.Slow]);
    }

    [Fact]
    public void Authoritative_pvp_buff_targets_only_exact_playing_generation()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(5215);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(5216);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        PlayerSlotId firstSlot = new(12);
        PlayerSlotId secondSlot = new(13);
        ConnectionHandle first = Connection(firstSource, firstSlot);
        ConnectionHandle second = Connection(secondSource, secondSlot);
        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = Spawn(firstSlot);
        PlayerSpawnCommitRequest secondSpawn = Spawn(secondSlot);
        registry.PlayerSpawned(first, in firstSpawn);
        registry.PlayerSpawned(second, in secondSpawn);
        Drain(firstOutbound);
        Drain(secondOutbound);

        registry.PlayerPvpBuffApplied(first.Player, VanillaBuffIds.OnFire, 180);

        Assert.Equal(1, firstOutbound.QueuedFrames);
        Assert.Equal(0, secondOutbound.QueuedFrames);
        Assert.Equal(1, registry.RelayedPvpBuffFrames);
        AssertPvpBuffFrame(Dequeue(firstOutbound), firstSlot.Value, VanillaBuffIds.OnFire, 180);

        PlayerHandle stale = new(firstSlot, new PlayerSessionGeneration(2));
        registry.PlayerPvpBuffApplied(stale, VanillaBuffIds.Poisoned, 600);
        registry.PlayerPvpBuffApplied(first.Player, VanillaBuffIds.Regeneration, 60);

        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(1, registry.RelayedPvpBuffFrames);
    }

    [Fact]
    public void Transfer_profile_distinguishes_unobserved_from_observed_empty_buff_snapshot()
    {
        var store = new RuntimePlayerTransferProfileStore();
        PlayerSlotId slot = new(10);
        ConnectionHandle connection = Connection(GameCommandSourceId.FromConnection(5221), slot);
        var appearance = new PlayerAppearanceCommitRequest(
            slot, 0, 0, 0f, 0, "BuffProfile", 0, 0, 0,
            default, default, default, default, default, default, default, 0, 0, 0);

        Assert.True(store.TrySetAppearance(connection, in appearance));
        Assert.True(store.TryCapture(connection, out _, out _, out BuffTypeId[]? neverObserved));
        Assert.Null(neverObserved);

        Assert.True(store.TrySetBuffTypes(connection, ReadOnlyMemory<BuffTypeId>.Empty));
        Assert.True(store.TryCapture(connection, out _, out _, out BuffTypeId[]? observedEmpty));
        Assert.NotNull(observedEmpty);
        Assert.Empty(observedEmpty);
    }

    [Fact]
    public void Transfer_profile_restore_rejects_invalid_buff_ids_instead_of_filtering_them()
    {
        var store = new RuntimePlayerTransferProfileStore();
        PlayerSlotId slot = new(11);
        ConnectionHandle connection = Connection(GameCommandSourceId.FromConnection(5222), slot);

        Assert.Throws<ArgumentException>(() => store.Restore(
            connection,
            appearance: null,
            equipment: [],
            buffTypes: [new BuffTypeId(401)]));
    }

    [Fact]
    public void Retained_buff_frame_is_generation_scoped()
    {
        var outbound = CreateOutbound();
        var endpoint = new RuntimeConnectionEndpoint(outbound);
        PlayerSlotId slot = new(9);
        PlayerHandle first = new(slot, new PlayerSessionGeneration(1));
        PlayerHandle second = new(slot, new PlayerSessionGeneration(2));
        byte[] firstFrame = TerrariaPlayerBuffCodec1458.Encode(slot.Value, [VanillaBuffIds.OnFire]);
        byte[] secondFrame = TerrariaPlayerBuffCodec1458.Encode(slot.Value, [VanillaBuffIds.Slow]);

        endpoint.MarkPlaying(first);
        Assert.True(endpoint.UpdateLatestBuffFrame(first, firstFrame));
        endpoint.MarkPlaying(second);
        Assert.False(endpoint.TryGetLatestBuffFrame(second, out _));
        Assert.True(endpoint.UpdateLatestBuffFrame(second, secondFrame));
        endpoint.ClearPlaying(first);

        Assert.True(endpoint.TryGetLatestBuffFrame(second, out OutboundFrame retained));
        AssertBuffFrame(retained, slot.Value, [VanillaBuffIds.Slow]);
    }

    private static PlayerBuffTypesCommitRequest Request(PlayerSlotId slot, BuffTypeId[] buffs) => new(slot, buffs);

    private static ConnectionHandle Connection(GameCommandSourceId source, PlayerSlotId slot) =>
        new(source, new PlayerHandle(slot, new PlayerSessionGeneration(1)));

    private static PlayerSpawnCommitRequest Spawn(PlayerSlotId slot) =>
        new(slot, 100, 200, 0, 0, 0, 0, 0);

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 32_768, maxFrameBytes: 1_024));

    private static void AssertPvpBuffFrame(OutboundFrame outbound, byte player, BuffTypeId expected, int duration)
    {
        var input = new ReadOnlySequence<byte>(outbound.Bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.Equal(
            TerrariaPlayerPvpBuffDecodeResult.Decoded,
            TerrariaPlayerPvpBuffCodec1458.TryDecode(in frame, out byte target, out BuffTypeId buff, out int ticks));
        Assert.Equal(player, target);
        Assert.Equal(expected, buff);
        Assert.Equal(duration, ticks);
    }

    private static void AssertBuffFrame(OutboundFrame outbound, byte player, BuffTypeId[] expected)
    {
        var input = new ReadOnlySequence<byte>(outbound.Bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.Equal(
            TerrariaPlayerBuffDecodeResult.Decoded,
            TerrariaPlayerBuffCodec1458.TryDecode(in frame, out byte claimed, out BuffTypeId[] buffs));
        Assert.Equal(player, claimed);
        Assert.Equal(expected, buffs);
    }

    private static OutboundFrame Dequeue(TerrariaConnectionOutboundQueue outbound)
    {
        BoundedOutboundQueue queue = GetInnerQueue(outbound);
        Assert.True(queue.TryRead(out OutboundFrame frame));
        return frame;
    }

    private static void Drain(TerrariaConnectionOutboundQueue outbound)
    {
        BoundedOutboundQueue queue = GetInnerQueue(outbound);
        while (queue.TryRead(out _))
        {
        }
    }

    private static BoundedOutboundQueue GetInnerQueue(TerrariaConnectionOutboundQueue outbound) =>
        (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(outbound)!;
}
