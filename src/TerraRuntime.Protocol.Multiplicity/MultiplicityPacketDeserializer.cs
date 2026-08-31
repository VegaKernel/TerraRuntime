using System.Buffers;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Deserializes Multiplicity packet models from TerraRuntime frames without allocating a fresh payload array
/// for fragmented receives. Single-segment payloads are borrowed directly; fragmented payloads use a bounded
/// ArrayPool lease that is returned before the decoded packet crosses the protocol boundary.
/// </summary>
internal static class MultiplicityPacketDeserializer
{
    public static bool TryDeserialize(in TerrariaFrame frame, out TerrariaPacket packet)
    {
        try
        {
            using var payload = PayloadLease.Create(in frame);
            return TerrariaPacket.TryDeserializePayload(frame.MessageId, payload.Memory, out packet);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            EndOfStreamException or
            IOException or
            OverflowException or
            FormatException or
            ArgumentException)
        {
            packet = null!;
            return false;
        }
    }

    private ref struct PayloadLease
    {
        private byte[]? _rented;

        private PayloadLease(ReadOnlyMemory<byte> memory, byte[]? rented)
        {
            Memory = memory;
            _rented = rented;
        }

        public ReadOnlyMemory<byte> Memory { get; private set; }

        public static PayloadLease Create(in TerrariaFrame frame)
        {
            int length = checked((int)frame.Payload.Length);
            if (frame.Payload.IsSingleSegment)
                return new PayloadLease(frame.Payload.First, rented: null);

            byte[] rented = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
            try
            {
                int offset = 0;
                foreach (ReadOnlyMemory<byte> segment in frame.Payload)
                {
                    segment.Span.CopyTo(rented.AsSpan(offset, segment.Length));
                    offset += segment.Length;
                }

                return new PayloadLease(rented.AsMemory(0, length), rented);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(rented);
                throw;
            }
        }

        public void Dispose()
        {
            byte[]? rented = _rented;
            _rented = null;
            Memory = default;
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
