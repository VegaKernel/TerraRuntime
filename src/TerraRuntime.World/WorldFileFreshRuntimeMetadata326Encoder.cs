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
/// Emits the SaveWorldFlags tail for a newly generated Terraria 1.4.5.8 world. Fresh vanilla seed switches and the
/// source-backed ordinary-world Reset bootstrap are persisted from the finalized generation snapshot; loaded-world
/// persistence still preserves existing opaque state.
/// </summary>
public static class WorldFileFreshRuntimeMetadata326Encoder
{
    public const double InitialTime = 13500d;
    public const int InitialCultistDelay = 86400;

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

        VanillaWorldSeedProfile1458 seeds = source.Generation.VanillaSeedProfile;
        VanillaWorldGenerationBootstrapState1458? bootstrap = source.Generation.VanillaBootstrapState;
        VanillaSpecialWorldSeed1458 special = seeds.Special;
        VanillaSecretWorldSeed1458 secret = seeds.Secret;
        bool rainingForever = seeds.Has(VanillaSecretWorldSeed1458.BringATowel);
        if (rainingForever && bootstrap is null)
            return WorldFileFreshRuntimeMetadata326EncodeResult.InvalidMetadata;
        bool startsBloodMoon = seeds.Has(VanillaSecretWorldSeed1458.NightOfTheLivingDead);
        bool startsHardmode = seeds.Has(VanillaSecretWorldSeed1458.TooEasy);
        bool teamSpawns = seeds.Has(VanillaSecretWorldSeed1458.RoyaleWithCheese);

        try
        {
            using var buffer = new MemoryStream(capacity: 512);
            using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((int)source.GameMode);
                writer.Write((special & VanillaSpecialWorldSeed1458.DrunkWorld) != 0);
                writer.Write((special & VanillaSpecialWorldSeed1458.ForTheWorthy) != 0);
                writer.Write((special & VanillaSpecialWorldSeed1458.CelebrationMk10) != 0);
                writer.Write((special & VanillaSpecialWorldSeed1458.TheConstant) != 0);
                writer.Write((special & VanillaSpecialWorldSeed1458.NotTheBees) != 0);
                writer.Write((special & VanillaSpecialWorldSeed1458.Remix) != 0);
                writer.Write((special & VanillaSpecialWorldSeed1458.NoTraps) != 0);
                writer.Write((special & VanillaSpecialWorldSeed1458.Zenith) != 0);
                writer.Write((special & VanillaSpecialWorldSeed1458.Skyblock) != 0);
                writer.Write(source.CreationTimeBinary);
                writer.Write(source.LastPlayedBinary);
                WriteResetVisualState(writer, bootstrap, header.Dimensions.WidthTiles);

                writer.Write(source.Generation.Spawn.X);
                writer.Write(source.Generation.Spawn.Y);
                writer.Write(source.Generation.Layers.WorldSurface);
                writer.Write(source.Generation.Layers.RockLayer);
                writer.Write(startsBloodMoon ? 1d : InitialTime);
                writer.Write(!startsBloodMoon);
                writer.Write(0);
                writer.Write(startsBloodMoon);
                writer.Write(false);
                writer.Write(source.Generation.Dungeon.X);
                writer.Write(source.Generation.Dungeon.Y);
                writer.Write(source.Crimson);

                WriteBools(writer, 11, false);
                WriteBools(writer, 3, false);
                WriteBools(writer, 4, false);
                writer.Write(false);
                writer.Write(false);
                writer.Write((byte)0);
                writer.Write(0);
                writer.Write(startsHardmode);
                writer.Write(bootstrap?.AfterPartyOfDoom ?? false);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0d);
                writer.Write(bootstrap is null ? -1d : bootstrap.SlimeRainTime);
                writer.Write((byte)0);
                writer.Write(bootstrap?.Raining ?? false);
                writer.Write(bootstrap?.RainTime ?? 0);
                writer.Write(bootstrap?.MaxRaining ?? 0f);
                writer.Write(-1);
                writer.Write(-1);
                writer.Write(-1);

                WritePrimaryBackgroundState(writer, bootstrap);
                writer.Write(bootstrap?.CloudBackgroundActive ?? 0);
                writer.Write((short)(bootstrap?.NumClouds ?? 0));
                writer.Write(bootstrap?.WindSpeedTarget ?? 0f);
                WorldAnglerQuestData1458 angler = source.Generation.AnglerQuest ?? WorldAnglerQuestData1458.Empty;
                writer.Write(angler.CompletedNames.Count);
                foreach (string name in angler.CompletedNames) writer.Write(name);
                writer.Write(false);
                writer.Write(angler.Quest);
                writer.Write(false);
                writer.Write(false);
                writer.Write(false);
                writer.Write(0);
                writer.Write(InitialCultistDelay);

                if (bootstrap is null)
                {
                    writer.Write((short)0);
                    writer.Write((short)0);
                }
                else
                {
                    writer.Write(new WorldBannerData1458(new int[WorldBannerData1458.MaximumEntries],
                        new ushort[WorldBannerData1458.MaximumEntries]).Encode());
                }

                writer.Write(false);
                WriteBools(writer, 13, false);
                WriteBools(writer, 5, false);
                writer.Write(false);
                writer.Write(seeds.Has(VanillaSpecialWorldSeed1458.CelebrationMk10) && !seeds.Has(VanillaSpecialWorldSeed1458.Skyblock));
                writer.Write(0);
                writer.Write(0);

                writer.Write(false);
                writer.Write(0);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(false);
                WriteBools(writer, 3, false);
                WriteSecondaryBackgroundState(writer, bootstrap);
                writer.Write(false);
                writer.Write(0);
                WriteBools(writer, 3, false);

                writer.Write(13);
                if (bootstrap is null)
                    WriteInts(writer, 13, 0);
                else
                {
                    WriteInts(writer, bootstrap.TreeStyle);
                    WriteInts(writer, [bootstrap.CorruptBackground, bootstrap.JungleBackground,
                        bootstrap.SnowBackground, bootstrap.HallowBackground, bootstrap.CrimsonBackground,
                        bootstrap.DesertBackground, bootstrap.OceanBackground, bootstrap.MushroomBackground,
                        bootstrap.UnderworldBackground]);
                }
                writer.Write(false);
                writer.Write(false);
                writer.Write(bootstrap?.CopperOre ?? CopperOre);
                writer.Write(bootstrap?.IronOre ?? IronOre);
                writer.Write(bootstrap?.SilverOre ?? SilverOre);
                writer.Write(bootstrap?.GoldOre ?? GoldOre);

                WriteBools(writer, 7, false);
                WriteBools(writer, 4, false);
                writer.Write(false);
                WriteBools(writer, 3, false);
                writer.Write(false);
                writer.Write(false);
                WriteBools(writer, 7, false);
                writer.Write(false);
                writer.Write((byte)0);
                writer.Write(seeds.Has(VanillaSecretWorldSeed1458.HocusPocus));
                writer.Write(seeds.Has(VanillaSecretWorldSeed1458.JingleAllTheWay));
                writer.Write(seeds.Has(VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse));
                writer.Write(seeds.Has(VanillaSecretWorldSeed1458.PurifyThis));
                writer.Write(0);
                writer.Write(0);
                writer.Write(teamSpawns);
                WriteExtraSpawnPoints(writer, source.Generation, header.Dimensions, teamSpawns);
                writer.Write(seeds.Has(VanillaSecretWorldSeed1458.DoubleDaringDangers));
                writer.Write(seeds.Has(VanillaSecretWorldSeed1458.ElectricBoogaloo));
                writer.Write(seeds.Has(VanillaSecretWorldSeed1458.CalmBeforeTheStorm));
                writer.Write(string.Empty);
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

    private static void WriteResetVisualState(
        BinaryWriter writer,
        VanillaWorldGenerationBootstrapState1458? bootstrap,
        int width)
    {
        writer.Write((byte)(bootstrap?.MoonType ?? 0));
        if (bootstrap is null)
        {
            WriteInts(writer, 3, width);
            WriteInts(writer, 4, 0);
            WriteInts(writer, 3, width);
            WriteInts(writer, 4, 0);
            WriteInts(writer, 3, 0);
            return;
        }

        WriteInts(writer, bootstrap.TreeX);
        WriteInts(writer, bootstrap.TreeStyle);
        WriteInts(writer, bootstrap.CaveBackX);
        WriteInts(writer, bootstrap.CaveBackStyle);
        writer.Write(bootstrap.IceBackStyle);
        writer.Write(bootstrap.JungleBackStyle);
        writer.Write(bootstrap.HellBackStyle);
    }

    private static void WritePrimaryBackgroundState(
        BinaryWriter writer,
        VanillaWorldGenerationBootstrapState1458? bootstrap)
    {
        if (bootstrap is null)
        {
            WriteBytes(writer, 8, 0);
            return;
        }

        writer.Write(checked((byte)bootstrap.ForestBackgroundStyles[0]));
        writer.Write(checked((byte)bootstrap.CorruptBackground));
        writer.Write(checked((byte)bootstrap.JungleBackground));
        writer.Write(checked((byte)bootstrap.SnowBackground));
        writer.Write(checked((byte)bootstrap.HallowBackground));
        writer.Write(checked((byte)bootstrap.CrimsonBackground));
        writer.Write(checked((byte)bootstrap.DesertBackground));
        writer.Write(checked((byte)bootstrap.OceanBackground));
    }

    private static void WriteSecondaryBackgroundState(
        BinaryWriter writer,
        VanillaWorldGenerationBootstrapState1458? bootstrap)
    {
        if (bootstrap is null)
        {
            WriteBytes(writer, 5, 0);
            return;
        }

        writer.Write(checked((byte)bootstrap.MushroomBackground));
        writer.Write(checked((byte)bootstrap.UnderworldBackground));
        writer.Write(checked((byte)bootstrap.ForestBackgroundStyles[1]));
        writer.Write(checked((byte)bootstrap.ForestBackgroundStyles[2]));
        writer.Write(checked((byte)bootstrap.ForestBackgroundStyles[3]));
    }

    private static void WriteExtraSpawnPoints(
        BinaryWriter writer,
        RuntimeWorldGenerationMetadataSnapshot generation,
        WorldDimensions dimensions,
        bool enabled)
    {
        WorldSpawnPoint[] points = generation.ExtraSpawnPoints ?? Array.Empty<WorldSpawnPoint>();
        writer.Write(checked((byte)points.Length));
        foreach (WorldSpawnPoint point in points) { writer.Write(point.X); writer.Write(point.Y); }
    }

    private static bool IsValidGenerationMetadata(
        WorldDimensions dimensions,
        in RuntimeWorldGenerationMetadataSnapshot metadata)
    {
        WorldSpawnPoint[]? points = metadata.ExtraSpawnPoints;
        if (metadata.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.RoyaleWithCheese) && points?.Length != 6)
            return false; // Source PlayerTeamID.Count; no fabricated fallback points.
        if (points is not null && (points.Length > byte.MaxValue || points.Any(point =>
                (uint)point.X >= (uint)dimensions.WidthTiles || (uint)point.Y >= (uint)dimensions.HeightTiles)))
            return false;
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

    private static void WriteInts(BinaryWriter writer, ReadOnlySpan<int> values)
    {
        foreach (int value in values)
            writer.Write(value);
    }

    private static void WriteBytes(BinaryWriter writer, int count, byte value)
    {
        for (int i = 0; i < count; i++)
            writer.Write(value);
    }
}
