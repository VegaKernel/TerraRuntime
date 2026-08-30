using System.Buffers;
using System.IO;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Minimal write-only <see cref="Stream"/> adapter over an <see cref="IBufferWriter{Byte}"/>.
/// Exists to bridge Multiplicity's <c>TerrariaPacket.ToStream(Stream)</c> API to the
/// runtime's preferred <c>IBufferWriter</c>/<c>Span</c> hot path without allocating a <see cref="MemoryStream"/>.
/// Only write operations are supported; read/seek are intentionally unsupported.
/// </summary>
internal sealed class ArrayBufferWriterStream : Stream
{
    private readonly IBufferWriter<byte> writer;
    private long length;

    public ArrayBufferWriterStream(IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => length;

    public override long Position
    {
        get => length;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return;

        Span<byte> destination = writer.GetSpan(buffer.Length);
        buffer.CopyTo(destination);
        writer.Advance(buffer.Length);
        length += buffer.Length;
    }

    public override void WriteByte(byte value)
    {
        Span<byte> destination = writer.GetSpan(1);
        destination[0] = value;
        writer.Advance(1);
        length++;
    }
}
