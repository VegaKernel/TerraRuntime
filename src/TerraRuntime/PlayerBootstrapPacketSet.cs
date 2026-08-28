using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

public readonly record struct PlayerBootstrapSectionResponse(
    ReadOnlyMemory<byte> StatusFrame,
    ReadOnlyMemory<byte>[] AdditionalSectionFrames,
    ReadOnlyMemory<byte> RequestedTileFrameFrame)
{
    public bool HasRequestedTileFrame => !RequestedTileFrameFrame.IsEmpty;
}

/// <summary>
/// Immutable server-owned frames shared by connections during the initial Terraria join bootstrap.
/// Expensive section compression is cached by section and reused across joining clients.
/// </summary>
public sealed class PlayerBootstrapPacketSet
{
    private const string ReceivingTileDataLocalizationKey = "LegacyInterface.44";

    private readonly WorldFileData? _world;
    private readonly WorldSectionId[] _baseSections;
    private readonly Dictionary<int, ReadOnlyMemory<byte>> _sectionFrameCache;
    private readonly object _sectionFrameCacheGate = new();

    private PlayerBootstrapPacketSet(
        WorldFileData? world,
        WorldSectionId[] baseSections,
        ReadOnlyMemory<byte> worldInfoFrame,
        ReadOnlyMemory<byte> statusFrame,
        ReadOnlyMemory<byte>[] baseSectionFrames,
        ReadOnlyMemory<byte> baseTileFrameFrame,
        ReadOnlyMemory<byte> enterWorldFrame)
    {
        _world = world;
        _baseSections = baseSections;
        WorldInfoFrame = worldInfoFrame;
        StatusFrame = statusFrame;
        BaseSectionFrames = baseSectionFrames;
        BaseTileFrameFrame = baseTileFrameFrame;
        EnterWorldFrame = enterWorldFrame;

        _sectionFrameCache = new Dictionary<int, ReadOnlyMemory<byte>>(baseSections.Length);
        if (world is not null)
        {
            for (int i = 0; i < baseSections.Length; i++)
            {
                int index = TerrariaSectionGeometry.ToLinearIndex(world.Header.Dimensions, baseSections[i]);
                _sectionFrameCache[index] = baseSectionFrames[i];
            }
        }
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

        Span<WorldSectionId> plannedSections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        int sectionCount = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(
            world.Header.Dimensions,
            world.RuntimeMetadata.SpawnX,
            world.RuntimeMetadata.SpawnY,
            plannedSections);
        WorldSectionId[] baseSections = plannedSections[..sectionCount].ToArray();

        var baseSectionFrames = new ReadOnlyMemory<byte>[sectionCount];
        for (int i = 0; i < sectionCount; i++)
        {
            if (!TryEncodeSection(world, baseSections[i], out ReadOnlyMemory<byte> frame))
            {
                throw new InvalidOperationException(
                    $"Failed to cache bootstrap section {baseSections[i]}.");
            }

            baseSectionFrames[i] = frame;
        }

        byte[] statusFrame = EncodeStatusFrame(sectionCount);
        byte[] tileFrameFrame = EncodeTileFrameSectionFrame(baseSections);
        byte[] enterWorldFrame = EncodeEmptyFrame((byte)TerrariaMessageId.PlayerSpawnSelf);
        return new PlayerBootstrapPacketSet(
            world,
            baseSections,
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
            world: null,
            baseSections: [],
            worldInfoFrame,
            EncodeStatusFrame(baseSectionFrames.Length),
            (ReadOnlyMemory<byte>[])baseSectionFrames.Clone(),
            EncodeTileFrameSectionFrame(0, 0, 0, 0),
            enterWorldFrame);
    }

    /// <summary>
    /// Creates the packet-8 response additions for the optional client-requested tile position.
    /// Base spawn sections remain prebuilt; additional packet-10 frames are encoded once and cached.
    /// </summary>
    public bool TryCreateSectionResponse(
        int tileX,
        int tileY,
        out PlayerBootstrapSectionResponse response)
    {
        if (_world is null)
        {
            response = new PlayerBootstrapSectionResponse(StatusFrame, [], ReadOnlyMemory<byte>.Empty);
            return true;
        }

        Span<WorldSectionId> requestedSections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumRequestedSectionCount];
        int requestedCount = InitialSectionBootstrapPlanner.PlanRequestedSections(
            _world.Header.Dimensions,
            tileX,
            tileY,
            requestedSections);
        if (requestedCount == 0)
        {
            response = new PlayerBootstrapSectionResponse(StatusFrame, [], ReadOnlyMemory<byte>.Empty);
            return true;
        }

        var additionalFrames = new ReadOnlyMemory<byte>[requestedCount];
        int additionalCount = 0;
        for (int i = 0; i < requestedCount; i++)
        {
            WorldSectionId section = requestedSections[i];
            if (ContainsBaseSection(section))
                continue;

            if (!TryGetOrEncodeSection(section, out ReadOnlyMemory<byte> frame))
            {
                response = default;
                return false;
            }

            additionalFrames[additionalCount++] = frame;
        }

        if (additionalCount != additionalFrames.Length)
            Array.Resize(ref additionalFrames, additionalCount);

        ReadOnlyMemory<byte> statusFrame = additionalCount == 0
            ? StatusFrame
            : EncodeStatusFrame(checked(BaseSectionFrames.Count + additionalCount));
        ReadOnlyMemory<byte> requestedTileFrameFrame = EncodeTileFrameSectionFrame(requestedSections[..requestedCount]);

        response = new PlayerBootstrapSectionResponse(
            statusFrame,
            additionalFrames,
            requestedTileFrameFrame);
        return true;
    }

    private bool ContainsBaseSection(WorldSectionId section)
    {
        for (int i = 0; i < _baseSections.Length; i++)
        {
            if (_baseSections[i] == section)
                return true;
        }

        return false;
    }

    private bool TryGetOrEncodeSection(WorldSectionId section, out ReadOnlyMemory<byte> frame)
    {
        WorldFileData world = _world!;
        int index = TerrariaSectionGeometry.ToLinearIndex(world.Header.Dimensions, section);

        lock (_sectionFrameCacheGate)
        {
            if (_sectionFrameCache.TryGetValue(index, out frame))
                return true;
        }

        if (!TryEncodeSection(world, section, out ReadOnlyMemory<byte> encoded))
        {
            frame = default;
            return false;
        }

        lock (_sectionFrameCacheGate)
        {
            if (_sectionFrameCache.TryGetValue(index, out frame))
                return true;

            _sectionFrameCache.Add(index, encoded);
            frame = encoded;
            return true;
        }
    }

    private static bool TryEncodeSection(
        WorldFileData world,
        WorldSectionId section,
        out ReadOnlyMemory<byte> frame)
    {
        WorldTileBounds bounds = TerrariaSectionGeometry.GetBounds(world.Header.Dimensions, section);
        WorldSectionPacketEncodeResult result = WorldSectionPacketEncoder.TryEncode(
            world,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            out byte[] encoded);
        if (result != WorldSectionPacketEncodeResult.Encoded)
        {
            frame = default;
            return false;
        }

        frame = encoded;
        return true;
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
