using System.Buffers;

namespace TerraRuntime.Protocol;

public readonly record struct TerrariaFrame(
    ushort PacketLength,
    byte MessageId,
    ReadOnlySequence<byte> Packet,
    ReadOnlySequence<byte> Payload);
