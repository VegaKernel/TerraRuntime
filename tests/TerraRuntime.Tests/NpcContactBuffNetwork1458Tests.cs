using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class NpcContactBuffNetwork1458Tests
{
    [Fact]
    public void Earned_non_pvp_contact_effects_send_exact_adjusted_packet55_only_to_target()
    {
        var registry = new RuntimeConnectionRegistry();
        var target = AddPlaying(registry, 145801, 12);
        var observer = AddPlaying(registry, 145802, 13);
        Drain(target.Outbound);
        Drain(observer.Outbound);

        // Independent wire expectations from MessageBuffer(55): Int16 length, byte55, local player,
        // UInt16 type and Int32 adjusted duration. Frozen60 in Master has already become150 upstream.
        Assert.False(VanillaPvpBuffFacts1458.IsRelayable(VanillaBuffIds.Frozen));
        Assert.False(VanillaPvpBuffFacts1458.IsRelayable(new BuffTypeId(48)));
        registry.PlayerNpcContactBuffApplied(target.Connection.Player, VanillaBuffIds.Frozen, 150);
        registry.PlayerNpcContactBuffApplied(target.Connection.Player, new BuffTypeId(48), 1800);
        Assert.Equal(new byte[] { 10, 0, 55, 12, 47, 0, 150, 0, 0, 0 }, Dequeue(target.Outbound).Bytes.ToArray());
        Assert.Equal(new byte[] { 10, 0, 55, 12, 48, 0, 8, 7, 0, 0 }, Dequeue(target.Outbound).Bytes.ToArray());
        Assert.Equal(0, observer.Outbound.QueuedFrames);
        Assert.Equal(0, target.Outbound.QueuedFrames);

        // This source-owned outbound route does not widen hostile packet55/PvP admission.
        registry.PlayerPvpBuffApplied(target.Connection.Player, new BuffTypeId(48), 1800);
        registry.PlayerNpcContactBuffApplied(target.Connection.Player, VanillaBuffIds.Regeneration, 60);
        registry.PlayerNpcContactBuffApplied(target.Connection.Player, VanillaBuffIds.Frozen, 0);
        Assert.Equal(0, target.Outbound.QueuedFrames);
        Assert.Equal(0, observer.Outbound.QueuedFrames);
    }

    [Fact]
    public void Contact_confirmation_requires_playing_exact_generation_after_disconnect_and_slot_reuse()
    {
        var registry = new RuntimeConnectionRegistry();
        var outbound = CreateOutbound();
        var source = GameCommandSourceId.FromConnection(145811);
        var connection = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(12), new PlayerSessionGeneration(1)));
        Assert.True(registry.TryRegister(source, outbound));
        registry.PlayerNpcContactBuffApplied(connection.Player, new BuffTypeId(48), 1800);
        Assert.Equal(0, outbound.QueuedFrames);
        var spawn = Spawn(connection.Player.Slot);
        registry.PlayerSpawned(connection, in spawn);
        Drain(outbound);
        Assert.True(registry.TryUnregister(source, out var removed));
        Assert.Equal(connection.Player, removed);

        var replacement = AddPlaying(registry, 145812, 12, generation: 2);
        Drain(replacement.Outbound);
        registry.PlayerNpcContactBuffApplied(connection.Player, new BuffTypeId(48), 1800);
        Assert.Equal(0, outbound.QueuedFrames);
        Assert.Equal(0, replacement.Outbound.QueuedFrames);
        registry.PlayerNpcContactBuffApplied(replacement.Connection.Player, new BuffTypeId(48), 1800);
        Assert.Equal(new byte[] { 10, 0, 55, 12, 48, 0, 8, 7, 0, 0 }, Dequeue(replacement.Outbound).Bytes.ToArray());
    }

    [Fact]
    public void Contact_presence_uses_packet50_for_observers_and_late_join_without_replaying_duration_confirmation()
    {
        var registry = new RuntimeConnectionRegistry();
        var target = AddPlaying(registry, 145821, 12);
        var observer = AddPlaying(registry, 145822, 13);
        Drain(target.Outbound);
        Drain(observer.Outbound);
        registry.PlayerNpcContactBuffApplied(target.Connection.Player, new BuffTypeId(48), 1800);
        var presence = new PlayerBuffTypesCommitRequest(target.Connection.Player.Slot, new BuffTypeId[] { new(48) });
        registry.PlayerBuffTypesUpdated(target.Connection, in presence);

        Assert.Equal(new byte[] { 10, 0, 55, 12, 48, 0, 8, 7, 0, 0 }, Dequeue(target.Outbound).Bytes.ToArray());
        Assert.Equal(0, target.Outbound.QueuedFrames);
        Assert.Equal(new byte[] { 8, 0, 50, 12, 48, 0, 0, 0 }, Dequeue(observer.Outbound).Bytes.ToArray());
        Assert.Equal(0, observer.Outbound.QueuedFrames);

        var late = AddPlaying(registry, 145823, 14);
        byte[][] frames = Drain(late.Outbound);
        Assert.DoesNotContain(frames, frame => frame[2] == 55);
        Assert.Equal(new byte[] { 8, 0, 50, 12, 48, 0, 0, 0 }, Assert.Single(frames, frame => frame[2] == 50));
        Assert.True(registry.TryGetLatestPlayerBuffFrame(target.Connection.Player.Slot, out var retained));
        Assert.Equal(new byte[] { 8, 0, 50, 12, 48, 0, 0, 0 }, retained.Bytes.ToArray());
    }

    private static (ConnectionHandle Connection, TerrariaConnectionOutboundQueue Outbound) AddPlaying(
        RuntimeConnectionRegistry registry, int connectionId, byte slot, uint generation = 1)
    {
        var outbound = CreateOutbound();
        var source = GameCommandSourceId.FromConnection(connectionId);
        var connection = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(generation)));
        Assert.True(registry.TryRegister(source, outbound));
        var spawn = Spawn(connection.Player.Slot);
        registry.PlayerSpawned(connection, in spawn);
        return (connection, outbound);
    }

    private static PlayerSpawnCommitRequest Spawn(PlayerSlotId slot) => new(slot, 100, 200, 0, 0, 0, 0, 0);

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 32768, maxFrameBytes: 1024));

    private static OutboundFrame Dequeue(TerrariaConnectionOutboundQueue outbound)
    {
        Assert.True(GetInnerQueue(outbound).TryRead(out var frame));
        return frame;
    }

    private static byte[][] Drain(TerrariaConnectionOutboundQueue outbound)
    {
        var frames = new List<byte[]>();
        while (GetInnerQueue(outbound).TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }

    private static BoundedOutboundQueue GetInnerQueue(TerrariaConnectionOutboundQueue outbound) =>
        (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
}
