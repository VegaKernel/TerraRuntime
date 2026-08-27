namespace TerraRuntime.World;

/// <summary>
/// Transactionally loads a complete Terraria 1.4.5.8 .wld file. No partially decoded state is returned:
/// every section and the footer must validate before <see cref="WorldFileData"/> is published.
/// </summary>
public static class WorldFileLoader
{
    public static WorldFileLoadDiagnostic TryLoad(
        ReadOnlySpan<byte> file,
        WorldFileLoadLimits limits,
        out WorldFileData? world)
    {
        limits.Validate();
        world = null;

        WorldFileCoreLoadDiagnostic coreLoad = WorldFileCoreLoader.TryLoad(
            file,
            limits.MaxTileCount,
            out WorldFileCore? core);
        if (coreLoad.Result != WorldFileCoreLoadResult.Loaded || core is null)
            return FromCoreFailure(coreLoad);

        WorldFileRuntimeMetadataParseResult metadataResult = WorldFileRuntimeMetadataParser.TryParse(
            file,
            core.Envelope,
            core.Header,
            limits.RuntimeMetadata,
            out WorldFileRuntimeMetadata? runtimeMetadata,
            out _);
        if (metadataResult != WorldFileRuntimeMetadataParseResult.Parsed || runtimeMetadata is null)
        {
            return Failure(
                WorldFileLoadResult.InvalidHeader,
                WorldFileLoadStage.Header,
                0x100 + (int)metadataResult);
        }

        WorldFileChestDecodeResult chestResult = WorldFileChestDecoder.TryDecode(
            file,
            core.Envelope,
            core.Header,
            limits.MaxItemsPerChest,
            limits.MaxTotalChestItems,
            out WorldChest[] chests,
            out _);
        if (chestResult != WorldFileChestDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidChests, WorldFileLoadStage.Chests, (int)chestResult);

        WorldFileSignDecodeResult signResult = WorldFileSignDecoder.TryDecode(
            file,
            core.Envelope,
            core.Header,
            limits.MaxTextBytesPerSign,
            limits.MaxTotalSignTextBytes,
            out WorldSign[] signs,
            out _);
        if (signResult != WorldFileSignDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidSigns, WorldFileLoadStage.Signs, (int)signResult);

        WorldFileNpcDecodeResult npcResult = WorldFileNpcDecoder.TryDecode(
            file,
            core.Envelope,
            limits.Npcs,
            out WorldNpcPersistence? npcs,
            out _);
        if (npcResult != WorldFileNpcDecodeResult.Decoded || npcs is null)
            return Failure(WorldFileLoadResult.InvalidNpcs, WorldFileLoadStage.Npcs, (int)npcResult);

        WorldFileTileEntityDecodeResult tileEntityResult = WorldFileTileEntityDecoder.TryDecode(
            file,
            core.Envelope,
            core.Header,
            limits.MaxTileEntities,
            out WorldTileEntity[] tileEntities,
            out _);
        if (tileEntityResult != WorldFileTileEntityDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidTileEntities, WorldFileLoadStage.TileEntities, (int)tileEntityResult);

        WorldFilePressurePlateDecodeResult pressurePlateResult = WorldFilePressurePlateDecoder.TryDecode(
            file,
            core.Envelope,
            core.Header,
            limits.MaxPressurePlates,
            out WorldPressurePlate[] pressurePlates,
            out _);
        if (pressurePlateResult != WorldFilePressurePlateDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidPressurePlates, WorldFileLoadStage.PressurePlates, (int)pressurePlateResult);

        WorldFileTownRoomDecodeResult townRoomResult = WorldFileTownRoomDecoder.TryDecode(
            file,
            core.Envelope,
            core.Header,
            limits.MaxTownRooms,
            out WorldTownRoom[] townRooms,
            out _);
        if (townRoomResult != WorldFileTownRoomDecodeResult.Decoded)
            return Failure(WorldFileLoadResult.InvalidTownRooms, WorldFileLoadStage.TownRooms, (int)townRoomResult);

        WorldFileBestiaryDecodeResult bestiaryResult = WorldFileBestiaryDecoder.TryDecode(
            file,
            core.Envelope,
            limits.Bestiary,
            out WorldBestiaryData? bestiary,
            out _);
        if (bestiaryResult != WorldFileBestiaryDecodeResult.Decoded || bestiary is null)
            return Failure(WorldFileLoadResult.InvalidBestiary, WorldFileLoadStage.Bestiary, (int)bestiaryResult);

        WorldFileCreativePowersDecodeResult creativeResult = WorldFileCreativePowersDecoder.TryDecode(
            file,
            core.Envelope,
            out WorldCreativePowersData? creativePowers,
            out _);
        if (creativeResult != WorldFileCreativePowersDecodeResult.Decoded || creativePowers is null)
            return Failure(WorldFileLoadResult.InvalidCreativePowers, WorldFileLoadStage.CreativePowers, (int)creativeResult);

        WorldFileFooterValidationResult footerResult = WorldFileFooterValidator.Validate(
            file,
            core.Envelope,
            core.Header,
            out _);
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

    private static WorldFileLoadDiagnostic FromCoreFailure(WorldFileCoreLoadDiagnostic core)
    {
        return core.Result switch
        {
            WorldFileCoreLoadResult.InvalidEnvelope => Failure(
                WorldFileLoadResult.InvalidEnvelope,
                WorldFileLoadStage.Envelope,
                core.EnvelopeResult.HasValue ? (int)core.EnvelopeResult.Value : -1),

            WorldFileCoreLoadResult.InvalidHeader => Failure(
                WorldFileLoadResult.InvalidHeader,
                WorldFileLoadStage.Header,
                core.HeaderResult.HasValue ? (int)core.HeaderResult.Value : -1),

            WorldFileCoreLoadResult.InvalidTiles => Failure(
                WorldFileLoadResult.InvalidTiles,
                WorldFileLoadStage.Tiles,
                core.TileResult.HasValue ? (int)core.TileResult.Value : -1),

            WorldFileCoreLoadResult.TileBudgetExceeded or WorldFileCoreLoadResult.TileStorageUnsupported => Failure(
                WorldFileLoadResult.InvalidTiles,
                WorldFileLoadStage.Tiles,
                0x100 + (int)core.Result),

            _ => Failure(WorldFileLoadResult.InvalidTiles, WorldFileLoadStage.Tiles, -1)
        };
    }

    private static WorldFileLoadDiagnostic Failure(
        WorldFileLoadResult result,
        WorldFileLoadStage stage,
        int stageResultCode) =>
        new(result, stage, stageResultCode);
}
