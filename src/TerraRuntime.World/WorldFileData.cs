namespace TerraRuntime.World;

/// <summary>
/// Fully validated Terraria 1.4.5.8 world persistence state. The instance is created only after every
/// current .wld section and the footer have been decoded successfully.
/// </summary>
public sealed record WorldFileData(
    WorldFileEnvelope Envelope,
    WorldFileHeader Header,
    WorldTileStore Tiles,
    WorldChest[] Chests,
    WorldSign[] Signs,
    WorldNpcPersistence Npcs,
    WorldTileEntity[] TileEntities,
    WorldPressurePlate[] PressurePlates,
    WorldTownRoom[] TownRooms,
    WorldBestiaryData Bestiary,
    WorldCreativePowersData CreativePowers);

public readonly record struct WorldFileLoadLimits(
    long MaxTileCount,
    int MaxItemsPerChest,
    long MaxTotalChestItems,
    int MaxTextBytesPerSign,
    long MaxTotalSignTextBytes,
    WorldFileNpcDecodeOptions Npcs,
    int MaxTileEntities,
    int MaxPressurePlates,
    int MaxTownRooms,
    WorldFileBestiaryLimits Bestiary)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxTileCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxItemsPerChest);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalChestItems);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTextBytesPerSign);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalSignTextBytes);
        Npcs.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTileEntities);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxPressurePlates);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTownRooms);
        Bestiary.Validate();
    }
}

public enum WorldFileLoadStage : byte
{
    Envelope = 0,
    Header = 1,
    Tiles = 2,
    Chests = 3,
    Signs = 4,
    Npcs = 5,
    TileEntities = 6,
    PressurePlates = 7,
    TownRooms = 8,
    Bestiary = 9,
    CreativePowers = 10,
    Footer = 11,
    Complete = 12
}

public enum WorldFileLoadResult : byte
{
    Loaded = 0,
    InvalidEnvelope = 1,
    InvalidHeader = 2,
    InvalidTiles = 3,
    InvalidChests = 4,
    InvalidSigns = 5,
    InvalidNpcs = 6,
    InvalidTileEntities = 7,
    InvalidPressurePlates = 8,
    InvalidTownRooms = 9,
    InvalidBestiary = 10,
    InvalidCreativePowers = 11,
    InvalidFooter = 12
}

public readonly record struct WorldFileLoadDiagnostic(
    WorldFileLoadResult Result,
    WorldFileLoadStage Stage,
    int StageResultCode)
{
    public bool IsLoaded => Result == WorldFileLoadResult.Loaded;
}
