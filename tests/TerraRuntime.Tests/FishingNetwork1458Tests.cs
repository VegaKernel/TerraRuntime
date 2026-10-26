using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class FishingNetwork1458Tests
{
    [Fact]
    public void Completion_and_forged_state_frames_become_authenticated_intents_without_imported_counters()
    {
        var source = GameCommandSourceId.FromConnection(4751);
        using var bootstrap = Bootstrap(source, playing: true);
        var ingress = new RecordingIngress();
        var sink = new FishingFrameSink1458(source, bootstrap, new ContinuingSink(), ingress);
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(75, [])));
        var paid = Assert.IsType<CompleteAnglerQuestRuntimeCommand>(Assert.Single(ingress.Commands).Command);
        Assert.Equal(source, paid.Connection.Source);
        Assert.Equal(bootstrap.AssignedPlayerHandle, paid.Connection.Player);
        Assert.Equal(source, ingress.Commands[0].Source);

        // Packet74 contains a forged quest/finished image; packet76 contains a
        // forged player slot and Int32 counters. Neither enters mutable state.
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(74, [40, 255])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(76, [249, 255, 255, 255, 127, 255, 255, 255, 127])));
        foreach (var item in ingress.Commands.Skip(1))
        {
            var correction = Assert.IsType<PublishAnglerStateRuntimeCommand>(item.Command);
            Assert.Equal(paid.Connection, correction.Connection);
            Assert.Equal(source, item.Source);
        }
        Assert.Equal(3, ingress.Commands.Count);
        Assert.Equal(TerrariaFrameRejectionCategory.None, sink.RejectionCategory);
    }

    [Theory]
    [InlineData(74, 1)]
    [InlineData(74, 3)]
    [InlineData(75, 1)]
    [InlineData(76, 8)]
    [InlineData(76, 10)]
    public void Malformed_Angler_payloads_stop_at_the_bounded_boundary(byte message, int size)
    {
        var source = GameCommandSourceId.FromConnection(4752);
        using var bootstrap = Bootstrap(source, playing: true);
        var ingress = new RecordingIngress();
        var sink = new FishingFrameSink1458(source, bootstrap, new ContinuingSink(), ingress);
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(Frame(message, new byte[size])));
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol, sink.RejectionCategory);
        Assert.Empty(ingress.Commands);
    }

    [Fact]
    public void Unjoined_completion_is_rejected_and_backpressure_does_not_grant_or_retain_an_intent()
    {
        var source = GameCommandSourceId.FromConnection(4753);
        using var unjoined = Bootstrap(source, playing: false);
        var ingress = new RecordingIngress();
        var sink = new FishingFrameSink1458(source, unjoined, new ContinuingSink(), ingress);
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(Frame(75, [])));
        Assert.Equal(TerrariaFrameRejectionCategory.InvalidState, sink.RejectionCategory);
        Assert.Empty(ingress.Commands);
        using var playing = Bootstrap(source, playing: true);
        var refusing = new RecordingIngress { Accept = false };
        var bounded = new FishingFrameSink1458(source, playing, new ContinuingSink(), refusing);
        Assert.Equal(TerrariaFrameSinkResult.Continue, bounded.OnFrame(Frame(75, [])));
        Assert.Equal(1, refusing.Attempts);
        Assert.Empty(refusing.Commands);
    }

    [Fact]
    public void Source_74_and_76_bytes_are_addressed_only_to_the_current_playing_occupation()
    {
        var source = GameCommandSourceId.FromConnection(4754);
        var registry = new RuntimeConnectionRegistry();
        var outbound = Queue();
        var frames = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
        Assert.True(registry.TryRegister(source, outbound));
        var current = new ConnectionHandle(source, new(new(7), new(1)));
        registry.PublishAnglerState(current, 40, true, 123456);
        Assert.Equal(0, outbound.QueuedFrames);
        var spawn = new PlayerSpawnCommitRequest(new(7), 100, 200, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(current, spawn);
        while (frames.TryRead(out _)) { }
        registry.PublishAnglerState(current with { Player = new(new(7), new(2)) }, 40, true, 123456);
        Assert.Equal(0, outbound.QueuedFrames);
        registry.PublishAnglerState(current, 40, true, 123456);
        Assert.True(frames.TryRead(out var daily));
        Assert.Equal(new byte[] { 5, 0, 74, 40, 1 }, daily.Bytes.ToArray());
        Assert.True(frames.TryRead(out var progress));
        Assert.Equal(new byte[] { 12, 0, 76, 7, 64, 226, 1, 0, 0, 0, 0, 0 }, progress.Bytes.ToArray());
        Assert.False(frames.TryRead(out _));
    }

    private static TerrariaConnectionOutboundQueue Queue() =>
        new(new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 8192, maxFrameBytes: 2048));

    private static PlayerBootstrapFrameSink Bootstrap(GameCommandSourceId source, bool playing)
    {
        var bootstrap = new PlayerBootstrapFrameSink(new PlayerSlotPool(1), Queue(),
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(), new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }), source, new SpawnIngress());
        if (!playing) return bootstrap;
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame((byte)TerrariaMessageId.Hello,
            [11, (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a', (byte)'3', (byte)'2', (byte)'6'])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame((byte)TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame((byte)TerrariaMessageId.SpawnTileData, new byte[9])));
        var payload = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), 100);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 200);
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame((byte)TerrariaMessageId.PlayerSpawn, payload)));
        Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
        return bootstrap;
    }

    private static TerrariaFrame Frame(byte message, byte[] payload) => new(checked((ushort)(3 + payload.Length)),
        message, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(payload));

    private sealed class SpawnIngress : IPlayerSpawnCommitIngress
    {
        public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request) =>
            session.TryCommitSpawn(request.ClaimedSlot) == PlayerSpawnCommitResult.Committed;
    }

    private sealed class ContinuingSink : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }

    private sealed class RecordingIngress : IGameCommandIngress<RuntimeCommand>
    {
        internal readonly List<(GameCommandSourceId Source, RuntimeCommand Command)> Commands = [];
        internal bool Accept = true;
        internal int Attempts;
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Attempts++;
            if (!Accept) return false;
            Commands.Add((source, command));
            return true;
        }
    }
}
