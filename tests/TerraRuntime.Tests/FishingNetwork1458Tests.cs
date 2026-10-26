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
    public void Completion_intent_and_character_snapshot_keep_separate_authenticated_commands()
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

        // A claimed daily image asks for correction. Character counters remain a separate
        // snapshot command and its claimed wire actor cannot replace the authenticated occupation.
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(74, [40, 255])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(76, [249, 255, 255, 255, 127, 255, 255, 255, 127])));
        var correction = Assert.IsType<PublishAnglerStateRuntimeCommand>(ingress.Commands[1].Command);
        Assert.Equal(paid.Connection, correction.Connection);
        var progress = Assert.IsType<PlayerCharacterProgressRuntimeCommand>(ingress.Commands[2].Command);
        Assert.Equal(paid.Connection, progress.Connection);
        Assert.Equal(int.MaxValue, progress.AnglerQuestsFinished);
        Assert.Equal(int.MaxValue, progress.GolferScoreAccumulated);
        Assert.All(ingress.Commands, item => Assert.Equal(source, item.Source));
        Assert.Equal(3, ingress.Commands.Count);
        Assert.Equal(TerrariaFrameRejectionCategory.None, sink.RejectionCategory);
    }

    [Theory]
    [InlineData(7, -1, 1234)]
    [InlineData(7, 29, -1)]
    [InlineData(255, 29, 1234)]
    public void Character_counter_semantic_refusals_do_not_post_or_disconnect(byte claimed, int angler, int golf)
    {
        var source = GameCommandSourceId.FromConnection(4755);
        using var bootstrap = Bootstrap(source, playing: true);
        var ingress = new RecordingIngress();
        var sink = new FishingFrameSink1458(source, bootstrap, new ContinuingSink(), ingress);
        byte[] encoded = TerrariaPlayerCharacterProgressCodec1458.Encode(new(claimed, angler, golf));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(76, encoded[3..])));
        Assert.Equal(TerrariaFrameRejectionCategory.None, sink.RejectionCategory);
        Assert.Equal(0, ingress.Attempts);
        Assert.Empty(ingress.Commands);
    }

    [Theory]
    [InlineData(PlayerJoinState.AwaitingWorldRequest)]
    [InlineData(PlayerJoinState.AwaitingSectionRequest)]
    [InlineData(PlayerJoinState.AwaitingSpawn)]
    public void Packet76_is_not_admitted_as_a_vanilla_preplaying_character_baseline(PlayerJoinState state)
    {
        var source = GameCommandSourceId.FromConnection(4756);
        using var bootstrap = Bootstrap(source, playing: false);
        AdvanceToState(bootstrap, state);
        Assert.Equal(state, bootstrap.JoinState);
        var ingress = new RecordingIngress();
        var sink = new FishingFrameSink1458(source, bootstrap, new ContinuingSink(), ingress);
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(Frame(76, [3, 29, 0, 0, 0, 210, 4, 0, 0])));
        Assert.Equal(TerrariaFrameRejectionCategory.InvalidState, sink.RejectionCategory);
        Assert.Empty(ingress.Commands);
    }

    [Fact]
    public void Packet76_codec_preserves_independently_captured_original_signed_counter_bytes()
    {
        // Actual official Linux1458 MessageBuffer.GetData76 -> NetMessage.SendData76 observations.
        // Source maps claimed7 to whoAmI3, retains golf1234, and encodes even negative Int32 values.
        foreach (var row in new[]
        {
            (Hex: "0C004C031D000000D2040000", Angler: 29, Golf: 1234),
            (Hex: "0C004C03FFFFFFFF85FFFFFF", Angler: -1, Golf: -123)
        })
        {
            byte[] bytes = Convert.FromHexString(row.Hex);
            var frame = Frame(76, bytes[3..]);
            Assert.Equal(TerrariaPlayerCharacterProgressDecodeResult1458.Decoded,
                TerrariaPlayerCharacterProgressCodec1458.TryDecode(frame, out var state));
            Assert.Equal(new TerrariaPlayerCharacterProgressState1458(3, row.Angler, row.Golf), state);
            Assert.Equal(bytes, TerrariaPlayerCharacterProgressCodec1458.Encode(state));
        }
    }

    [Fact]
    public void Packet76_codec_bounds_lengths_and_decodes_every_two_segment_split()
    {
        byte[] payload = Convert.FromHexString("031D000000D2040000");
        for (int split = 1; split < payload.Length; split++)
        {
            var first = new Segment(payload.AsMemory(0, split));
            var last = first.Append(payload.AsMemory(split));
            var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
            var segmented = new TerrariaFrame(12, 76, ReadOnlySequence<byte>.Empty, sequence);
            Assert.Equal(TerrariaPlayerCharacterProgressDecodeResult1458.Decoded,
                TerrariaPlayerCharacterProgressCodec1458.TryDecode(segmented, out var state));
            Assert.Equal(new TerrariaPlayerCharacterProgressState1458(3, 29, 1234), state);
        }
        for (int size = 0; size < 32; size++)
        {
            if (size == TerrariaPlayerCharacterProgressCodec1458.PayloadLength) continue;
            var wrongLength = Frame(76, new byte[size]);
            Assert.Equal(TerrariaPlayerCharacterProgressDecodeResult1458.InvalidPayloadLength,
                TerrariaPlayerCharacterProgressCodec1458.TryDecode(wrongLength, out var state));
            Assert.Equal(default, state);
        }
        var wrongId = Frame(75, payload);
        Assert.Equal(TerrariaPlayerCharacterProgressDecodeResult1458.WrongMessageId,
            TerrariaPlayerCharacterProgressCodec1458.TryDecode(wrongId, out _));
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
        Assert.Equal(TerrariaFrameSinkResult.Continue, bounded.OnFrame(Frame(76, [7, 29, 0, 0, 0, 210, 4, 0, 0])));
        Assert.Equal(2, refusing.Attempts);
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
        registry.PublishAnglerState(current, 40, true, 123456, 1234);
        Assert.Equal(0, outbound.QueuedFrames);
        var spawn = new PlayerSpawnCommitRequest(new(7), 100, 200, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(current, spawn);
        while (frames.TryRead(out _)) { }
        registry.PublishAnglerState(current with { Player = new(new(7), new(2)) }, 40, true, 123456, 1234);
        Assert.Equal(0, outbound.QueuedFrames);
        registry.PublishAnglerState(current, 40, true, 123456, 1234);
        Assert.True(frames.TryRead(out var daily));
        Assert.Equal(new byte[] { 5, 0, 74, 40, 1 }, daily.Bytes.ToArray());
        Assert.True(frames.TryRead(out var progress));
        Assert.Equal(new byte[] { 12, 0, 76, 7, 64, 226, 1, 0, 210, 4, 0, 0 }, progress.Bytes.ToArray());
        Assert.False(frames.TryRead(out _));
    }

    [Fact]
    public void Character_progress_relays_both_counters_to_playing_peers_only_for_the_current_generation()
    {
        var source = GameCommandSourceId.FromConnection(4757);
        var peerSource = GameCommandSourceId.FromConnection(4758);
        var unjoinedSource = GameCommandSourceId.FromConnection(4759);
        var registry = new RuntimeConnectionRegistry();
        var sender = Queue(); var peer = Queue(); var unjoined = Queue();
        Assert.True(registry.TryRegister(source, sender));
        Assert.True(registry.TryRegister(peerSource, peer));
        Assert.True(registry.TryRegister(unjoinedSource, unjoined));
        var current = new ConnectionHandle(source, new(new(3), new(1)));
        var observer = new ConnectionHandle(peerSource, new(new(7), new(1)));
        registry.PlayerSpawned(current, new(new(3), 100, 200, 0, 0, 0, 0, 0));
        registry.PlayerSpawned(observer, new(new(7), 100, 200, 0, 0, 0, 0, 0));
        var senderFrames = Frames(sender); var peerFrames = Frames(peer); var unjoinedFrames = Frames(unjoined);
        while (senderFrames.TryRead(out _)) { }
        while (peerFrames.TryRead(out _)) { }
        while (unjoinedFrames.TryRead(out _)) { }
        registry.PublishCharacterProgress(current with { Player = new(new(3), new(2)) }, 29, 1234);
        registry.PublishCharacterProgress(new(unjoinedSource, new(new(8), new(1))), 29, 1234);
        Assert.False(peerFrames.TryRead(out _));
        registry.PublishCharacterProgress(current, 29, 1234);
        Assert.True(peerFrames.TryRead(out var progress));
        Assert.Equal(Convert.FromHexString("0C004C031D000000D2040000"), progress.Bytes.ToArray());
        Assert.False(peerFrames.TryRead(out _));
        Assert.False(senderFrames.TryRead(out _));
        Assert.False(unjoinedFrames.TryRead(out _));
    }

    private static BoundedOutboundQueue Frames(TerrariaConnectionOutboundQueue queue) =>
        (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;

    private static TerrariaConnectionOutboundQueue Queue() =>
        new(new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 8192, maxFrameBytes: 2048));

    private static PlayerBootstrapFrameSink Bootstrap(GameCommandSourceId source, bool playing)
    {
        var bootstrap = new PlayerBootstrapFrameSink(new PlayerSlotPool(1), Queue(),
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(), new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }), source, new SpawnIngress());
        if (!playing) return bootstrap;
        AdvanceToState(bootstrap, PlayerJoinState.Playing);
        return bootstrap;
    }

    private static void AdvanceToState(PlayerBootstrapFrameSink bootstrap, PlayerJoinState state)
    {
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame((byte)TerrariaMessageId.Hello,
            [11, (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a', (byte)'3', (byte)'2', (byte)'6'])));
        if (state == PlayerJoinState.AwaitingWorldRequest) return;
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame((byte)TerrariaMessageId.RequestWorldData, [])));
        if (state == PlayerJoinState.AwaitingSectionRequest) return;
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame((byte)TerrariaMessageId.SpawnTileData, new byte[9])));
        if (state == PlayerJoinState.AwaitingSpawn) return;
        var payload = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), 100);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 200);
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame((byte)TerrariaMessageId.PlayerSpawn, payload)));
        Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
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

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory) => Memory = memory;
        internal Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}
