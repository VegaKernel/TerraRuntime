using System.Text;

namespace TerraRuntime.World;

public enum WorldFileFreshCompose326Result : byte
{
    Composed = 0,
    InvalidDimensions = 1,
    HeaderEncodeFailed = 2,
    RuntimeMetadataEncodeFailed = 3,
    TileEncodeFailed = 4,
    ChestEncodeFailed = 5,
    SignEncodeFailed = 6,
    NpcEncodeFailed = 7,
    TileEntityEncodeFailed = 8,
    PressurePlateEncodeFailed = 9,
    TownRoomEncodeFailed = 10,
    BestiaryEncodeFailed = 11,
    CreativePowersEncodeFailed = 12,
    FooterEncodeFailed = 13,
    EnvelopeEncodeFailed = 14,
    FileTooLarge = 15,
    ValidationFailed = 16
}

public readonly record struct WorldFileFreshCompose326Diagnostic(
    WorldFileFreshCompose326Result Result,
    int StageResultCode = 0,
    WorldFileLoadDiagnostic Validation = default)
{
    public bool Succeeded => Result == WorldFileFreshCompose326Result.Composed;
}

/// <summary>
/// Complete semantic side-table state for a newly composed current-format world. The fresh composer owns the
/// envelope/header/tile layout while each section remains encoded by its dedicated version-pinned codec.
/// </summary>
public sealed record WorldFileFreshSections326(
    WorldChest[] Chests,
    WorldSign[] Signs,
    WorldNpcPersistence Npcs,
    WorldTileEntity[] TileEntities,
    WorldPressurePlate[] PressurePlates,
    WorldTownRoom[] TownRooms,
    WorldBestiaryData Bestiary,
    WorldCreativePowersData CreativePowers);

/// <summary>
/// Composes a complete current-format Terraria world from a finalized generated candidate. Fresh generation owns the
/// tile store and semantic header anchors; callers may supply every semantic side-table section represented by the
/// runtime model. Existing convenience overloads keep the historical empty-section defaults.
///
/// The completed byte image is fed back through <see cref="WorldFileLoader"/> before it can escape this method.
/// Callers never receive a partially encoded or structurally invalid .wld candidate.
/// </summary>
public static class WorldFileFreshComposer326
{
    private const int HeaderStringBudget = 4 * 1024;

    public static WorldFileFreshCompose326Diagnostic TryCompose(
        WorldFileHeader header,
        RuntimeWorldGenerationMetadataSnapshot generation,
        WorldTileStore tiles,
        byte gameMode,
        bool crimson,
        long creationTimeBinary,
        long lastPlayedBinary,
        out byte[] file) =>
        TryCompose(
            header,
            generation,
            tiles,
            CreateSections(ReadOnlySpan<WorldChest>.Empty, EmptyNpcPersistence()),
            gameMode,
            crimson,
            creationTimeBinary,
            lastPlayedBinary,
            out file);

    public static WorldFileFreshCompose326Diagnostic TryCompose(
        WorldFileHeader header,
        RuntimeWorldGenerationMetadataSnapshot generation,
        WorldTileStore tiles,
        ReadOnlySpan<WorldChest> chests,
        byte gameMode,
        bool crimson,
        long creationTimeBinary,
        long lastPlayedBinary,
        out byte[] file) =>
        TryCompose(
            header,
            generation,
            tiles,
            CreateSections(chests, EmptyNpcPersistence()),
            gameMode,
            crimson,
            creationTimeBinary,
            lastPlayedBinary,
            out file);

    public static WorldFileFreshCompose326Diagnostic TryCompose(
        WorldFileHeader header,
        RuntimeWorldGenerationMetadataSnapshot generation,
        WorldTileStore tiles,
        ReadOnlySpan<WorldChest> chests,
        WorldNpcPersistence npcs,
        byte gameMode,
        bool crimson,
        long creationTimeBinary,
        long lastPlayedBinary,
        out byte[] file) =>
        TryCompose(
            header,
            generation,
            tiles,
            CreateSections(chests, npcs),
            gameMode,
            crimson,
            creationTimeBinary,
            lastPlayedBinary,
            out file);

    /// <summary>
    /// Composes all eleven current .wld sections from semantic state and validates the complete byte image before
    /// publication. This is the full-section path used when generation or another trusted producer already owns
    /// signs, tile entities, pressure plates, housing, bestiary and creative-power state in addition to tiles/chests/NPCs.
    /// </summary>
    public static WorldFileFreshCompose326Diagnostic TryCompose(
        WorldFileHeader header,
        RuntimeWorldGenerationMetadataSnapshot generation,
        WorldTileStore tiles,
        WorldFileFreshSections326 sections,
        byte gameMode,
        bool crimson,
        long creationTimeBinary,
        long lastPlayedBinary,
        out byte[] file)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(sections.Chests);
        ArgumentNullException.ThrowIfNull(sections.Signs);
        ArgumentNullException.ThrowIfNull(sections.Npcs);
        ArgumentNullException.ThrowIfNull(sections.TileEntities);
        ArgumentNullException.ThrowIfNull(sections.PressurePlates);
        ArgumentNullException.ThrowIfNull(sections.TownRooms);
        ArgumentNullException.ThrowIfNull(sections.Bestiary);
        ArgumentNullException.ThrowIfNull(sections.CreativePowers);
        file = Array.Empty<byte>();

        if (header.Dimensions.WidthTiles != tiles.Dimensions.WidthTiles ||
            header.Dimensions.HeightTiles != tiles.Dimensions.HeightTiles)
        {
            return new WorldFileFreshCompose326Diagnostic(WorldFileFreshCompose326Result.InvalidDimensions);
        }

        using var stream = new MemoryStream();
        stream.Position = WorldFileEnvelopeEncoder.CurrentEncodedLength;
        int[] pointers = new int[VanillaWorldFormat326.SectionCount];
        pointers[0] = WorldFileEnvelopeEncoder.CurrentEncodedLength;

        WorldFileHeaderPrefixEncodeResult headerResult = WorldFileHeaderPrefixEncoder.TryEncode(
            header,
            stream,
            out _);
        if (headerResult != WorldFileHeaderPrefixEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.HeaderEncodeFailed, (int)headerResult);

        var freshMetadata = new WorldFileFreshRuntimeMetadata326(
            generation,
            gameMode,
            crimson,
            creationTimeBinary,
            lastPlayedBinary);
        WorldFileFreshRuntimeMetadata326EncodeResult metadataResult =
            WorldFileFreshRuntimeMetadata326Encoder.TryEncode(header, freshMetadata, stream, out _);
        if (metadataResult != WorldFileFreshRuntimeMetadata326EncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.RuntimeMetadataEncodeFailed, (int)metadataResult);

        if (!TryCapturePointer(stream, pointers, 1))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);

        ReadOnlyMemory<byte> frameImportance = VanillaWorldFrameImportance326.CopyPackedBits();
        WorldFileTileEncodeResult tileResult = WorldFileTileEncoder.TryEncode(
            tiles,
            VanillaWorldFrameImportance326.Count,
            frameImportance.Span,
            stream,
            out _);
        if (tileResult != WorldFileTileEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.TileEncodeFailed, (int)tileResult);

        if (!TryCapturePointer(stream, pointers, 2))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);
        WorldFileChestEncodeResult chestResult = WorldFileChestEncoder.TryEncode(
            sections.Chests,
            header.Dimensions,
            stream,
            out _);
        if (chestResult != WorldFileChestEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.ChestEncodeFailed, (int)chestResult);

        if (!TryCapturePointer(stream, pointers, 3))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);
        WorldFileSignEncodeResult signResult = WorldFileSignEncoder.TryEncode(
            sections.Signs,
            header.Dimensions,
            maxTextBytesPerSign: int.MaxValue,
            maxTotalTextBytes: long.MaxValue,
            stream,
            out _);
        if (signResult != WorldFileSignEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.SignEncodeFailed, (int)signResult);

        if (!TryCapturePointer(stream, pointers, 4))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);
        WorldFileNpcDecodeOptions npcOptions = CreateNpcOptions(sections.Npcs);
        WorldFileNpcEncodeResult npcResult = WorldFileNpcEncoder.TryEncode(sections.Npcs, npcOptions, stream, out _);
        if (npcResult != WorldFileNpcEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.NpcEncodeFailed, (int)npcResult);

        if (!TryCapturePointer(stream, pointers, 5))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);
        WorldFileTileEntityEncodeResult tileEntityResult = WorldFileTileEntityEncoder.TryEncode(
            sections.TileEntities,
            header.Dimensions,
            maxEntities: sections.TileEntities.Length,
            stream,
            out _);
        if (tileEntityResult != WorldFileTileEntityEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.TileEntityEncodeFailed, (int)tileEntityResult);

        if (!TryCapturePointer(stream, pointers, 6))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);
        WorldFilePressurePlateEncodeResult pressurePlateResult = WorldFilePressurePlateEncoder.TryEncode(
            sections.PressurePlates,
            header.Dimensions,
            stream,
            out _);
        if (pressurePlateResult != WorldFilePressurePlateEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.PressurePlateEncodeFailed, (int)pressurePlateResult);

        if (!TryCapturePointer(stream, pointers, 7))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);
        WorldFileTownRoomEncodeResult townRoomResult = WorldFileTownRoomEncoder.TryEncode(
            sections.TownRooms,
            header.Dimensions,
            maxRooms: sections.TownRooms.Length,
            stream,
            out _);
        if (townRoomResult != WorldFileTownRoomEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.TownRoomEncodeFailed, (int)townRoomResult);

        if (!TryCapturePointer(stream, pointers, 8))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);
        WorldFileBestiaryLimits bestiaryLimits = CreateBestiaryLimits(sections.Bestiary);
        WorldFileBestiaryEncodeResult bestiaryResult = WorldFileBestiaryEncoder.TryEncode(
            sections.Bestiary,
            bestiaryLimits,
            stream,
            out _);
        if (bestiaryResult != WorldFileBestiaryEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.BestiaryEncodeFailed, (int)bestiaryResult);

        if (!TryCapturePointer(stream, pointers, 9))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);
        WorldFileCreativePowersEncodeResult creativeResult = WorldFileCreativePowersEncoder.TryEncode(
            sections.CreativePowers,
            stream,
            out _);
        if (creativeResult != WorldFileCreativePowersEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.CreativePowersEncodeFailed, (int)creativeResult);

        if (!TryCapturePointer(stream, pointers, 10))
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);
        WorldFileFooterEncodeResult footerResult = WorldFileFooterEncoder.TryEncode(header, stream, out _);
        if (footerResult != WorldFileFooterEncodeResult.Encoded)
            return Fail(WorldFileFreshCompose326Result.FooterEncodeFailed, (int)footerResult);

        if (stream.Length > int.MaxValue)
            return Fail(WorldFileFreshCompose326Result.FileTooLarge);

        WorldFileEnvelope envelope = VanillaWorldEnvelope326.CreateFresh(pointers);
        stream.Position = 0;
        WorldFileEnvelopeEncodeResult envelopeResult = WorldFileEnvelopeEncoder.TryEncode(envelope, stream, out long envelopeBytes);
        if (envelopeResult != WorldFileEnvelopeEncodeResult.Encoded ||
            envelopeBytes != WorldFileEnvelopeEncoder.CurrentEncodedLength)
        {
            return Fail(WorldFileFreshCompose326Result.EnvelopeEncodeFailed, (int)envelopeResult);
        }

        file = stream.ToArray();
        WorldFileLoadLimits validationLimits = CreateValidationLimits(tiles.Count, sections);
        WorldFileLoadDiagnostic validation = WorldFileLoader.TryLoadReusingTiles(
            file,
            validationLimits,
            tiles,
            out WorldFileData? loaded);
        if (!validation.IsLoaded ||
            loaded is null ||
            loaded.Chests.Length != sections.Chests.Length ||
            loaded.Signs.Length != sections.Signs.Length ||
            loaded.Npcs.TownNpcs.Length != sections.Npcs.TownNpcs.Length ||
            loaded.Npcs.PersistentNpcs.Length != sections.Npcs.PersistentNpcs.Length ||
            loaded.Npcs.ShimmeredTownNpcIndices.Length != sections.Npcs.ShimmeredTownNpcIndices.Length ||
            loaded.TileEntities.Length != sections.TileEntities.Length ||
            loaded.PressurePlates.Length != sections.PressurePlates.Length ||
            loaded.TownRooms.Length != sections.TownRooms.Length ||
            loaded.Bestiary.Kills.Length != (sections.Bestiary.Kills?.Length ?? -1) ||
            loaded.Bestiary.Sightings.Length != (sections.Bestiary.Sightings?.Length ?? -1) ||
            loaded.Bestiary.Chats.Length != (sections.Bestiary.Chats?.Length ?? -1) ||
            loaded.CreativePowers != sections.CreativePowers)
        {
            file = Array.Empty<byte>();
            return new WorldFileFreshCompose326Diagnostic(
                WorldFileFreshCompose326Result.ValidationFailed,
                Validation: validation);
        }

        return new WorldFileFreshCompose326Diagnostic(
            WorldFileFreshCompose326Result.Composed,
            Validation: validation);
    }

    private static WorldFileFreshSections326 CreateSections(
        ReadOnlySpan<WorldChest> chests,
        WorldNpcPersistence npcs) =>
        new(
            chests.ToArray(),
            [],
            npcs,
            [],
            [],
            [],
            new WorldBestiaryData([], [], []),
            new WorldCreativePowersData(
                FreezeTime: false,
                TimeRateSlider: 0f,
                FreezeRain: false,
                FreezeWind: false,
                DifficultySlider: 0f,
                StopBiomeSpread: false));

    private static WorldNpcPersistence EmptyNpcPersistence() => new([], [], []);

    private static WorldFileNpcDecodeOptions CreateNpcOptions(WorldNpcPersistence npcs)
    {
        int maxNameBytes = 0;
        long totalNameBytes = 0;
        foreach (WorldTownNpc npc in npcs.TownNpcs)
        {
            int nameBytes = Encoding.UTF8.GetByteCount(npc.GivenName);
            maxNameBytes = Math.Max(maxNameBytes, nameBytes);
            totalNameBytes = checked(totalNameBytes + nameBytes);
        }

        int maxShimmerIndexExclusive = 0;
        foreach (int index in npcs.ShimmeredTownNpcIndices)
        {
            if (index >= maxShimmerIndexExclusive)
                maxShimmerIndexExclusive = checked(index + 1);
        }

        return new WorldFileNpcDecodeOptions(
            npcs.ShimmeredTownNpcIndices.Length,
            maxShimmerIndexExclusive,
            npcs.TownNpcs.Length,
            npcs.PersistentNpcs.Length,
            maxNameBytes,
            totalNameBytes);
    }

    private static WorldFileBestiaryLimits CreateBestiaryLimits(WorldBestiaryData bestiary) =>
        new(
            bestiary.Kills?.Length ?? 0,
            bestiary.Sightings?.Length ?? 0,
            bestiary.Chats?.Length ?? 0,
            int.MaxValue,
            long.MaxValue);

    private static WorldFileLoadLimits CreateValidationLimits(
        int tileCount,
        WorldFileFreshSections326 sections)
    {
        int maxItemsPerChest = 0;
        long totalChestItems = 0;
        foreach (WorldChest chest in sections.Chests)
        {
            int itemCount = chest?.Items?.Length ?? 0;
            maxItemsPerChest = Math.Max(maxItemsPerChest, itemCount);
            totalChestItems = checked(totalChestItems + itemCount);
        }

        return new WorldFileLoadLimits(
            MaxTileCount: tileCount,
            MaxItemsPerChest: maxItemsPerChest,
            MaxTotalChestItems: totalChestItems,
            MaxTextBytesPerSign: int.MaxValue,
            MaxTotalSignTextBytes: long.MaxValue,
            Npcs: CreateNpcOptions(sections.Npcs),
            MaxTileEntities: sections.TileEntities.Length,
            MaxPressurePlates: sections.PressurePlates.Length,
            MaxTownRooms: sections.TownRooms.Length,
            Bestiary: CreateBestiaryLimits(sections.Bestiary),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(
                HeaderStringBudget,
                HeaderStringBudget * 3L,
                MaxAnglerNames: 0,
                MaxBannerEntries: 0,
                MaxPartyNpcEntries: 0,
                MaxManifestBytes: 0));
    }

    private static bool TryCapturePointer(MemoryStream stream, int[] pointers, int index)
    {
        if (stream.Position > int.MaxValue)
            return false;
        pointers[index] = (int)stream.Position;
        return true;
    }

    private static WorldFileFreshCompose326Diagnostic Fail(
        WorldFileFreshCompose326Result result,
        int stageResultCode = 0) =>
        new(result, stageResultCode);
}
