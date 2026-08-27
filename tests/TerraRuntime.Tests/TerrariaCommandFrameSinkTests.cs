using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaCommandFrameSinkTests
{
    [Fact]
    public void Decoded_command_is_posted_with_the_connection_source()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(17);
        var ingress = new RecordingIngress(accept: true);
        var sink = new TerrariaCommandFrameSink<ProbeCommand>(source, new ProbeDecoder(), ingress);
        TerrariaFrame frame = CreateFrame(messageId: 42, payload: new byte[] { 7 });

        TerrariaFrameSinkResult result = sink.OnFrame(in frame);

        Assert.Equal(TerrariaFrameSinkResult.Continue, result);
        Assert.Equal(TerrariaCommandFrameSinkStopReason.None, sink.StopReason);
        Assert.Equal(source, ingress.Source);
        Assert.Equal(new ProbeCommand(7), ingress.Command);
    }

    [Fact]
    public void Authoritative_backpressure_stops_the_connection_sink()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(3);
        var ingress = new RecordingIngress(accept: false);
        var sink = new TerrariaCommandFrameSink<ProbeCommand>(source, new ProbeDecoder(), ingress);
        TerrariaFrame frame = CreateFrame(messageId: 42, payload: new byte[] { 9 });

        TerrariaFrameSinkResult result = sink.OnFrame(in frame);

        Assert.Equal(TerrariaFrameSinkResult.Stop, result);
        Assert.Equal(TerrariaCommandFrameSinkStopReason.GameLoopBackpressure, sink.StopReason);
    }

    [Fact]
    public void Malformed_command_stops_before_it_reaches_the_game_loop()
    {
        var ingress = new RecordingIngress(accept: true);
        var sink = new TerrariaCommandFrameSink<ProbeCommand>(
            GameCommandSourceId.FromConnection(5),
            new ProbeDecoder(),
            ingress);
        TerrariaFrame frame = CreateFrame(messageId: 42, payload: ReadOnlyMemory<byte>.Empty);

        TerrariaFrameSinkResult result = sink.OnFrame(in frame);

        Assert.Equal(TerrariaFrameSinkResult.Stop, result);
        Assert.Equal(TerrariaCommandFrameSinkStopReason.MalformedCommand, sink.StopReason);
        Assert.Null(ingress.Command);
    }

    [Fact]
    public void Unrelated_frames_are_ignored_without_touching_ingress()
    {
        var ingress = new RecordingIngress(accept: true);
        var sink = new TerrariaCommandFrameSink<ProbeCommand>(
            GameCommandSourceId.FromConnection(8),
            new ProbeDecoder(),
            ingress);
        TerrariaFrame frame = CreateFrame(messageId: 99, payload: new byte[] { 1 });

        TerrariaFrameSinkResult result = sink.OnFrame(in frame);

        Assert.Equal(TerrariaFrameSinkResult.Continue, result);
        Assert.Null(ingress.Command);
    }

    private static TerrariaFrame CreateFrame(byte messageId, ReadOnlyMemory<byte> payload)
    {
        var payloadSequence = new ReadOnlySequence<byte>(payload);
        return new TerrariaFrame(
            PacketLength: checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            MessageId: messageId,
            Packet: payloadSequence,
            Payload: payloadSequence);
    }

    private sealed record ProbeCommand(byte Value);

    private sealed class ProbeDecoder : ITerrariaCommandDecoder<ProbeCommand>
    {
        public TerrariaCommandDecodeResult TryDecode(in TerrariaFrame frame, out ProbeCommand command)
        {
            if (frame.MessageId != 42)
            {
                command = default!;
                return TerrariaCommandDecodeResult.Ignored;
            }

            if (frame.Payload.Length != 1)
            {
                command = default!;
                return TerrariaCommandDecodeResult.Malformed;
            }

            command = new ProbeCommand(frame.Payload.FirstSpan[0]);
            return TerrariaCommandDecodeResult.Decoded;
        }
    }

    private sealed class RecordingIngress(bool accept) : IGameCommandIngress<ProbeCommand>
    {
        public GameCommandSourceId? Source { get; private set; }

        public ProbeCommand? Command { get; private set; }

        public bool TryPost(GameCommandSourceId source, ProbeCommand command)
        {
            Source = source;
            Command = command;
            return accept;
        }
    }
}
