using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TypedProtocolDecoderFuzzTests
{
    private const int SamplesPerDecoder = 1024;
    private const int MaximumPayloadLength = 160;

    [Fact]
    public void Deterministic_arbitrary_payloads_never_escape_typed_decoder_contracts()
    {
        uint state = 0x51A7E11u;

        for (DecoderKind kind = 0; kind < DecoderKind.Count; kind++)
        {
            for (int sample = 0; sample < SamplesPerDecoder; sample++)
            {
                // Every decoder sees every payload length in the bounded fuzz window repeatedly.
                // This guarantees exact-length codecs exercise their parser paths instead of relying
                // on random selection to happen to land on 5/8/9/etc. bytes.
                int payloadLength = sample % (MaximumPayloadLength + 1);
                var payload = new byte[payloadLength];
                for (int index = 0; index < payload.Length; index++)
                {
                    payload[index] = (byte)Next(ref state);
                }

                bool segmented = (sample & 1) != 0;
                TerrariaFrame frame = CreateFrame(kind, payload, segmented);
                Exception? exception = Record.Exception(() => Decode(kind, frame));

                Assert.True(
                    exception is null,
                    $"Decoder {kind} escaped its contract for sample {sample}, payload length {payloadLength}, segmented={segmented}: {exception}");
            }
        }
    }

    private static TerrariaFrame CreateFrame(DecoderKind kind, byte[] payload, bool segmented)
    {
        byte messageId = kind switch
        {
            DecoderKind.Hello => (byte)TerrariaMessageId.Hello,
            DecoderKind.WorldRequest => (byte)TerrariaMessageId.RequestWorldData,
            DecoderKind.SectionRequest => (byte)TerrariaMessageId.SpawnTileData,
            DecoderKind.PlayerSpawn => (byte)TerrariaMessageId.PlayerSpawn,
            DecoderKind.PlayerMovement => (byte)TerrariaMessageId.PlayerControls,
            DecoderKind.PlayerAppearance => (byte)TerrariaMessageId.SyncPlayer,
            DecoderKind.PlayerEquipment => (byte)TerrariaMessageId.SyncEquipment,
            DecoderKind.PlayerHealth => (byte)TerrariaMessageId.PlayerHp,
            DecoderKind.PlayerMana => (byte)TerrariaMessageId.PlayerMana,
            DecoderKind.TileManipulation => (byte)TerrariaMessageId.TileManipulation,
            DecoderKind.ProjectileUpdate => (byte)TerrariaMessageId.ProjectileNew,
            DecoderKind.ProjectileDestroy => (byte)TerrariaMessageId.ProjectileDestroy,
            DecoderKind.WorldItemDrop => (byte)TerrariaMessageId.WorldItemDrop,
            DecoderKind.WorldItemOwner => (byte)TerrariaMessageId.WorldItemOwner,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        ReadOnlySequence<byte> payloadSequence = CreateSequence(payload, segmented);
        return new TerrariaFrame(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            messageId,
            default,
            payloadSequence);
    }

    private static void Decode(DecoderKind kind, in TerrariaFrame frame)
    {
        switch (kind)
        {
            case DecoderKind.Hello:
            {
                ConnectRequestDecodeResult result = TerrariaConnectRequestDecoder.TryDecode(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.WorldRequest:
            {
                TerrariaJoinDecodeResult result = TerrariaJoinRequestDecoder.TryDecodeWorldRequest(frame);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.SectionRequest:
            {
                TerrariaJoinDecodeResult result = TerrariaJoinRequestDecoder.TryDecodeSectionRequest(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.PlayerSpawn:
            {
                TerrariaJoinDecodeResult result = TerrariaJoinRequestDecoder.TryDecodePlayerSpawn(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.PlayerMovement:
            {
                TerrariaPlayerMovementDecodeResult result = TerrariaPlayerMovementDecoder.TryDecode(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.PlayerAppearance:
            {
                TerrariaPlayerAppearanceDecodeResult result = TerrariaPlayerAppearanceCodec.TryDecode(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.PlayerEquipment:
            {
                TerrariaPlayerEquipmentDecodeResult result = TerrariaPlayerEquipmentCodec.TryDecode(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.PlayerHealth:
            {
                TerrariaPlayerHealthDecodeResult result = TerrariaPlayerVitalsCodec.TryDecodeHealth(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.PlayerMana:
            {
                TerrariaPlayerManaDecodeResult result = TerrariaPlayerVitalsCodec.TryDecodeMana(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.TileManipulation:
            {
                TerrariaTileManipulationDecodeResult result = TerrariaTileManipulationCodec.TryDecode(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.ProjectileUpdate:
            {
                TerrariaProjectileDecodeResult result = TerrariaProjectileDecoder.TryDecodeUpdate(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.ProjectileDestroy:
            {
                TerrariaProjectileDecodeResult result = TerrariaProjectileDecoder.TryDecodeDestroy(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.WorldItemDrop:
            {
                TerrariaWorldItemDropDecodeResult result = TerrariaWorldItemDropDecoder.TryDecode(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            case DecoderKind.WorldItemOwner:
            {
                TerrariaWorldItemOwnerDecodeResult result = TerrariaWorldItemOwnerDecoder.TryDecode(frame, out _);
                Assert.True(Enum.IsDefined(result));
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static ReadOnlySequence<byte> CreateSequence(byte[] payload, bool segmented)
    {
        if (!segmented || payload.Length < 2)
        {
            return new ReadOnlySequence<byte>(payload);
        }

        int split = payload.Length / 2;
        var first = new BufferSegment(payload.AsMemory(0, split));
        BufferSegment last = first.Append(payload.AsMemory(split));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private static uint Next(ref uint state)
    {
        state = unchecked((state * 1664525u) + 1013904223u);
        return state;
    }

    private enum DecoderKind : uint
    {
        Hello,
        WorldRequest,
        SectionRequest,
        PlayerSpawn,
        PlayerMovement,
        PlayerAppearance,
        PlayerEquipment,
        PlayerHealth,
        PlayerMana,
        TileManipulation,
        ProjectileUpdate,
        ProjectileDestroy,
        WorldItemDrop,
        WorldItemOwner,
        Count
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = next;
            return next;
        }
    }
}
