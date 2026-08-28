using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Immutable server-owned frames shared by connections during the initial Terraria join bootstrap.
/// Expensive section compression is paid once when the loaded world becomes active, not once per client.
/// </summary>
public sealed class PlayerBootstrapPacketSet
{
    private const string ReceivingTileDataLocalizationKey = "LegacyInterface.44";

    private PlayerBootstrapPacketSet(
        ReadOnlyMemory<byte> worldInfoFrame,
        ReadOnlyMemory<byte> statusFrame,
        ReadOnlyMemory<byte>[] baseSectionFrames,
        ReadOnlyMemory<byte> baseTileFrameFrame,
        ReadOnlyMemory<byte> enterWorldFrame)
    {
        WorldInfoFrame = worldInfoFrame;
        StatusFrame = statusFrame;
        BaseSectionFrames = baseSectionFrames;
        BaseTileFrameFrame = baseTileFrameFrame;
        EnterWorldFrame = enterWorldFrame;
    }

    public ReadOnlyMemory<byte> WorldInfoFrame { get; }
    public ReadOnlyMemory<byte> StatusFrame { get; }
    public IReadOnlyList<ReadOnlyMemory<byte>> BaseSectionFrames { get; }
    public ReadOnlyMemory<byte> BaseTileFrameFrame { get; }
    public ReadOnlyMemory<byte> EnterWorldFrame { get; }

    public static PlayerBootstrapPacketSet Create(
        WorldFileData world,
        WorldInfoTransientState transient = default)
    {
        ArgumentNullException.ThrowIfNull(world);

        byte[] worldInfoFrame = SerializePacket(PlayerJoinPacketFactory.CreateWorldInfo(world, transient));

        Span<WorldSectionId> sections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        int sectionCount = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(
            world.Header.Dimensions,
            world.RuntimeMetadata.SpawnX,
            world.RuntimeMetadata.SpawnY,
            sections);

        var baseSectionFrames = new ReadOnlyMemory<byte>[sectionCount];
        for (int i = 0; i < sectionCount; i++)
        {
            WorldTileBounds bounds = TerrariaSectionGeometry.GetBounds(world.Header.Dimensions, sections[i]);
            WorldSectionPacketEncodeResult result = WorldSectionPacketEncoder.TryEncode(
                world,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                out byte[] frame);
            if (result != WorldSectionPacketEncodeResult.Encoded)
            {
                throw new InvalidOperationException(
                    $"Failed to cache bootstrap section {sections[i]}: {result}.");
            }

            baseSectionFrames[i] = frame;
        }

        byte[] statusFrame = EncodeStatusFrame(sectionCount);
        byte[] tileFrameFrame = EncodeTileFrameSectionFrame(sections[..sectionCount]);
        byte[] enterWorldFrame = EncodeEmptyFrame((byte)TerrariaMessageId.PlayerSpawnSelf);
        return new PlayerBootstrapPacketSet(
            worldInfoFrame,
            statusFrame,
            baseSectionFrames,
            tileFrameFrame,
            enterWorldFrame);
    }

    public static PlayerBootstrapPacketSet CreateForTesting(
        ReadOnlyMemory<byte> worldInfoFrame,
        ReadOnlyMemory<byte>[] baseSectionFrames,
        ReadOnlyMemory<byte> enterWorldFrame)
    {
        ArgumentNullException.ThrowIfNull(baseSectionFrames);
        return new PlayerBootstrapPacketSet(
            worldInfoFrame,
            EncodeStatusFrame(baseSectionFrames.Length),
            (ReadOnlyMemory<byte>[])baseSectionFrames.Clone(),
            EncodeTileFrameSectionFrame(0, 0, 0, 0),
            enterWorldFrame);
    }

    private static byte[] EncodeStatusFrame(int sectionCount)
    {
        var packet = new Status
        {
            StatusMax = sectionCount,
            StatusText = new NetworkText
            {
                TextMode = (byte)NetworkText.Mode.LocalizationKey,
                Text = ReceivingTileDataLocalizationKey
            },
            SpecialFlags = StatusSpecialFlags.None
        };

        return SerializePacket(packet);
    }

    private static byte[] EncodeTileFrameSectionFrame(ReadOnlySpan<WorldSectionId> sections)
    {
        if (sections.IsEmpty)
            throw new ArgumentException("At least one bootstrap section is required.", nameof(sections));

        WorldSectionId first = sections[0];
        WorldSectionId last = sections[^1];
        return EncodeTileFrameSectionFrame(first.X, first.Y, last.X, last.Y);
    }

    private static byte[] EncodeTileFrameSectionFrame(int startX, int startY, int endX, int endY)
    {
        Span<byte> payload = stackalloc byte[sizeof(short) * 4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload, checked((short)startX));
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload[2..], checked((short)startY));
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload[4..], checked((short)endX));
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload[6..], checked((short)endY));

        var writer = new System.Buffers.ArrayBufferWriter<byte>(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length);
        if (TerrariaFrameEncoder.TryWrite(writer, (byte)TerrariaMessageId.TileFrameSection, payload) != TerrariaFrameWriteResult.Written)
            throw new InvalidOperationException("Failed to encode bootstrap tile-frame section packet.");
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] SerializePacket(TerrariaPacket packet)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        byte[] frame = stream.ToArray();
        if (frame.Length < TerrariaFrameDecoderOptions.MinimumFrameLength || frame.Length > ushort.MaxValue)
            throw new InvalidOperationException($"Multiplicity produced invalid bootstrap frame length {frame.Length}.");
        return frame;
    }

    private static byte[] EncodeEmptyFrame(byte messageId)
    {
        var writer = new System.Buffers.ArrayBufferWriter<byte>(TerrariaFrameDecoderOptions.MinimumFrameLength);
        if (TerrariaFrameEncoder.TryWrite(writer, messageId, ReadOnlySpan<byte>.Empty) != TerrariaFrameWriteResult.Written)
            throw new InvalidOperationException($"Failed to encode empty bootstrap frame {messageId}.");
        return writer.WrittenSpan.ToArray();
    }
}
