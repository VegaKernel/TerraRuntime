namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct PacketHeaderInfo(
    ushort PacketLength,
    byte MessageId,
    int PayloadLength);
