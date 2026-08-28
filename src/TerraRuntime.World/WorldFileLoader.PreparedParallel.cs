namespace TerraRuntime.World;

public static partial class WorldFileLoader
{
    /// <summary>
    /// Completes a prepared-core load from an owned byte array. Independent non-tile sections are decoded
    /// concurrently, then validated in canonical section order before a single WorldFileData is published.
    /// </summary>
    internal static WorldFileLoadDiagnostic TryLoadPreparedCore(
        byte[] file,
        WorldFileLoadLimits limits,
        WorldFileCore core,
        out WorldFileData? world)
    {
        ArgumentNullException.ThrowIfNull(file);
        limits.Validate();
        ArgumentNullException.ThrowIfNull(core);
        world = null;

        long expectedTileCount = (long)core.Header.Dimensions.WidthTiles * core.Header.Dimensions.HeightTiles;
        if (expectedTileCount != core.Tiles.Count || expectedTileCount > limits.MaxTileCount)
        {
            return Failure(
                WorldFileLoadResult.InvalidTiles,
                WorldFileLoadStage.Tiles,
                0x200);
        }

        WorldDimensions tileDimensions = core.Tiles.Dimensions;
        WorldDimensions headerDimensions = core.Header.Dimensions;
        if (tileDimensions.WidthTiles != headerDimensions.WidthTiles ||
            tileDimensions.HeightTiles != headerDimensions.HeightTiles)
        {
            return Failure(
                WorldFileLoadResult.InvalidTiles,
                WorldFileLoadStage.Tiles,
                0x201);
        }

        WorldFileRuntimeMetadataParseResult metadataResult = default;
        WorldFileRuntimeMetadata? runtimeMetadata = null;
        WorldFileChestDecodeResult chestResult = default;
        WorldChest[] chests = [];
        WorldFileSignDecodeResult signResult = default;
        WorldSign[] signs = [];
        WorldFileNpcDecodeResult npcResult = default;
        WorldNpcPersistence? npcs = null;
        WorldFileTileEntityDecodeResult tileEntityResult = default;
        WorldTileEntity[] tileEntities = [];
        WorldFilePressurePlateDecodeResult pressurePlateResult = default;
        WorldPressurePlate[] pressurePlates = [];
        WorldFileTownRoomDecodeResult townRoomResult = default;
        WorldTownRoom[] townRooms = [];
        WorldFileBestiaryDecodeResult bestiaryResult = default;
        WorldBestiaryData? bestiary = null;
        WorldFileCreativePowersDecodeResult creativeResult = default;
        WorldCreativePowersData? creativePowers = null;
        WorldFileFooterValidationResult footerResult = default;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4)
        };

        Parallel.Invoke(
            parallelOptions,
            () => metadataResult = WorldFileRuntimeMetadataParser.TryParse(
                file,
                core.Envelope,
                core.Header,
                limits.RuntimeMetadata,
                out runtimeMetadata,
                out _),
            () => chestResult = WorldFileChestDecoder.TryDecode(
                file,
                core.Envelope,
                core.Header,
                limits.MaxItemsPerChest,
                limits.MaxTotalChestItems,
                out chests,
                out _),
            () => signResult = WorldFileSignDecoder.TryDecode(
                file,
                core.Envelope,
                core.Header,
                limits.MaxTextBytesPerSign,
                limits.MaxTotalSignTextBytes,
                out signs,
                out _),
            () => npcResult = WorldFileNpcDecoder.TryDecode(
                file,
                core.Envelope,
                limits.Npcs,
                out npcs,
                out _),
            () => tileEntityResult = WorldFileTileEntityDecoder.TryDecode(
                file,
                core.Envelope,
                core.Header,
                limits.MaxTileEntities,
                out tileEntities,
                out _),
            () => pressurePlateResult = WorldFilePressurePlateDecoder.TryDecode(
                file,
                core.Envelope,
                core.Header,
                limits.MaxPressurePlates,
                out pressurePlates,
                out _),
            () => townRoomResult = WorldFileTownRoomDecoder.TryDecode(
                file,
                core.Envelope,
                core.Header,
                limits.MaxTownRooms,
                out townRooms,
                out _),
            () => bestiaryResult = WorldFileBestiaryDecoder.TryDecode(
                file,
                core.Envelope,
                limits.Bestiary,
                out bestiary,
                out _),
            () => creativeResult = WorldFileCreativePowersDecoder.TryDecode(
                file,
                core.Envelope,
                out creativePowers,
                out _),
            () => footerResult = WorldFileFooterValidator.Validate(
                file,
                core.Envelope,
                core.Header,
                out _));

        // Preserve the canonical loader's deterministic failure priority even though the work ran concurrently.
        if (metadataResult != WorldFileRuntimeMetadataParseResult.Parsed || runtimeMetadata is null)
        {
            return Failure(
                WorldFileLoadResult.InvalidHeader,
                WorldFileLoadStage.Header,
                0x100 + (int)metadataResult);
        }

        if (chestResult != WorldFileChestDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidChests, WorldFileLoadStage.Chests, (int)chestResult);
        if (signResult != WorldFileSignDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidSigns, WorldFileLoadStage.Signs, (int)signResult);
        if (npcResult != WorldFileNpcDecodeResult.Decoded || npcs is null)
            return Failure(WorldFileLoadResult.InvalidNpcs, WorldFileLoadStage.Npcs, (int)npcResult);
        if (tileEntityResult != WorldFileTileEntityDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidTileEntities, WorldFileLoadStage.TileEntities, (int)tileEntityResult);
        if (pressurePlateResult != WorldFilePressurePlateDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidPressurePlates, WorldFileLoadStage.PressurePlates, (int)pressurePlateResult);
        if (townRoomResult != WorldFileTownRoomDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidTownRooms, WorldFileLoadStage.TownRooms, (int)townRoomResult);
        if (bestiaryResult != WorldFileBestiaryDecodeResult.Decoded || bestiary is null)
            return Failure(WorldFileLoadResult.InvalidBestiary, WorldFileLoadStage.Bestiary, (int)bestiaryResult);
        if (creativeResult != WorldFileCreativePowersDecodeResult.Decoded || creativePowers is null)
            return Failure(WorldFileLoadResult.InvalidCreativePowers, WorldFileLoadStage.CreativePowers, (int)creativeResult);
        if (footerResult != WorldFileFooterValidationResult.Valid)
            return Failure(WorldFileLoadResult.InvalidFooter, WorldFileLoadStage.Footer, (int)footerResult);

        world = new WorldFileData(
            core.Envelope,
            core.Header,
            runtimeMetadata,
            core.Tiles,
            chests,
            signs,
            npcs,
            tileEntities,
            pressurePlates,
            townRooms,
            bestiary,
            creativePowers);

        return new WorldFileLoadDiagnostic(WorldFileLoadResult.Loaded, WorldFileLoadStage.Complete, 0);
    }
}
