namespace TerraRuntime.Network;

/// <summary>
/// An already encoded immutable outbound frame.
/// The underlying memory must not be mutated while the frame is queued or being written.
/// </summary>
public readonly record struct OutboundFrame(ReadOnlyMemory<byte> Bytes)
{
    public int Length => Bytes.Length;
}
