using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class TerrariaOutboundFrameWriterTests
{
    [Fact]
    public async Task Drains_frames_in_order_and_completes_when_the_queue_closes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(4, 64, 32));
        using var stream = new MemoryStream();

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[] { 1, 2, 3 })));
        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[] { 4, 5 })));
        Assert.True(queue.Complete());

        OutboundWriterResult result = await TerrariaOutboundFrameWriter.RunAsync(stream, queue, cancellationToken);

        Assert.Equal(OutboundWriterStopReason.Completed, result.Reason);
        Assert.Equal(2, result.FramesWritten);
        Assert.Equal(5, result.BytesWritten);
        Assert.Null(result.Error);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, stream.ToArray());
    }

    [Fact]
    public async Task Reports_queue_failure_after_draining_frames_already_accepted()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(2, 32, 16));
        using var stream = new MemoryStream();
        var expected = new InvalidDataException("queue failed");

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[] { 7, 8, 9 })));
        Assert.True(queue.Complete(expected));

        OutboundWriterResult result = await TerrariaOutboundFrameWriter.RunAsync(stream, queue, cancellationToken);

        Assert.Equal(OutboundWriterStopReason.QueueFailure, result.Reason);
        Assert.Equal(1, result.FramesWritten);
        Assert.Equal(3, result.BytesWritten);
        Assert.Same(expected, result.Error);
        Assert.Equal(new byte[] { 7, 8, 9 }, stream.ToArray());
    }

    [Fact]
    public async Task Reports_io_failure_without_claiming_the_failed_frame_was_written()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(2, 32, 16));
        await using var stream = new FailingWriteStream();

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[] { 1, 2, 3 })));

        OutboundWriterResult result = await TerrariaOutboundFrameWriter.RunAsync(stream, queue, cancellationToken);

        Assert.Equal(OutboundWriterStopReason.IoFailure, result.Reason);
        Assert.Equal(0, result.FramesWritten);
        Assert.Equal(0, result.BytesWritten);
        Assert.IsType<IOException>(result.Error);
    }

    private sealed class FailingWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("write failed");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("write failed"));
    }
}
