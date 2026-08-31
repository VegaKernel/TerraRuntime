using System.IO;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Write-only stream over an exact-size final byte array. Writes beyond the supplied storage are counted but
/// not exposed, allowing callers to reject a packet whose model under-reports <c>GetLength()</c> without growing
/// a second temporary buffer. The backing array is safe to publish only when <see cref="Overflowed"/> is false
/// and <see cref="WrittenCount"/> exactly matches its length.
/// </summary>
internal sealed class FixedBufferWriteStream : Stream
{
    private readonly byte[] buffer;
    private int storedCount;
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

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return;

        int nextWrittenCount = checked(writtenCount + source.Length);
        int remaining = buffer.Length - storedCount;
        int copyLength = Math.Min(remaining, source.Length);
        if (copyLength > 0)
        {
            source[..copyLength].CopyTo(buffer.AsSpan(storedCount, copyLength));
            storedCount += copyLength;
        }

        if (copyLength != source.Length)
            overflowed = true;

        writtenCount = nextWrittenCount;
    }

    public override void WriteByte(byte value)
    {
        writtenCount = checked(writtenCount + 1);
        if (storedCount < buffer.Length)
        {
            buffer[storedCount++] = value;
            return;
        }

        overflowed = true;
    }
}
