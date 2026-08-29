using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public enum WorldFileFreshRuntimeMetadata326EncodeResult : byte
{
    Encoded = 0,
    InvalidDimensions = 1,
    InvalidMetadata = 2,
    DestinationNotWritable = 3,
    WriteFailed = 4
}

public readonly record struct WorldFileFreshRuntimeMetadata326(
    RuntimeWorldGenerationMetadataSnapshot Generation,
    byte GameMode,
    bool Crimson,
    long CreationTimeBinary,
    long LastPlayedBinary);

/// <summary>
/// Emits the SaveWorldFlags tail for a newly generated Terraria 1.4.5.8 world. This is intentionally fresh-only:
/// loaded-world persistence must preserve opaque progression/event state instead of resetting it through this path.
/// </summary>
public static class WorldFileFreshRuntimeMetadata326Encoder
{
    public const double InitialTime = 13500d;
    public const int InitialCultistDelay = 86400;

    // Base pre-hardmode ore IDs. Custom generators can initially expose the canonical tier identities even when a
    // minimal generator chooses not to place those ores. Hardmode tiers are -1 until the world enters hardmode.
    private const int CopperOre = 7;
    private const int IronOre = 6;
    private const int SilverOre = 9;
    private const int GoldOre = 8;

    public static WorldFileFreshRuntimeMetadata326EncodeResult TryEncode(
        WorldFileHeader header,
        in WorldFileFreshRuntimeMetadata326 source,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileFreshRuntimeMetadata326EncodeResult.DestinationNotWritable;

        if (header.Dimensions.WidthTiles > short.MaxValue ||
            header.Dimensions.HeightTiles > short.MaxValue)
        {
            return WorldFileFreshRuntimeMetadata326EncodeResult.InvalidDimensions;
        }

        if (source.GameMode > 3 || !IsValidGenerationMetadata(header.Dimensions, source.Generation))
            return WorldFileFreshRuntimeMetadata326EncodeResult.InvalidMetadata;

        try
        {
            using var buffer = new MemoryStream(capacity: 512);
            using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((int)source.GameMode);
                WriteBools(writer, 9, false); // special-world flags
                writer.Write(source.CreationTimeBinary);
                writer.Write(source.LastPlayedBinary);
                writer.Write((byte)0); // moon type

                WriteInts(writer, 3, header.Dimensions.WidthTiles); // tree background split X
                WriteInts(writer, 4, 0); // tree styles
                WriteInts(writer, 3, header.Dimensions.WidthTiles); // cave background split X
                WriteInts(writer, 4, 0); // cave styles
                WriteInts(writer, 3, 0); // ice/jungle/hell background styles

                writer.Write(source.Generation.Spawn.X);
                writer.Write(source.Generation.Spawn.Y);
                writer.Write(source.Generation.Layers.WorldSurface);
                writer.Write(source.Generation.Layers.RockLayer);
                writer.Write(InitialTime);
                writer.Write(true); // day time
                writer.Write(0); // moon phase
                writer.Write(false); // blood moon
                writer.Write(false); // eclipse
                writer.Write(source.Generation.Dungeon.X);
                writer.Write(source.Generation.Dungeon.Y);
                writer.Write(source.Crimson);

                WriteBools(writer, 11, false); // bosses / hardmode progression
                WriteBools(writer, 3, false); // rescued goblin/wizard/mechanic
                WriteBools(writer, 4, false); // invasions
                writer.Write(false); // shadow orb smashed
                writer.Write(false); // spawn meteor
                writer.Write((byte)0); // shadow orb count
                writer.Write(0); // altar count
                writer.Write(false); // hard mode
                writer.Write(false); // after party of doom
                writer.Write(0); // invasion delay
                writer.Write(0); // invasion size
                writer.Write(0); // invasion type
                writer.Write(0d); // invasion X
                writer.Write(-1d); // inactive slime-rain timer
                writer.Write((byte)0); // sundial cooldown
                writer.Write(false); // raining
                writer.Write(0); // rain time
                writer.Write(0f); // max rain
                writer.Write(-1); // cobalt/palladium tier not selected
                writer.Write(-1); // mythril/orichalcum tier not selected
                writer.Write(-1); // adamantite/titanium tier not selected

                WriteBytes(writer, 8, 0); // primary backgrounds
                writer.Write(0); // cloud background inactive
                writer.Write((short)0); // cloud count
                writer.Write(0f); // wind target
                writer.Write(0); // angler names count
                writer.Write(false); // saved angler
                writer.Write(0); // angler quest
                writer.Write(false); // saved stylist
                writer.Write(false); // saved tax collector
                writer.Write(false); // saved golfer
                writer.Write(0); // invasion size start
                writer.Write(InitialCultistDelay);

                writer.Write((short)0); // BannerSystem kill-count entries
                writer.Write((short)0); // BannerSystem claimed-banner entries

                writer.Write(false); // fast-forward dawn
                WriteBools(writer, 13, false); // late bosses/events/towers downed
                WriteBools(writer, 5, false); // active towers + lunar apocalypse
                writer.Write(false); // party manual
                writer.Write(false); // party genuine
                writer.Write(0); // party cooldown
                writer.Write(0); // celebrating NPC count

                writer.Write(false); // sandstorm
                writer.Write(0); // sandstorm time left
                writer.Write(0f); // sandstorm severity
                writer.Write(0f); // sandstorm intended severity
                writer.Write(false); // saved bartender
                WriteBools(writer, 3, false); // DD2 tiers
                WriteBytes(writer, 5, 0); // mushroom/underworld/tree BG 2..4
                writer.Write(false); // combat book
                writer.Write(0); // lantern cooldown
                WriteBools(writer, 3, false); // lantern genuine/manual/next

                writer.Write(13); // TreeTopsInfo fixed variation count
                WriteInts(writer, 13, 0);
                writer.Write(false); // Halloween today
                writer.Write(false); // Xmas today
                writer.Write(CopperOre);
                writer.Write(IronOre);
                writer.Write(SilverOre);
                writer.Write(GoldOre);

                WriteBools(writer, 7, false); // pets + bosses + blue slime
                WriteBools(writer, 4, false); // merchant/demolitionist/party girl/dye trader unlocks
                writer.Write(false); // truffle unlock
                WriteBools(writer, 3, false); // arms dealer/nurse/princess unlocks
                writer.Write(false); // combat book volume two
                writer.Write(false); // peddler's satchel
                WriteBools(writer, 7, false); // remaining town slimes
                writer.Write(false); // fast-forward dusk
                writer.Write((byte)0); // moondial cooldown
                writer.Write(false); // Halloween forever
                writer.Write(false); // Xmas forever
                writer.Write(false); // vampire seed
                writer.Write(false); // infected seed
                writer.Write(0); // meteor shower count
                writer.Write(0); // coin rain
                writer.Write(false); // team-based spawns seed
                writer.Write((byte)0); // extra spawn points
                writer.Write(false); // dual dungeons seed
                writer.Write(false); // more lightning seed
                writer.Write(false); // no lightning seed
                writer.Write(string.Empty); // valid empty WorldManifest; custom passes are not vanilla WorldGen passes
                writer.Flush();
            }

            buffer.Position = 0;
            buffer.CopyTo(destination);
            bytesWritten = buffer.Length;
            return WorldFileFreshRuntimeMetadata326EncodeResult.Encoded;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileFreshRuntimeMetadata326EncodeResult.WriteFailed;
        }
    }

    private static bool IsValidGenerationMetadata(
        WorldDimensions dimensions,
        in RuntimeWorldGenerationMetadataSnapshot metadata)
    {
        if ((uint)metadata.Spawn.X >= (uint)dimensions.WidthTiles ||
            (uint)metadata.Spawn.Y >= (uint)dimensions.HeightTiles ||
            (uint)metadata.Dungeon.X >= (uint)dimensions.WidthTiles ||
            (uint)metadata.Dungeon.Y >= (uint)dimensions.HeightTiles)
        {
            return false;
        }

        return double.IsFinite(metadata.Layers.WorldSurface) &&
            double.IsFinite(metadata.Layers.RockLayer) &&
            metadata.Layers.WorldSurface > 0d &&
            metadata.Layers.WorldSurface < metadata.Layers.RockLayer &&
            metadata.Layers.RockLayer < dimensions.HeightTiles &&
            metadata.Layers.WorldSurface <= short.MaxValue &&
            metadata.Layers.RockLayer <= short.MaxValue;
    }

    private static void WriteBools(BinaryWriter writer, int count, bool value)
    {
        for (int i = 0; i < count; i++)
            writer.Write(value);
    }

    private static void WriteInts(BinaryWriter writer, int count, int value)
    {
        for (int i = 0; i < count; i++)
            writer.Write(value);
    }

    private static void WriteBytes(BinaryWriter writer, int count, byte value)
    {
        for (int i = 0; i < count; i++)
            writer.Write(value);
    }
}
