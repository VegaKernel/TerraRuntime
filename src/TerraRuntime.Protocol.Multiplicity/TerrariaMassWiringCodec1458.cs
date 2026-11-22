using System.Buffers;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaMassWiringState1458(short StartX, short StartY, short EndX, short EndY, byte Mode);

/// <summary>MessageBuffer109 and NetMessage110, Terraria 1.4.5.8 / protocol326.</summary>
public static class TerrariaMassWiringCodec1458
{
    public static bool TryDecode(in TerrariaFrame frame, out TerrariaMassWiringState1458 state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.MassWireOperation || frame.Payload.Length != 9) return false;
        Span<byte> payload = stackalloc byte[9]; frame.Payload.CopyTo(payload);
        var packet = MassWireOperationView.FromPayload(payload);
        state = new(packet.StartX, packet.StartY, packet.EndX, packet.EndY, (byte)packet.ToolMode);
        return true;
    }

    public static byte[] Encode(in TerrariaMassWiringState1458 state) => new MassWireOperation
    {
        StartX = state.StartX, StartY = state.StartY, EndX = state.EndX, EndY = state.EndY,
        ToolMode = (MassWireOperationToolMode)state.Mode
    }.ToArray();

    public static byte[] EncodePayment(short item, short quantity, byte player) =>
        new MassWireOperationPay { ItemType = item, Quantity = quantity, PlayerId = player }.ToArray();
}
