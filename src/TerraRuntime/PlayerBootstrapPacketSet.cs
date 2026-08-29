using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

public readonly record struct PlayerBootstrapSectionResponse(
    ReadOnlyMemory<byte> StatusFrame,
    ReadOnlyMemory<byte>[] BaseSectionFrames,
    ReadOnlyMemory<byte>[] AdditionalSectionFrames);

/// <summary>
/// Immutable server-owned frames shared by connections during the initial Terraria join bootstrap.
/// Expensive section compression and immutable global bootstrap sync are cached and reused.
/// Section-local persistence frames intentionally remain empty during initial join so chest inventories
/// cannot delay the final packet-49 handoff after tile transfer has completed.
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
                long version = world.Tiles.GetSectionVersion(baseSections[i]);
                _sectionCache[index] = new SectionCacheEntry(
                    baseSectionFrames[i],
                    baseSectionPostFrames[i],
                    version);
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

    internal static bool TryCreateFromSnapshot(
        WorldFileData world,
        PlayerBootstrapPacketSnapshot snapshot,
        out PlayerBootstrapPacketSet? packets)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(snapshot);
        packets = null;

        int sectionCount = snapshot.BaseSections.Length;
        if (sectionCount > InitialSectionBootstrapPlanner.MaximumBaseSectionCount ||
            snapshot.BaseSectionFrames.Length != sectionCount ||
            snapshot.BaseSectionPostFrames.Length != sectionCount ||
            !IsValidFrame(snapshot.WorldInfoFrame) ||
            !IsValidFrame(snapshot.StatusFrame) ||
            !IsValidFrame(snapshot.EnterWorldFrame))
        {
            return false;
        }

        Span<WorldSectionId> plannedSections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        int plannedCount = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(
            world.Header.Dimensions,
            world.RuntimeMetadata.SpawnX,
            world.RuntimeMetadata.SpawnY,
            plannedSections);
        if (plannedCount != sectionCount)
            return false;

        for (int i = 0; i < sectionCount; i++)
        {
            if (snapshot.BaseSections[i] != plannedSections[i] ||
                !IsValidFrame(snapshot.BaseSectionFrames[i]))
            {
                return false;
            }

            ReadOnlyMemory<byte>[] postFrames = snapshot.BaseSectionPostFrames[i];
            if (postFrames is null || postFrames.Length != 0)
            {
                // Older bootstrap snapshots embedded section-local NPC/chest synchronization.
                // Reject them so a warm start cannot reintroduce the pre-packet-49 join stall.
                return false;
            }
        }

        for (int i = 0; i < snapshot.GlobalPostSectionFrames.Length; i++)
        {
            if (!IsValidFrame(snapshot.GlobalPostSectionFrames[i]))
                return false;
        }

        WorldSectionId[] baseSections = (WorldSectionId[])snapshot.BaseSections.Clone();
        var baseFrames = (ReadOnlyMemory<byte>[])snapshot.BaseSectionFrames.Clone();
        var postFramesCopy = new ReadOnlyMemory<byte>[sectionCount][];
        for (int i = 0; i < sectionCount; i++)
            postFramesCopy[i] = (ReadOnlyMemory<byte>[])snapshot.BaseSectionPostFrames[i].Clone();

        packets = new PlayerBootstrapPacketSet(
            world,
            baseSections,
            snapshot.WorldInfoFrame,
            snapshot.StatusFrame,
            baseFrames,
            postFramesCopy,
            (ReadOnlyMemory<byte>[])snapshot.GlobalPostSectionFrames.Clone(),
            snapshot.EnterWorldFrame);
        return true;
    }

    internal PlayerBootstrapPacketSnapshot CaptureSnapshot()
    {
        var baseFrames = new ReadOnlyMemory<byte>[BaseSectionFrames.Count];
        for (int i = 0; i < baseFrames.Length; i++)
            baseFrames[i] = BaseSectionFrames[i];

        var postFrames = new ReadOnlyMemory<byte>[BaseSectionPostFrames.Count][];
        for (int i = 0; i < postFrames.Length; i++)
            postFrames[i] = (ReadOnlyMemory<byte>[])BaseSectionPostFrames[i].Clone();

        var globalFrames = new ReadOnlyMemory<byte>[GlobalPostSectionFrames.Count];
        for (int i = 0; i < globalFrames.Length; i++)
            globalFrames[i] = GlobalPostSectionFrames[i];

        return new PlayerBootstrapPacketSnapshot(
            (WorldSectionId[])_baseSections.Clone(),
            WorldInfoFrame,
            StatusFrame,
            baseFrames,
            postFrames,
            globalFrames,
            EnterWorldFrame);
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
    /// Resolves the base spawn section frames against current tile versions. Testing packet sets have no live
    /// world and simply return their immutable fixtures; production sets refresh only sections that changed.
    /// </summary>
    public bool TryGetBaseSectionFrames(out ReadOnlyMemory<byte>[] frames)
    {
        if (_world is null)
        {
            frames = new ReadOnlyMemory<byte>[BaseSectionFrames.Count];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = BaseSectionFrames[i];
            return true;
        }

        frames = new ReadOnlyMemory<byte>[_baseSections.Length];
        for (int i = 0; i < _baseSections.Length; i++)
        {
            if (!TryGetOrEncodeSection(_baseSections[i], out SectionCacheEntry entry))
            {
                frames = [];
                return false;
            }

            frames[i] = entry.TileSectionFrame;
        }

        return true;
    }

    /// <summary>
    /// Creates the packet-10 transfer plan selected by packet 8. Base and additional section frames are resolved
    /// together so one connection never mixes a stale shared base array with newly encoded additional sections.
    /// </summary>
    public bool TryCreateSectionResponse(
        int tileX,
        int tileY,
        byte team,
        out PlayerBootstrapSectionResponse response)
    {
        if (!TryGetBaseSectionFrames(out ReadOnlyMemory<byte>[] baseSectionFrames))
        {
            response = default;
            return false;
        }

        if (_world is null)
        {
            response = new PlayerBootstrapSectionResponse(StatusFrame, baseSectionFrames, []);
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
            response = new PlayerBootstrapSectionResponse(StatusFrame, baseSectionFrames, []);
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
        }

        ReadOnlyMemory<byte> statusFrame = additionalCount == 0
            ? StatusFrame
            : EncodeStatusFrame(checked(baseSectionFrames.Length + additionalCount));
        response = new PlayerBootstrapSectionResponse(
            statusFrame,
            baseSectionFrames,
            additionalFrames.ToArray());
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
        long version = world.Tiles.GetSectionVersion(section);
        if ((version & 1L) != 0)
        {
            entry = default;
            return false;
        }

        lock (_sectionCacheGate)
        {
            if (_sectionCache.TryGetValue(index, out entry) && entry.Version == version)
                return true;
        }

        if (!world.Tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? snapshot) || snapshot is null ||
            !TryEncodeSection(world, snapshot, out SectionCacheEntry encoded))
        {
            entry = default;
            return false;
        }

        long currentVersion = world.Tiles.GetSectionVersion(section);
        if (currentVersion != snapshot.Revision)
        {
            entry = default;
            return false;
        }

        lock (_sectionCacheGate)
        {
            currentVersion = world.Tiles.GetSectionVersion(section);
            if (currentVersion != snapshot.Revision)
            {
                entry = default;
                return false;
            }

            if (_sectionCache.TryGetValue(index, out SectionCacheEntry existing) &&
                existing.Version == currentVersion)
            {
                entry = existing;
                return true;
            }

            _sectionCache[index] = encoded;
            entry = encoded;
            return true;
        }
    }

    private static bool TryEncodeSection(
        WorldFileData world,
        WorldSectionId section,
        out SectionCacheEntry entry)
    {
        long before = world.Tiles.GetSectionVersion(section);
        if ((before & 1L) != 0)
        {
            entry = default;
            return false;
        }

        WorldTileBounds bounds = TerrariaSectionGeometry.GetBounds(world.Header.Dimensions, section);
        WorldSectionPacketEncodeResult result = WorldSectionPacketEncoder.TryEncode(
            world,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            out byte[] encoded);
        long after = world.Tiles.GetSectionVersion(section);
        if (result != WorldSectionPacketEncodeResult.Encoded || before != after || (after & 1L) != 0)
        {
            entry = default;
            return false;
        }

        entry = new SectionCacheEntry(encoded, [], after);
        return true;
    }

    private static bool TryEncodeSection(
        WorldFileData world,
        WorldSectionTileSnapshot snapshot,
        out SectionCacheEntry entry)
    {
        WorldSectionPacketEncodeResult result = WorldSectionPacketEncoder.TryEncode(
            world,
            snapshot,
            out byte[] encoded);
        if (result != WorldSectionPacketEncodeResult.Encoded)
        {
            entry = default;
            return false;
        }

        entry = new SectionCacheEntry(encoded, [], snapshot.Revision);
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

    private static bool IsValidFrame(ReadOnlyMemory<byte> frame) =>
        frame.Length is >= TerrariaFrameDecoderOptions.MinimumFrameLength and <= ushort.MaxValue;

    private readonly record struct SectionCacheEntry(
        ReadOnlyMemory<byte> TileSectionFrame,
        ReadOnlyMemory<byte>[] PostSectionFrames,
        long Version);
}

internal sealed record PlayerBootstrapPacketSnapshot(
    WorldSectionId[] BaseSections,
    ReadOnlyMemory<byte> WorldInfoFrame,
    ReadOnlyMemory<byte> StatusFrame,
    ReadOnlyMemory<byte>[] BaseSectionFrames,
    ReadOnlyMemory<byte>[][] BaseSectionPostFrames,
    ReadOnlyMemory<byte>[] GlobalPostSectionFrames,
    ReadOnlyMemory<byte> EnterWorldFrame);