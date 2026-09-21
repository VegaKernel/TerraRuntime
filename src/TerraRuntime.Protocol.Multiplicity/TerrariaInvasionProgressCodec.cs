using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Protocol-326 packet 78, emitted by <c>NPC.CheckProgressFrostMoon/PumpkinMoon</c>.</summary>
public readonly record struct TerrariaInvasionProgressState(int Progress, int Maximum, sbyte Icon, sbyte Wave);

public static class TerrariaInvasionProgressCodec
{
    public const int PayloadLength = 10;

    public static bool TryEncode(in TerrariaInvasionProgressState state, out byte[] frame)
    {
        if (state.Progress < 0 || state.Maximum < 0 || state.Wave < 0)
        {
            frame = [];
            return false;
        }

        frame = new byte[TerrariaFrameDecoderOptions.MinimumFrameLength + PayloadLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = (byte)TerrariaMessageId.ReportInvasionProgress;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(3), state.Progress);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(7), state.Maximum);
        frame[11] = unchecked((byte)state.Icon);
        frame[12] = unchecked((byte)state.Wave);
        return true;
    }
}
