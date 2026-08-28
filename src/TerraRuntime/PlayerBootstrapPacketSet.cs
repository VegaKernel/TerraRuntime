using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

public readonly record struct PlayerBootstrapSectionResponse(
    ReadOnlyMemory<byte> StatusFrame,
    ReadOnlyMemory<byte>[] AdditionalSectionFrames);

/// <summary>
/// Immutable server-owned frames shared by connections during the initial Terraria join bootstrap.
/// Expensive section compression and immutable persistence-derived section sync are cached and reused.
/// </summary>
public sealed class PlayerBootstrapPacketSet
{
    private const string ReceivingTileDataLocalizationKey = "LegacyInterface.44";

    private readonly WorldFileData? _world;
    private readonly WorldSectionId[] _baseSections;
    private readonly Dictionary<int, SectionCacheEntry> _sectionCache;
    private readonly object _sectionCacheGate = new();

    private PlayerBootstrapPacketSet(
        WorldFileData? world,
        WorldSectionId[] baseSections,
        ReadOnlyMemory<byte> worldInfoFrame,
        ReadOnlyMemory<byte> statusFrame,
        ReadOnlyMemory<byte>[] baseSectionFrames,
        ReadOnlyMemory<byte>[][] baseSectionPostFrames,
        ReadOnlyMemory<byte>[] globalPostSectionFrames,
        ReadOnlyMemory<byte> enterWorldFrame)
    {
        _world = world;
        _baseSections = baseSections;
        WorldInfoFrame = worldInfoFrame;
        StatusFrame = statusFrame;
        BaseSectionFrames = baseSectionFrames;
        BaseSectionPostFrames = baseSectionPostFrames;
        GlobalPostSectionFrames = globalPostSectionFrames;
        EnterWorldFrame = enterWorldFrame;

        _sectionCache = new Dictionary<int, SectionCacheEntry>(baseSections.Length);
        if (world is not null)
        {
            for (int i = 0; i < baseSections.Length; i++)
            {
                int index = TerrariaSectionGeometry.ToLinearIndex(world.Header.Dimensions, baseSections[i]);
                _sectionCache[index] = new SectionCacheEntry(baseSectionFrames[i], baseSectionPostFrames[i]);
            }
        }
    }

    public ReadOnlyMemory<byte> WorldInfoFrame { get; }
    public ReadOnlyMemory<byte> StatusFrame { get; }
    public IReadOnlyList<ReadOnlyMemory<byte>> BaseSectionFrames { get; }
    public IReadOnlyList<ReadOnlyMemory<byte>[]> BaseSectionPostFrames { get; }
    public IReadOnlyList<ReadOnlyMemory<byte>> GlobalPostSectionFrames { get; }
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

        var entries = new SectionCacheEntry[sectionCount];
        var encoded = new bool[sectionCount];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(sectionCount, Math.Clamp(Environment.ProcessorCount, 1, 8))
        };

        try
        {
            Parallel.For(
                0,
                sectionCount,
                parallelOptions,
                i => encoded[i] = TryEncodeSection(world, baseSections[i], out entries[i]));
        }
        catch (AggregateException exception)
        {
            throw new InvalidOperationException(
                "Failed while encoding bootstrap sections in parallel.",
                exception.Flatten());
        }

        var baseSectionFrames = new ReadOnlyMemory<byte>[sectionCount];
        var baseSectionPostFrames = new ReadOnlyMemory<byte>[sectionCount][];
        for (int i = 0; i < sectionCount; i++)
        {
            if (!encoded[i])
            {
                throw new InvalidOperationException(
                    $"Failed to cache bootstrap section {baseSections[i]}.");
            }

            baseSectionFrames[i] = entries[i].TileSectionFrame;
            baseSectionPostFrames[i] = entries[i].PostSectionFrames;
        }

        if (WorldGlobalTownNpcBootstrapPacketEncoder.TryEncode(
                world.Npcs.TownNpcs,
                out ReadOnlyMemory<byte>[] globalPostSectionFrames) != WorldGlobalTownNpcBootstrapPacketEncodeResult.Encoded)
        {
            throw new InvalidOperationException("Failed to cache the global persisted town-NPC bootstrap baseline.");
        }

        byte[] statusFrame = EncodeStatusFrame(sectionCount);
        byte[] enterWorldFrame = EncodeEmptyFrame((byte)TerrariaMessageId.PlayerSpawnSelf);
        return new PlayerBootstrapPacketSet(
            world,
            baseSections,
            worldInfoFrame,
            statusFrame,
            baseSectionFrames,
            baseSectionPostFrames,
            globalPostSectionFrames,
            enterWorldFrame);
    }

    public static PlayerBootstrapPacketSet CreateForTesting(
        ReadOnlyMemory<byte> worldInfoFrame,
        ReadOnlyMemory<byte>[] baseSectionFrames,
        ReadOnlyMemory<byte> enterWorldFrame,
        ReadOnlyMemory<byte>[]? globalPostSectionFrames = null)
    {
        ArgumentNullException.ThrowIfNull(baseSectionFrames);
        var postFrames = new ReadOnlyMemory<byte>[baseSectionFrames.Length][];
        for (int i = 0; i < postFrames.Length; i++)
            postFrames[i] = [];

        return new PlayerBootstrapPacketSet(
            world: null,
            baseSections: [],
            worldInfoFrame,
            EncodeStatusFrame(baseSectionFrames.Length),
            (ReadOnlyMemory<byte>[])baseSectionFrames.Clone(),
            postFrames,
            globalPostSectionFrames is null ? [] : (ReadOnlyMemory<byte>[])globalPostSectionFrames.Clone(),
            enterWorldFrame);
    }

    /// <summary>
    /// Creates the additional section sync selected by packet 8. The client-requested window and,
    /// for team-based-spawn worlds, the team's extra-spawn window are deduplicated against already sent sections.
    /// Each section contributes packet 10 followed by persisted town-NPC sync and then chest contents.
    /// </summary>
    public bool TryCreateSectionResponse(
        int tileX,
        int tileY,
        byte team,
        out PlayerBootstrapSectionResponse response)
    {
        if (_world is null)
        {
            response = new PlayerBootstrapSectionResponse(StatusFrame, []);
            return true;
        }

        Span<WorldSectionId> requestedSections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumRequestedSectionCount];
        int requestedCount = InitialSectionBootstrapPlanner.PlanRequestedSections(
            _world.Header.Dimensions,
            tileX,
            tileY,
            requestedSections);

        Span<WorldSectionId> teamSections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumTeamSpawnSectionCount];
        int teamCount = 0;
        if (_world.RuntimeMetadata.TeamBasedSpawnsSeed &&
            team != 0 &&
            team < _world.RuntimeMetadata.ExtraSpawnPoints.Length)
        {
            var teamSpawn = _world.RuntimeMetadata.ExtraSpawnPoints[team];
            teamCount = InitialSectionBootstrapPlanner.PlanTeamSpawnSections(
                _world.Header.Dimensions,
                teamSpawn.X,
                teamSpawn.Y,
                teamSections);
        }

        if (requestedCount + teamCount == 0)
        {
            response = new PlayerBootstrapSectionResponse(StatusFrame, []);
            return true;
        }

        Span<WorldSectionId> additionalSections = stackalloc WorldSectionId[
            InitialSectionBootstrapPlanner.MaximumRequestedSectionCount +
            InitialSectionBootstrapPlanner.MaximumTeamSpawnSectionCount];
        int additionalCount = 0;
        AppendUniqueAdditionalSections(requestedSections[..requestedCount], additionalSections, ref additionalCount);
        AppendUniqueAdditionalSections(teamSections[..teamCount], additionalSections, ref additionalCount);

        var additionalFrames = new List<ReadOnlyMemory<byte>>(additionalCount);
        for (int i = 0; i < additionalCount; i++)
        {
            if (!TryGetOrEncodeSection(additionalSections[i], out SectionCacheEntry entry))
            {
                response = default;
                return false;
            }

            additionalFrames.Add(entry.TileSectionFrame);
            additionalFrames.AddRange(entry.PostSectionFrames);
        }

        ReadOnlyMemory<byte> statusFrame = additionalCount == 0
            ? StatusFrame
            : EncodeStatusFrame(checked(BaseSectionFrames.Count + additionalCount));
        response = new PlayerBootstrapSectionResponse(statusFrame, additionalFrames.ToArray());
        return true;
    }

    private void AppendUniqueAdditionalSections(
        ReadOnlySpan<WorldSectionId> candidates,
        Span<WorldSectionId> destination,
        ref int count)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            WorldSectionId section = candidates[i];
            if (ContainsBaseSection(section) || Contains(destination[..count], section))
                continue;

            destination[count++] = section;
        }
    }

    private bool ContainsBaseSection(WorldSectionId section) => Contains(_baseSections, section);

    private static bool Contains(ReadOnlySpan<WorldSectionId> sections, WorldSectionId section)
    {
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == section)
                return true;
        }

        return false;
    }

    private bool TryGetOrEncodeSection(WorldSectionId section, out SectionCacheEntry entry)
    {
        WorldFileData world = _world!;
        int index = TerrariaSectionGeometry.ToLinearIndex(world.Header.Dimensions, section);

        lock (_sectionCacheGate)
        {
            if (_sectionCache.TryGetValue(index, out entry))
                return true;
        }

        if (!TryEncodeSection(world, section, out SectionCacheEntry encoded))
        {
            entry = default;
            return false;
        }

        lock (_sectionCacheGate)
        {
            if (_sectionCache.TryGetValue(index, out entry))
                return true;

            _sectionCache.Add(index, encoded);
            entry = encoded;
            return true;
        }
    }

    private static bool TryEncodeSection(
        WorldFileData world,
        WorldSectionId section,
        out SectionCacheEntry entry)
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
            entry = default;
            return false;
        }

        if (!TryEncodePostSectionFrames(world, section, out ReadOnlyMemory<byte>[] postSectionFrames))
        {
            entry = default;
            return false;
        }

        entry = new SectionCacheEntry(encoded, postSectionFrames);
        return true;
    }

    private static bool TryEncodePostSectionFrames(
        WorldFileData world,
        WorldSectionId section,
        out ReadOnlyMemory<byte>[] frames)
    {
        WorldSectionPersistenceSyncPacketEncodeResult result = WorldSectionPersistenceSyncPacketEncoder.TryEncode(
            world.Header.Dimensions,
            world.Npcs.TownNpcs,
            world.Chests,
            section,
            out frames);
        return result == WorldSectionPersistenceSyncPacketEncodeResult.Encoded;
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

    private readonly record struct SectionCacheEntry(
        ReadOnlyMemory<byte> TileSectionFrame,
        ReadOnlyMemory<byte>[] PostSectionFrames);
}
