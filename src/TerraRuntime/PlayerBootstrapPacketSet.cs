using global::Multiplicity.Packets;
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
    private PlayerBootstrapPacketSet(
        ReadOnlyMemory<byte> worldInfoFrame,
        ReadOnlyMemory<byte>[] baseSectionFrames,
        ReadOnlyMemory<byte> enterWorldFrame)
    {
        WorldInfoFrame = worldInfoFrame;
        BaseSectionFrames = baseSectionFrames;
        EnterWorldFrame = enterWorldFrame;
    }

    public ReadOnlyMemory<byte> WorldInfoFrame { get; }
    public IReadOnlyList<ReadOnlyMemory<byte>> BaseSectionFrames { get; }
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
            WorldSectionPacketEncodeResult result = WorldSectionPacketEncoder.TryEncodeTileOnly(
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

        byte[] enterWorldFrame = EncodeEmptyFrame((byte)TerrariaMessageId.PlayerSpawnSelf);
        return new PlayerBootstrapPacketSet(worldInfoFrame, baseSectionFrames, enterWorldFrame);
    }

    public static PlayerBootstrapPacketSet CreateForTesting(
        ReadOnlyMemory<byte> worldInfoFrame,
        ReadOnlyMemory<byte>[] baseSectionFrames,
        ReadOnlyMemory<byte> enterWorldFrame)
    {
        ArgumentNullException.ThrowIfNull(baseSectionFrames);
        return new PlayerBootstrapPacketSet(worldInfoFrame, (ReadOnlyMemory<byte>[])baseSectionFrames.Clone(), enterWorldFrame);
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
