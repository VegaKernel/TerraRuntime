from pathlib import Path

registry_path = Path("src/TerraRuntime/RuntimeSignReplicationRegistry.cs")
processor_path = Path("src/TerraRuntime/RuntimeSignCommandProcessor.cs")
test_path = Path("tests/TerraRuntime.Tests/RuntimeSignCommandProcessorTests.cs")

registry = registry_path.read_text(encoding="utf-8")
processor = processor_path.read_text(encoding="utf-8")

if "public void PublishChanged(ConnectionHandle source, WorldSign sign)" not in registry:
    replacements = [
        (
            "!TryCreateState(sign, connection.Player.Slot.Value, flags: 0, out TerrariaSignState state)",
            "!TryCreateState(sign, connection.Player.Slot.Value, out TerrariaSignState state)",
        ),
        (
            "public void PublishChanged(ConnectionHandle source, WorldSign sign, byte flags)",
            "public void PublishChanged(ConnectionHandle source, WorldSign sign)",
        ),
        (
            "!TryCreateState(sign, source.Player.Slot.Value, flags, out TerrariaSignState state)",
            "!TryCreateState(sign, source.Player.Slot.Value, out TerrariaSignState state)",
        ),
        (
            "        byte player,\n        byte flags,\n        out TerrariaSignState state)",
            "        byte player,\n        out TerrariaSignState state)",
        ),
        (
            "            sign.Text,\n            player,\n            flags);",
            "            sign.Text,\n            player,\n            Flags: 0);",
        ),
    ]
    for old, new in replacements:
        count = registry.count(old)
        if count != 1:
            raise SystemExit(f"registry contract pattern count={count}: {old!r}")
        registry = registry.replace(old, new, 1)

if "replication.PublishChanged(command.Connection, committed);" not in processor:
    old = "replication.PublishChanged(command.Connection, committed, flags: 0);"
    count = processor.count(old)
    if count != 1:
        raise SystemExit(f"processor contract pattern count={count}")
    processor = processor.replace(old, "replication.PublishChanged(command.Connection, committed);", 1)

registry_path.write_text(registry, encoding="utf-8")
processor_path.write_text(processor, encoding="utf-8")

if not test_path.exists():
    test_path.write_text(r'''using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeSignCommandProcessorTests
{
    [Fact]
    public void Read_targets_requester_with_authoritative_player_and_zero_flags()
    {
        var store = new RuntimeSignStore([new WorldSign(0, "hello", 10, 20)]);
        var replication = new RuntimeSignReplicationRegistry();
        var processor = new RuntimeSignCommandProcessor(store, replication);
        ConnectionHandle owner = Connection(1, playerSlot: 4, generation: 1);
        ConnectionHandle observer = Connection(2, playerSlot: 7, generation: 1);
        var ownerOutbound = Outbound();
        var observerOutbound = Outbound();

        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));
        MarkPlaying(replication, owner);
        MarkPlaying(replication, observer);

        Assert.True(processor.TryApply(
            new ClientSignReadRuntimeCommand(owner, new TerrariaSignReadRequest(10, 20))));

        Assert.Equal(1, processor.AppliedReads);
        Assert.Equal(0, processor.RejectedReads);
        Assert.Equal(1, replication.ReadFrames);
        Assert.Equal(1, ownerOutbound.QueuedFrames);
        Assert.Equal(0, observerOutbound.QueuedFrames);
        Assert.Equal(new TerrariaSignState(0, 10, 20, "hello", 4, 0), ReadState(ownerOutbound));
    }

    [Fact]
    public void Changed_update_excludes_sender_and_clears_client_flags_on_observer_frame()
    {
        var store = new RuntimeSignStore([new WorldSign(0, "before", 10, 20)]);
        var replication = new RuntimeSignReplicationRegistry();
        var processor = new RuntimeSignCommandProcessor(store, replication);
        ConnectionHandle owner = Connection(11, playerSlot: 3, generation: 2);
        ConnectionHandle observer = Connection(12, playerSlot: 5, generation: 1);
        var ownerOutbound = Outbound();
        var observerOutbound = Outbound();

        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));
        MarkPlaying(replication, owner);
        MarkPlaying(replication, observer);

        var submitted = new TerrariaSignState(0, 10, 20, "after", Player: 99, Flags: 0x7F);
        Assert.True(processor.TryApply(new ClientSignUpdateRuntimeCommand(owner, submitted)));

        Assert.Equal(1, processor.AppliedUpdates);
        Assert.Equal(0, processor.RejectedUpdates);
        Assert.Equal(0, ownerOutbound.QueuedFrames);
        Assert.Equal(1, observerOutbound.QueuedFrames);
        Assert.Equal(1, replication.UpdateFrames);

        TerrariaSignState state = ReadState(observerOutbound);
        Assert.Equal((short)0, state.SignId);
        Assert.Equal((short)10, state.TileX);
        Assert.Equal((short)20, state.TileY);
        Assert.Equal("after", state.Text);
        Assert.Equal((byte)3, state.Player);
        Assert.Equal((byte)0, state.Flags);

        Assert.True(store.TryCaptureCanonicalSnapshot(out WorldSign[] snapshot));
        Assert.Equal("after", Assert.Single(snapshot).Text);
    }

    [Fact]
    public void Identical_update_is_applied_without_observer_broadcast()
    {
        var store = new RuntimeSignStore([new WorldSign(0, "same", 10, 20)]);
        var replication = new RuntimeSignReplicationRegistry();
        var processor = new RuntimeSignCommandProcessor(store, replication);
        ConnectionHandle owner = Connection(21, playerSlot: 1, generation: 1);
        ConnectionHandle observer = Connection(22, playerSlot: 2, generation: 1);
        var ownerOutbound = Outbound();
        var observerOutbound = Outbound();

        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));
        MarkPlaying(replication, owner);
        MarkPlaying(replication, observer);

        var submitted = new TerrariaSignState(0, 10, 20, "same", Player: 88, Flags: 9);
        Assert.True(processor.TryApply(new ClientSignUpdateRuntimeCommand(owner, submitted)));

        Assert.Equal(1, processor.AppliedUpdates);
        Assert.Equal(0, processor.RejectedUpdates);
        Assert.Equal(0, replication.UpdateFrames);
        Assert.Equal(0, ownerOutbound.QueuedFrames);
        Assert.Equal(0, observerOutbound.QueuedFrames);
    }

    [Fact]
    public void Invalid_update_is_consumed_and_rejected_without_mutation_or_replication()
    {
        var store = new RuntimeSignStore([new WorldSign(0, "before", 10, 20)]);
        var replication = new RuntimeSignReplicationRegistry();
        var processor = new RuntimeSignCommandProcessor(store, replication);
        ConnectionHandle owner = Connection(31, playerSlot: 1, generation: 1);
        var outbound = Outbound();

        Assert.True(replication.TryRegister(owner.Source, outbound));
        MarkPlaying(replication, owner);

        var submitted = new TerrariaSignState(0, 11, 20, "wrong coordinate", Player: 1, Flags: 0);
        Assert.True(processor.TryApply(new ClientSignUpdateRuntimeCommand(owner, submitted)));

        Assert.Equal(0, processor.AppliedUpdates);
        Assert.Equal(1, processor.RejectedUpdates);
        Assert.Equal(0, outbound.QueuedFrames);
        Assert.True(store.TryRead(10, 20, out WorldSign unchanged));
        Assert.Equal("before", unchanged.Text);
    }

    private static TerrariaSignState ReadState(TerrariaConnectionOutboundQueue outbound)
    {
        Assert.True(outbound.InnerQueue.TryRead(out OutboundFrame encoded));
        var buffer = new ReadOnlySequence<byte>(encoded.Bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.True(buffer.IsEmpty);
        Assert.Equal(TerrariaSignDecodeResult.Decoded, TerrariaSignCodec.TryDecodeState(in frame, out TerrariaSignState state));
        return state;
    }

    private static void MarkPlaying(RuntimeSignReplicationRegistry replication, ConnectionHandle connection)
    {
        var spawn = new PlayerSpawnCommitRequest(
            connection.Player.Slot,
            SpawnX: 0,
            SpawnY: 0,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 0,
            SpawnContext: 0);
        replication.PlayerSpawned(connection, in spawn);
    }

    private static TerrariaConnectionOutboundQueue Outbound() =>
        new(new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 64 * 1024, maxFrameBytes: 8 * 1024));

    private static ConnectionHandle Connection(long connectionId, byte playerSlot, ulong generation) =>
        new(
            GameCommandSourceId.FromConnection(connectionId),
            new PlayerHandle(
                new PlayerSlotId(playerSlot),
                new PlayerSessionGeneration(generation)));
}
''', encoding="utf-8")

print("applied vanilla sign replication contract hardening")
