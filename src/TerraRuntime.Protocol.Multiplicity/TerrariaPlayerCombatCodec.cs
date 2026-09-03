using System.Buffers;
using System.Text;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Source-pinned adapters for TerrariaServer 1.4.5.8 packets 30, 45 and 117.</summary>
public static class TerrariaPlayerCombatCodec
{
    public static bool TryDecodePvpToggle(in TerrariaFrame frame, out byte player, out bool hostile)
    {
        player = 0;
        hostile = false;
        if (frame.MessageId != (byte)TerrariaMessageId.TogglePvp || frame.Payload.Length != 2)
            return false;
        Span<byte> payload = stackalloc byte[2];
        frame.Payload.CopyTo(payload);
        player = payload[0];
        hostile = payload[1] != 0;
        return payload[1] is 0 or 1;
    }

    public static bool TryDecodeTeam(in TerrariaFrame frame, out byte player, out byte team)
    {
        player = 0;
        team = 0;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerTeam || frame.Payload.Length != 2)
            return false;
        Span<byte> payload = stackalloc byte[2];
        frame.Payload.CopyTo(payload);
        player = payload[0];
        team = payload[1];
        return team <= 5;
    }

    public static TerrariaPlayerHurtDecodeResult TryDecodeHurt(
        in TerrariaFrame frame,
        out TerrariaPlayerHurtState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerHurt)
            return TerrariaPlayerHurtDecodeResult.WrongMessageId;
        if (frame.Payload.Length < 6 || frame.Payload.Length > 1024)
            return TerrariaPlayerHurtDecodeResult.InvalidPayload;

        try
        {
            byte[] payload = frame.Payload.ToArray();
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            byte target = reader.ReadByte();
            TerrariaPlayerDeathReasonState reason = ReadReason(reader);
            short damage = reader.ReadInt16();
            byte direction = reader.ReadByte();
            byte flags = reader.ReadByte();
            sbyte cooldown = reader.ReadSByte();
            if (stream.Position != stream.Length)
                return TerrariaPlayerHurtDecodeResult.InvalidPayload;

            state = new TerrariaPlayerHurtState(target, reason, damage, direction, flags, cooldown);
            return state.IsStructurallyValid
                ? TerrariaPlayerHurtDecodeResult.Decoded
                : TerrariaPlayerHurtDecodeResult.InvalidState;
        }
        catch (EndOfStreamException)
        {
            return TerrariaPlayerHurtDecodeResult.InvalidPayload;
        }
        catch (IOException)
        {
            return TerrariaPlayerHurtDecodeResult.InvalidPayload;
        }
        catch (ArgumentException)
        {
            return TerrariaPlayerHurtDecodeResult.InvalidPayload;
        }
    }

    public static TerrariaPlayerHurtEncodeResult TryEncodeHurt(
        in TerrariaPlayerHurtState state,
        out byte[] frame)
    {
        frame = [];
        if (!state.IsStructurallyValid || state.Damage < 0)
            return TerrariaPlayerHurtEncodeResult.InvalidState;

        try
        {
            using var payloadStream = new MemoryStream(64);
            using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(state.TargetPlayer);
                TerrariaPlayerDeathReasonState reason = state.Reason;
                WriteReason(writer, in reason);
                writer.Write(state.Damage);
                writer.Write(state.HitDirectionWire);
                writer.Write(state.Flags);
                writer.Write(state.CooldownCounter);
            }

            byte[] payload = payloadStream.ToArray();
            frame = new byte[payload.Length + TerrariaFrameDecoderOptions.MinimumFrameLength];
            return TerrariaFrameEncoder.TryWrite(frame, (byte)TerrariaMessageId.PlayerHurt, payload) == TerrariaFrameWriteResult.Written
                ? TerrariaPlayerHurtEncodeResult.Encoded
                : TerrariaPlayerHurtEncodeResult.Failed;
        }
        catch (IOException)
        {
            frame = [];
            return TerrariaPlayerHurtEncodeResult.Failed;
        }
    }

    private static TerrariaPlayerDeathReasonState ReadReason(BinaryReader reader)
    {
        byte bits = reader.ReadByte();
        short player = (bits & 0x01) != 0 ? reader.ReadInt16() : (short)-1;
        short npc = (bits & 0x02) != 0 ? reader.ReadInt16() : (short)-1;
        short projectile = (bits & 0x04) != 0 ? reader.ReadInt16() : (short)-1;
        short other = (bits & 0x08) != 0 ? reader.ReadByte() : (short)-1;
        short projectileType = (bits & 0x10) != 0 ? reader.ReadInt16() : (short)0;
        short itemType = (bits & 0x20) != 0 ? reader.ReadInt16() : (short)0;
        short itemPrefix = (bits & 0x40) != 0 ? reader.ReadByte() : (short)0;
        string? custom = (bits & 0x80) != 0 ? reader.ReadString() : null;
        return new TerrariaPlayerDeathReasonState(player, npc, projectile, other, projectileType, itemType, itemPrefix, custom);
    }

    private static void WriteReason(BinaryWriter writer, in TerrariaPlayerDeathReasonState reason)
    {
        byte bits = 0;
        if (reason.SourcePlayer >= 0) bits |= 0x01;
        if (reason.SourceNpc >= 0) bits |= 0x02;
        if (reason.SourceProjectileLocalIndex >= 0) bits |= 0x04;
        if (reason.SourceOther >= 0) bits |= 0x08;
        if (reason.SourceProjectileType != 0) bits |= 0x10;
        if (reason.SourceItemType != 0) bits |= 0x20;
        if (reason.SourceItemPrefix != 0) bits |= 0x40;
        if (reason.CustomReason is not null) bits |= 0x80;
        writer.Write(bits);
        if ((bits & 0x01) != 0) writer.Write(reason.SourcePlayer);
        if ((bits & 0x02) != 0) writer.Write(reason.SourceNpc);
        if ((bits & 0x04) != 0) writer.Write(reason.SourceProjectileLocalIndex);
        if ((bits & 0x08) != 0) writer.Write(checked((byte)reason.SourceOther));
        if ((bits & 0x10) != 0) writer.Write(reason.SourceProjectileType);
        if ((bits & 0x20) != 0) writer.Write(reason.SourceItemType);
        if ((bits & 0x40) != 0) writer.Write(checked((byte)reason.SourceItemPrefix));
        if ((bits & 0x80) != 0) writer.Write(reason.CustomReason!);
    }
}
