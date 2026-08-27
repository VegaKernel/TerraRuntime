using System.IO.Pipelines;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaPipeFramePumpTests
{
    [Fact]
    public async Task Reads_a_frame_split_across_pipe_writes()
    {
        var pipe = new Pipe();
        var sink = new RecordingSink();
        ValueTask<TerrariaPipePumpResult> pump = TerrariaPipeFramePump.RunAsync(pipe.Reader, sink);

        await pipe.Writer.WriteAsync(new byte[] { 5 });
        await pipe.Writer.WriteAsync(new byte[] { 0, (byte)TerrariaMessageId.Kick, 0xAA, 0xBB });
        await pipe.Writer.CompleteAsync();

        Assert.Equal(TerrariaPipePumpResult.Completed, await pump);
        Assert.Single(sink.Frames);
        Assert.Equal((byte)TerrariaMessageId.Kick, sink.Frames[0].MessageId);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, sink.Frames[0].Payload);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task Reads_coalesced_frames_from_one_pipe_buffer()
    {
        var pipe = new Pipe();
        var sink = new RecordingSink();
        ValueTask<TerrariaPipePumpResult> pump = TerrariaPipeFramePump.RunAsync(pipe.Reader, sink);

        await pipe.Writer.WriteAsync(new byte[]
        {
            3, 0, (byte)TerrariaMessageId.Hello,
            4, 0, (byte)TerrariaMessageId.PlayerInfo, 0xCC
        });
        await pipe.Writer.CompleteAsync();

        Assert.Equal(TerrariaPipePumpResult.Completed, await pump);
        Assert.Equal(2, sink.Frames.Count);
        Assert.Equal((byte)TerrariaMessageId.Hello, sink.Frames[0].MessageId);
        Assert.Equal((byte)TerrariaMessageId.PlayerInfo, sink.Frames[1].MessageId);
        Assert.Equal(new byte[] { 0xCC }, sink.Frames[1].Payload);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task Reports_a_truncated_frame_when_the_writer_completes_mid_frame()
    {
        var pipe = new Pipe();
        var sink = new RecordingSink();
        ValueTask<TerrariaPipePumpResult> pump = TerrariaPipeFramePump.RunAsync(pipe.Reader, sink);

        await pipe.Writer.WriteAsync(new byte[] { 5, 0, (byte)TerrariaMessageId.Kick, 0xAA });
        await pipe.Writer.CompleteAsync();

        Assert.Equal(TerrariaPipePumpResult.TruncatedFrame, await pump);
        Assert.Empty(sink.Frames);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task Rejects_an_invalid_declared_length()
    {
        var pipe = new Pipe();
        var sink = new RecordingSink();
        ValueTask<TerrariaPipePumpResult> pump = TerrariaPipeFramePump.RunAsync(pipe.Reader, sink);

        await pipe.Writer.WriteAsync(new byte[] { 2, 0, (byte)TerrariaMessageId.Hello });
        await pipe.Writer.CompleteAsync();

        Assert.Equal(TerrariaPipePumpResult.InvalidFrameLength, await pump);
        Assert.Empty(sink.Frames);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task Rejects_an_oversized_declared_frame_without_waiting_for_its_body()
    {
        var pipe = new Pipe();
        var sink = new RecordingSink();
        var options = new TerrariaFrameDecoderOptions(maxFrameLength: 5);
        ValueTask<TerrariaPipePumpResult> pump = TerrariaPipeFramePump.RunAsync(pipe.Reader, sink, options);

        await pipe.Writer.WriteAsync(new byte[] { 6, 0 });

        Assert.Equal(TerrariaPipePumpResult.FrameTooLarge, await pump);
        Assert.Empty(sink.Frames);
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task Stops_immediately_when_the_sink_requests_it()
    {
        var pipe = new Pipe();
        var sink = new RecordingSink(stopAfter: 1);
        ValueTask<TerrariaPipePumpResult> pump = TerrariaPipeFramePump.RunAsync(pipe.Reader, sink);

        await pipe.Writer.WriteAsync(new byte[]
        {
            3, 0, (byte)TerrariaMessageId.Hello,
            3, 0, (byte)TerrariaMessageId.PlayerInfo
        });

        Assert.Equal(TerrariaPipePumpResult.SinkStopped, await pump);
        Assert.Single(sink.Frames);
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    private sealed class RecordingSink(int stopAfter = int.MaxValue) : ITerrariaFrameSink
    {
        public List<RecordedFrame> Frames { get; } = [];

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
        {
            Frames.Add(new RecordedFrame(frame.MessageId, frame.Payload.ToArray()));
            return Frames.Count >= stopAfter
                ? TerrariaFrameSinkResult.Stop
                : TerrariaFrameSinkResult.Continue;
        }
    }

    private sealed record RecordedFrame(byte MessageId, byte[] Payload);
}
