using System.IO;
using System.Runtime.CompilerServices;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Write-only stream over an exact-size final byte array. The normal write path is deliberately minimal because
/// every Multiplicity serializer reaches it repeatedly through <see cref="BinaryWriter"/>. An over-write marks the
/// stream invalid and discards the candidate frame; invalid packets are not worth partially copying into storage.
/// The backing array is safe to publish only when <see cref="Overflowed"/> is false and
/// <see cref="WrittenCount"/> exactly matches its length.
/// </summary>
internal sealed class FixedBufferWriteStream : Stream
{
    private readonly byte[] buffer;
    private int writtenCount;
    private bool overflowed;

    public FixedBufferWriteStream(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        this.buffer = buffer;
    }

    public int WrittenCount => writtenCount;

    public bool Overflowed => overflowed;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => writtenCount;

    public override long Position
    {
        get => writtenCount;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return;

        int offset = writtenCount;
        writtenCount = checked(offset + source.Length);

        // The successful path is one bounds check plus one copy. Once overflow occurs the entire candidate frame
        // is rejected, so copying a partial prefix only burns CPU and cannot improve diagnostics or correctness.
        if ((uint)offset <= (uint)buffer.Length && source.Length <= buffer.Length - offset)
        {
            source.CopyTo(buffer.AsSpan(offset));
            return;
        }

        overflowed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteByte(byte value)
    {
        int offset = writtenCount;
        writtenCount = checked(offset + 1);
        if ((uint)offset < (uint)buffer.Length)
        {
            buffer[offset] = value;
            return;
        }

        overflowed = true;
    }
}
