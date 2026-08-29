using System.Buffers;
using System.IO;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaClientChatMessage(string CommandName, string Text);

public readonly record struct TerrariaServerChatMessage(
    byte AuthorId,
    string Text,
    TerrariaRgbColor Color);

public enum TerrariaClientChatDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    WrongModule = 3,
    WrongDirection = 4,
    InvalidText = 5,
    Malformed = 6
}

/// <summary>
/// Protocol-326 chat adapter. Packet 82 wraps a NetTextModule and its payload is directional:
/// clients send command-id + text while servers send author + NetworkText + color.
/// </summary>
public static class TerrariaChatCodec
{
    public const int MaximumPayloadLength = 2_048;
    public const int MaximumCommandNameLength = 128;
    public const int MaximumTextLength = 512;

    public static TerrariaClientChatDecodeResult TryDecodeClientMessage(
        in TerrariaFrame frame,
        out TerrariaClientChatMessage message)
    {
        message = default;
        if (frame.MessageId != (byte)TerrariaMessageId.LoadNetModule)
            return TerrariaClientChatDecodeResult.WrongMessageId;
        if (frame.Payload.Length < sizeof(ushort) + 2 || frame.Payload.Length > MaximumPayloadLength)
            return TerrariaClientChatDecodeResult.InvalidPayloadLength;

        try
        {
            ReadOnlyMemory<byte> payload = CopyPayload(in frame);
            if (!TerrariaPacket.TryDeserializePayload(
                    (byte)TerrariaMessageId.LoadNetModule,
                    payload,
                    out TerrariaPacket packet) ||
                packet is not LoadNetModule load)
            {
                return TerrariaClientChatDecodeResult.Malformed;
            }

            if (load.LoadedModule is not NetTextModule textModule)
                return TerrariaClientChatDecodeResult.WrongModule;
            if (textModule.PayloadKind != NetTextModulePayloadKind.ClientChatMessage)
                return TerrariaClientChatDecodeResult.WrongDirection;

            string commandName = textModule.CommandName ?? string.Empty;
            string text = textModule.ChatMessage ?? string.Empty;
            if (commandName.Length > MaximumCommandNameLength ||
                text.Length == 0 ||
                text.Length > MaximumTextLength ||
                text.IndexOf('\0') >= 0)
            {
                return TerrariaClientChatDecodeResult.InvalidText;
            }

            message = new TerrariaClientChatMessage(commandName, text);
            return TerrariaClientChatDecodeResult.Decoded;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            EndOfStreamException or
            IOException or
            OverflowException or
            ArgumentException)
        {
            message = default;
            return TerrariaClientChatDecodeResult.Malformed;
        }
    }

    public static bool TryDecodeServerFrame(
        ReadOnlyMemory<byte> encodedFrame,
        out TerrariaServerChatMessage message)
    {
        message = default;
        if (encodedFrame.Length < TerrariaFrameDecoderOptions.MinimumFrameLength ||
            encodedFrame.Length > TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength)
        {
            return false;
        }

        try
        {
            var input = new ReadOnlySequence<byte>(encodedFrame);
            if (TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame) != TerrariaFrameReadResult.Frame ||
                !input.IsEmpty ||
                frame.MessageId != (byte)TerrariaMessageId.LoadNetModule)
            {
                return false;
            }

            ReadOnlyMemory<byte> payload = CopyPayload(in frame);
            if (!TerrariaPacket.TryDeserializePayload(
                    (byte)TerrariaMessageId.LoadNetModule,
                    payload,
                    out TerrariaPacket packet) ||
                packet is not LoadNetModule load ||
                load.LoadedModule is not NetTextModule textModule ||
                textModule.PayloadKind != NetTextModulePayloadKind.ServerChatMessage ||
                textModule.ServerText is null)
            {
                return false;
            }

            string text = textModule.ServerText.Text ?? string.Empty;
            if (text.Length == 0 || text.Length > MaximumTextLength || text.IndexOf('\0') >= 0)
                return false;

            ColorStruct color = textModule.MessageColor;
            message = new TerrariaServerChatMessage(
                textModule.AuthorId,
                text,
                new TerrariaRgbColor(color.R, color.G, color.B));
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            EndOfStreamException or
            IOException or
            OverflowException or
            ArgumentException)
        {
            message = default;
            return false;
        }
    }

    public static byte[] EncodeServerMessage(byte authorId, string text, TerrariaRgbColor color)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || text.Length > MaximumTextLength)
            throw new ArgumentOutOfRangeException(nameof(text));

        var module = new NetTextModule
        {
            PayloadKind = NetTextModulePayloadKind.ServerChatMessage,
            AuthorId = authorId,
            ServerText = new NetworkText
            {
                TextMode = (byte)NetworkText.Mode.Literal,
                Text = text,
                SubstitutionList = Array.Empty<NetworkText>()
            },
            MessageColor = new ColorStruct
            {
                R = color.R,
                G = color.G,
                B = color.B
            }
        };
        var packet = new LoadNetModule { LoadedModule = module };

        using var stream = new MemoryStream(packet.GetLength() + TerrariaPacket.PacketHeaderLength);
        packet.ToStream(stream);
        return stream.ToArray();
    }

    private static ReadOnlyMemory<byte> CopyPayload(in TerrariaFrame frame)
    {
        int length = checked((int)frame.Payload.Length);
        if (frame.Payload.IsSingleSegment)
            return frame.Payload.First;

        byte[] buffer = GC.AllocateUninitializedArray<byte>(length);
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(buffer.AsSpan(offset));
            offset += segment.Length;
        }

        return buffer;
    }
}
