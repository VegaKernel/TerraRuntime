using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.World;

public enum WorldFileRuntimeMetadataParseResult : byte
{
    Parsed = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    Truncated = 3,
    InvalidStringLength = 4,
    StringTooLarge = 5,
    StringBudgetExceeded = 6,
    InvalidUtf8 = 7,
    HeaderMismatch = 8,
    InvalidCount = 9,
    BudgetExceeded = 10,
    InvalidScalar = 11,
    SectionLengthMismatch = 12
}

/// <summary>
/// Decodes the complete Terraria 1.4.5.8 world-header state stored between section pointers 0 and 1.
/// The lightweight <see cref="WorldFileHeaderParser"/> remains responsible for identity/dimensions;
/// this parser consumes all SaveWorldFlags data and retains the save-backed state needed by runtime/networking.
/// </summary>
public static class WorldFileRuntimeMetadataParser
{
    private const int TreeTopVariationCount = 13;

    public static WorldFileRuntimeMetadataParseResult TryParse(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        WorldFileRuntimeMetadataLimits limits,
        out WorldFileRuntimeMetadata? metadata,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        limits.Validate();
        metadata = null;
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileRuntimeMetadataParseResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < 2)
            return WorldFileRuntimeMetadataParseResult.InvalidSectionBounds;

        int start = envelope.SectionOffsets[0];
        int end = envelope.SectionOffsets[1];
        if (start < 0 || end <= start || end > file.Length)
            return WorldFileRuntimeMetadataParseResult.InvalidSectionBounds;

        var reader = new MetadataReader(file.Slice(start, end - start));
        long totalStringBytes = 0;

        WorldFileRuntimeMetadataParseResult result = ReadString(
            ref reader,
            limits.MaxStringBytes,
            limits.MaxTotalStringBytes,
            ref totalStringBytes,
            out string name);
        if (result != WorldFileRuntimeMetadataParseResult.Parsed)
            return Finish(result, ref reader, out bytesConsumed);
        result = ReadString(
            ref reader,
            limits.MaxStringBytes,
            limits.MaxTotalStringBytes,
            ref totalStringBytes,
            out string seed);
        if (result != WorldFileRuntimeMetadataParseResult.Parsed)
            return Finish(result, ref reader, out bytesConsumed);

        if (!reader.TryReadUInt64(out ulong generatorVersion) ||
            !reader.TryReadGuid(out Guid uniqueId) ||
            !reader.TryReadInt32(out int worldId) ||
            !reader.TryReadInt32(out int leftWorld) ||
            !reader.TryReadInt32(out int rightWorld) ||
            !reader.TryReadInt32(out int topWorld) ||
            !reader.TryReadInt32(out int bottomWorld) ||
            !reader.TryReadInt32(out int heightTiles) ||
            !reader.TryReadInt32(out int widthTiles))
        {
            return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
        }

        if (name != header.Name || seed != header.SeedText || generatorVersion != header.WorldGeneratorVersion ||
            uniqueId != header.UniqueId || worldId != header.WorldId || leftWorld != header.LeftWorld ||
            rightWorld != header.RightWorld || topWorld != header.TopWorld || bottomWorld != header.BottomWorld ||
            heightTiles != header.Dimensions.HeightTiles || widthTiles != header.Dimensions.WidthTiles)
        {
            return Finish(WorldFileRuntimeMetadataParseResult.HeaderMismatch, ref reader, out bytesConsumed);
        }

        if (widthTiles > short.MaxValue || heightTiles > short.MaxValue)
            return Finish(WorldFileRuntimeMetadataParseResult.InvalidScalar, ref reader, out bytesConsumed);

        if (!reader.TryReadInt32(out int gameMode) || gameMode is < 0 or > 3)
            return Finish(reader.Remaining < 0 ? WorldFileRuntimeMetadataParseResult.Truncated : WorldFileRuntimeMetadataParseResult.InvalidScalar, ref reader, out bytesConsumed);

        if (!ReadBool(ref reader, out bool drunkWorld) ||
            !ReadBool(ref reader, out bool getGoodWorld) ||
            !ReadBool(ref reader, out bool tenthAnniversaryWorld) ||
            !ReadBool(ref reader, out bool dontStarveWorld) ||
            !ReadBool(ref reader, out bool notTheBeesWorld) ||
            !ReadBool(ref reader, out bool remixWorld) ||
            !ReadBool(ref reader, out bool noTrapsWorld) ||
            !ReadBool(ref reader, out bool zenithWorld) ||
            !ReadBool(ref reader, out bool skyblockWorld) ||
            !reader.TryReadInt64(out _) ||
            !reader.TryReadInt64(out _) ||
            !reader.TryReadByte(out byte moonType))
        {
            return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
        }

        int[] treeX = new int[3];
        for (int i = 0; i < treeX.Length; i++)
        {
            if (!reader.TryReadInt32(out treeX[i]))
                return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
        }

        byte[] treeStyles = new byte[4];
        for (int i = 0; i < treeStyles.Length; i++)
        {
            if (!TryReadByteCompatibleInt32(ref reader, out treeStyles[i], out result))
                return Finish(result, ref reader, out bytesConsumed);
        }

        int[] caveBackX = new int[3];
        for (int i = 0; i < caveBackX.Length; i++)
        {
            if (!reader.TryReadInt32(out caveBackX[i]))
                return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
        }

        byte[] caveBackStyles = new byte[4];
        for (int i = 0; i < caveBackStyles.Length; i++)
        {
            if (!TryReadByteCompatibleInt32(ref reader, out caveBackStyles[i], out result))
                return Finish(result, ref reader, out bytesConsumed);
        }

        if (!TryReadByteCompatibleInt32(ref reader, out byte iceBackStyle, out result) ||
            !TryReadByteCompatibleInt32(ref reader, out byte jungleBackStyle, out result) ||
            !TryReadByteCompatibleInt32(ref reader, out byte hellBackStyle, out result))
        {
            return Finish(result, ref reader, out bytesConsumed);
        }

        if (!TryReadInt16CompatibleInt32(ref reader, out short spawnX, out result) ||
            !TryReadInt16CompatibleInt32(ref reader, out short spawnY, out result) ||
            !TryReadInt16CompatibleDouble(ref reader, out short worldSurface, out result) ||
            !TryReadInt16CompatibleDouble(ref reader, out short rockLayer, out result) ||
            !TryReadInt32CompatibleDouble(ref reader, out int time, out result) ||
            !ReadBool(ref reader, out bool dayTime) ||
            !TryReadByteCompatibleInt32(ref reader, out byte moonPhase, out result) ||
            !ReadBool(ref reader, out bool bloodMoon) ||
            !ReadBool(ref reader, out bool eclipse) ||
            !TryReadInt16CompatibleInt32(ref reader, out short dungeonX, out result) ||
            !TryReadInt16CompatibleInt32(ref reader, out short dungeonY, out result))
        {
            return Finish(result == WorldFileRuntimeMetadataParseResult.Parsed ? WorldFileRuntimeMetadataParseResult.Truncated : result, ref reader, out bytesConsumed);
        }

        if (!ReadBool(ref reader, out bool crimson) ||
            !ReadBool(ref reader, out bool downedBoss1) ||
            !ReadBool(ref reader, out bool downedBoss2) ||
            !ReadBool(ref reader, out bool downedBoss3) ||
            !ReadBool(ref reader, out bool downedQueenBee) ||
            !ReadBool(ref reader, out bool downedMechBoss1) ||
            !ReadBool(ref reader, out bool downedMechBoss2) ||
            !ReadBool(ref reader, out bool downedMechBoss3) ||
            !ReadBool(ref reader, out bool downedMechBossAny) ||
            !ReadBool(ref reader, out bool downedPlantBoss) ||
            !ReadBool(ref reader, out bool downedGolemBoss) ||
            !ReadBool(ref reader, out bool downedSlimeKing) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out bool downedGoblins) ||
            !ReadBool(ref reader, out bool downedClown) ||
            !ReadBool(ref reader, out bool downedFrost) ||
            !ReadBool(ref reader, out bool downedPirates) ||
            !ReadBool(ref reader, out bool shadowOrbSmashed) ||
            !ReadBool(ref reader, out _) ||
            !reader.TryReadByte(out _) ||
            !reader.TryReadInt32(out _) ||
            !ReadBool(ref reader, out bool hardMode) ||
            !ReadBool(ref reader, out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out _))
        {
            return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
        }

        if (!TryReadSByteCompatibleInt32(ref reader, out sbyte invasionType, out result) ||
            !reader.TryReadDouble(out double invasionX) || !double.IsFinite(invasionX) ||
            !reader.TryReadDouble(out double slimeRainTime) || !double.IsFinite(slimeRainTime) ||
            !reader.TryReadByte(out byte sundialCooldown) ||
            !ReadBool(ref reader, out bool raining) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadSingle(out float maxRain) || !float.IsFinite(maxRain) ||
            !TryReadInt16CompatibleInt32(ref reader, out short oreCobalt, out result) ||
            !TryReadInt16CompatibleInt32(ref reader, out short oreMythril, out result) ||
            !TryReadInt16CompatibleInt32(ref reader, out short oreAdamantite, out result))
        {
            return Finish(result == WorldFileRuntimeMetadataParseResult.Parsed ? WorldFileRuntimeMetadataParseResult.InvalidScalar : result, ref reader, out bytesConsumed);
        }

        if (!reader.TryReadByte(out byte treeBackground) ||
            !reader.TryReadByte(out byte corruptionBackground) ||
            !reader.TryReadByte(out byte jungleBackground) ||
            !reader.TryReadByte(out byte snowBackground) ||
            !reader.TryReadByte(out byte hallowBackground) ||
            !reader.TryReadByte(out byte crimsonBackground) ||
            !reader.TryReadByte(out byte desertBackground) ||
            !reader.TryReadByte(out byte oceanBackground) ||
            !reader.TryReadInt32(out int cloudBackground) ||
            !reader.TryReadInt16(out short cloudCountRaw) || cloudCountRaw is < 0 or > byte.MaxValue ||
            !reader.TryReadSingle(out float windSpeed) || !float.IsFinite(windSpeed) ||
            !reader.TryReadInt32(out int anglerCount))
        {
            return Finish(WorldFileRuntimeMetadataParseResult.InvalidScalar, ref reader, out bytesConsumed);
        }

        if (anglerCount < 0)
            return Finish(WorldFileRuntimeMetadataParseResult.InvalidCount, ref reader, out bytesConsumed);
        if (anglerCount > limits.MaxAnglerNames)
            return Finish(WorldFileRuntimeMetadataParseResult.BudgetExceeded, ref reader, out bytesConsumed);

        for (int i = 0; i < anglerCount; i++)
        {
            result = ReadString(ref reader, limits.MaxStringBytes, limits.MaxTotalStringBytes, ref totalStringBytes, out _);
            if (result != WorldFileRuntimeMetadataParseResult.Parsed)
                return Finish(result, ref reader, out bytesConsumed);
        }

        if (!ReadBool(ref reader, out _) ||
            !reader.TryReadInt32(out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out _))
        {
            return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
        }

        if (!TrySkipBannerSystem(ref reader, limits.MaxBannerEntries, out result))
            return Finish(result, ref reader, out bytesConsumed);

        if (!ReadBool(ref reader, out bool fastForwardTimeToDawn) ||
            !ReadBool(ref reader, out bool downedFishron) ||
            !ReadBool(ref reader, out bool downedMartians) ||
            !ReadBool(ref reader, out bool downedAncientCultist) ||
            !ReadBool(ref reader, out bool downedMoonlord) ||
            !ReadBool(ref reader, out bool downedHalloweenKing) ||
            !ReadBool(ref reader, out bool downedHalloweenTree) ||
            !ReadBool(ref reader, out bool downedChristmasIceQueen) ||
            !ReadBool(ref reader, out bool downedChristmasSantank) ||
            !ReadBool(ref reader, out bool downedChristmasTree) ||
            !ReadBool(ref reader, out bool downedTowerSolar) ||
            !ReadBool(ref reader, out bool downedTowerVortex) ||
            !ReadBool(ref reader, out bool downedTowerNebula) ||
            !ReadBool(ref reader, out bool downedTowerStardust) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out bool partyManual) ||
            !ReadBool(ref reader, out bool partyGenuine) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out int partyNpcCount))
        {
            return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
        }

        if (partyNpcCount < 0)
            return Finish(WorldFileRuntimeMetadataParseResult.InvalidCount, ref reader, out bytesConsumed);
        if (partyNpcCount > limits.MaxPartyNpcEntries)
            return Finish(WorldFileRuntimeMetadataParseResult.BudgetExceeded, ref reader, out bytesConsumed);
        for (int i = 0; i < partyNpcCount; i++)
        {
            if (!reader.TryReadInt32(out _))
                return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
        }

        if (!ReadBool(ref reader, out bool sandstormHappening) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadSingle(out float sandstormSeverity) || !float.IsFinite(sandstormSeverity) ||
            !reader.TryReadSingle(out float sandstormIntendedSeverity) || !float.IsFinite(sandstormIntendedSeverity) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out bool downedDd2T1) ||
            !ReadBool(ref reader, out bool downedDd2T2) ||
            !ReadBool(ref reader, out bool downedDd2T3) ||
            !reader.TryReadByte(out byte mushroomBackground) ||
            !reader.TryReadByte(out byte underworldBackground) ||
            !reader.TryReadByte(out byte treeBackground2) ||
            !reader.TryReadByte(out byte treeBackground3) ||
            !reader.TryReadByte(out byte treeBackground4) ||
            !ReadBool(ref reader, out bool combatBookWasUsed) ||
            !reader.TryReadInt32(out _) ||
            !ReadBool(ref reader, out bool lanternNightGenuine) ||
            !ReadBool(ref reader, out bool lanternNightManual) ||
            !ReadBool(ref reader, out _) ||
            !reader.TryReadInt32(out int treeTopCount))
        {
            return Finish(WorldFileRuntimeMetadataParseResult.InvalidScalar, ref reader, out bytesConsumed);
        }

        if (treeTopCount != TreeTopVariationCount)
            return Finish(WorldFileRuntimeMetadataParseResult.InvalidCount, ref reader, out bytesConsumed);
        byte[] treeTopVariations = new byte[TreeTopVariationCount];
        for (int i = 0; i < treeTopVariations.Length; i++)
        {
            if (!TryReadByteCompatibleInt32(ref reader, out treeTopVariations[i], out result))
                return Finish(result, ref reader, out bytesConsumed);
        }

        if (!ReadBool(ref reader, out bool forceHalloweenForToday) ||
            !ReadBool(ref reader, out bool forceXMasForToday) ||
            !TryReadInt16CompatibleInt32(ref reader, out short oreCopper, out result) ||
            !TryReadInt16CompatibleInt32(ref reader, out short oreIron, out result) ||
            !TryReadInt16CompatibleInt32(ref reader, out short oreSilver, out result) ||
            !TryReadInt16CompatibleInt32(ref reader, out short oreGold, out result) ||
            !ReadBool(ref reader, out bool boughtCat) ||
            !ReadBool(ref reader, out bool boughtDog) ||
            !ReadBool(ref reader, out bool boughtBunny) ||
            !ReadBool(ref reader, out bool downedEmpressOfLight) ||
            !ReadBool(ref reader, out bool downedQueenSlime) ||
            !ReadBool(ref reader, out bool downedDeerclops) ||
            !ReadBool(ref reader, out bool unlockedSlimeBlueSpawn) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out bool unlockedTruffleSpawn) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out _) ||
            !ReadBool(ref reader, out bool combatBookVolumeTwoWasUsed) ||
            !ReadBool(ref reader, out bool peddlersSatchelWasUsed) ||
            !ReadBool(ref reader, out bool unlockedSlimeGreenSpawn) ||
            !ReadBool(ref reader, out bool unlockedSlimeOldSpawn) ||
            !ReadBool(ref reader, out bool unlockedSlimePurpleSpawn) ||
            !ReadBool(ref reader, out bool unlockedSlimeRainbowSpawn) ||
            !ReadBool(ref reader, out bool unlockedSlimeRedSpawn) ||
            !ReadBool(ref reader, out bool unlockedSlimeYellowSpawn) ||
            !ReadBool(ref reader, out bool unlockedSlimeCopperSpawn) ||
            !ReadBool(ref reader, out bool fastForwardTimeToDusk) ||
            !reader.TryReadByte(out byte moondialCooldown) ||
            !ReadBool(ref reader, out bool forceHalloweenForever) ||
            !ReadBool(ref reader, out bool forceXMasForever) ||
            !ReadBool(ref reader, out bool vampireSeed) ||
            !ReadBool(ref reader, out bool infectedSeed) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out _) ||
            !ReadBool(ref reader, out bool teamBasedSpawnsSeed) ||
            !reader.TryReadByte(out byte extraSpawnCount))
        {
            return Finish(result == WorldFileRuntimeMetadataParseResult.Parsed ? WorldFileRuntimeMetadataParseResult.Truncated : result, ref reader, out bytesConsumed);
        }

        WorldSpawnPoint[] extraSpawnPoints = new WorldSpawnPoint[extraSpawnCount];
        for (int i = 0; i < extraSpawnPoints.Length; i++)
        {
            if (!reader.TryReadInt16(out short x) || !reader.TryReadInt16(out short y))
                return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
            extraSpawnPoints[i] = new WorldSpawnPoint(x, y);
        }

        if (!ReadBool(ref reader, out bool dualDungeonsSeed) ||
            !ReadBool(ref reader, out bool moreLightningSeed) ||
            !ReadBool(ref reader, out bool noLightningSeed))
        {
            return Finish(WorldFileRuntimeMetadataParseResult.Truncated, ref reader, out bytesConsumed);
        }

        result = ReadString(
            ref reader,
            limits.MaxManifestBytes,
            limits.MaxTotalStringBytes,
            ref totalStringBytes,
            out _);
        if (result != WorldFileRuntimeMetadataParseResult.Parsed)
            return Finish(result, ref reader, out bytesConsumed);

        bytesConsumed = reader.Offset;
        if (reader.Remaining != 0)
            return WorldFileRuntimeMetadataParseResult.SectionLengthMismatch;

        metadata = new WorldFileRuntimeMetadata
        {
            GameMode = (byte)gameMode,
            DrunkWorld = drunkWorld,
            GetGoodWorld = getGoodWorld,
            TenthAnniversaryWorld = tenthAnniversaryWorld,
            DontStarveWorld = dontStarveWorld,
            NotTheBeesWorld = notTheBeesWorld,
            RemixWorld = remixWorld,
            NoTrapsWorld = noTrapsWorld,
            ZenithWorld = zenithWorld,
            SkyblockWorld = skyblockWorld,
            VampireSeed = vampireSeed,
            InfectedSeed = infectedSeed,
            TeamBasedSpawnsSeed = teamBasedSpawnsSeed,
            DualDungeonsSeed = dualDungeonsSeed,
            MoreLightningSeed = moreLightningSeed,
            NoLightningSeed = noLightningSeed,
            MoonType = moonType,
            TreeX = treeX,
            TreeStyles = treeStyles,
            CaveBackX = caveBackX,
            CaveBackStyles = caveBackStyles,
            IceBackStyle = iceBackStyle,
            JungleBackStyle = jungleBackStyle,
            HellBackStyle = hellBackStyle,
            SpawnX = spawnX,
            SpawnY = spawnY,
            WorldSurface = worldSurface,
            RockLayer = rockLayer,
            Time = time,
            DayTime = dayTime,
            MoonPhase = moonPhase,
            BloodMoon = bloodMoon,
            Eclipse = eclipse,
            DungeonX = dungeonX,
            DungeonY = dungeonY,
            Crimson = crimson,
            DownedBoss1 = downedBoss1,
            DownedBoss2 = downedBoss2,
            DownedBoss3 = downedBoss3,
            DownedQueenBee = downedQueenBee,
            DownedMechBoss1 = downedMechBoss1,
            DownedMechBoss2 = downedMechBoss2,
            DownedMechBoss3 = downedMechBoss3,
            DownedMechBossAny = downedMechBossAny,
            DownedPlantBoss = downedPlantBoss,
            DownedGolemBoss = downedGolemBoss,
            DownedSlimeKing = downedSlimeKing,
            DownedGoblins = downedGoblins,
            DownedClown = downedClown,
            DownedFrost = downedFrost,
            DownedPirates = downedPirates,
            ShadowOrbSmashed = shadowOrbSmashed,
            HardMode = hardMode,
            SlimeRainTime = slimeRainTime,
            SundialCooldown = sundialCooldown,
            Raining = raining,
            MaxRain = maxRain,
            OreTiers = new WorldOreTiers(oreCopper, oreIron, oreSilver, oreGold, oreCobalt, oreMythril, oreAdamantite),
            TreeBackground = treeBackground,
            TreeBackground2 = treeBackground2,
            TreeBackground3 = treeBackground3,
            TreeBackground4 = treeBackground4,
            CorruptionBackground = corruptionBackground,
            JungleBackground = jungleBackground,
            SnowBackground = snowBackground,
            HallowBackground = hallowBackground,
            CrimsonBackground = crimsonBackground,
            DesertBackground = desertBackground,
            OceanBackground = oceanBackground,
            MushroomBackground = mushroomBackground,
            UnderworldBackground = underworldBackground,
            CloudBackgroundActive = cloudBackground >= 1,
            CloudCount = (byte)cloudCountRaw,
            WindSpeed = windSpeed,
            FastForwardTimeToDawn = fastForwardTimeToDawn,
            DownedFishron = downedFishron,
            DownedMartians = downedMartians,
            DownedAncientCultist = downedAncientCultist,
            DownedMoonlord = downedMoonlord,
            DownedHalloweenKing = downedHalloweenKing,
            DownedHalloweenTree = downedHalloweenTree,
            DownedChristmasIceQueen = downedChristmasIceQueen,
            DownedChristmasSantank = downedChristmasSantank,
            DownedChristmasTree = downedChristmasTree,
            DownedTowerSolar = downedTowerSolar,
            DownedTowerVortex = downedTowerVortex,
            DownedTowerNebula = downedTowerNebula,
            DownedTowerStardust = downedTowerStardust,
            PartyManual = partyManual,
            PartyGenuine = partyGenuine,
            SandstormHappening = sandstormHappening,
            SandstormIntendedSeverity = sandstormIntendedSeverity,
            DownedDd2InvasionT1 = downedDd2T1,
            DownedDd2InvasionT2 = downedDd2T2,
            DownedDd2InvasionT3 = downedDd2T3,
            CombatBookWasUsed = combatBookWasUsed,
            LanternNightGenuine = lanternNightGenuine,
            LanternNightManual = lanternNightManual,
            TreeTopVariations = treeTopVariations,
            ForceHalloweenForToday = forceHalloweenForToday,
            ForceXMasForToday = forceXMasForToday,
            BoughtCat = boughtCat,
            BoughtDog = boughtDog,
            BoughtBunny = boughtBunny,
            DownedEmpressOfLight = downedEmpressOfLight,
            DownedQueenSlime = downedQueenSlime,
            DownedDeerclops = downedDeerclops,
            UnlockedSlimeBlueSpawn = unlockedSlimeBlueSpawn,
            UnlockedTruffleSpawn = unlockedTruffleSpawn,
            CombatBookVolumeTwoWasUsed = combatBookVolumeTwoWasUsed,
            PeddlersSatchelWasUsed = peddlersSatchelWasUsed,
            UnlockedSlimeGreenSpawn = unlockedSlimeGreenSpawn,
            UnlockedSlimeOldSpawn = unlockedSlimeOldSpawn,
            UnlockedSlimePurpleSpawn = unlockedSlimePurpleSpawn,
            UnlockedSlimeRainbowSpawn = unlockedSlimeRainbowSpawn,
            UnlockedSlimeRedSpawn = unlockedSlimeRedSpawn,
            UnlockedSlimeYellowSpawn = unlockedSlimeYellowSpawn,
            UnlockedSlimeCopperSpawn = unlockedSlimeCopperSpawn,
            FastForwardTimeToDusk = fastForwardTimeToDusk,
            MoondialCooldown = moondialCooldown,
            ForceHalloweenForever = forceHalloweenForever,
            ForceXMasForever = forceXMasForever,
            InvasionType = invasionType,
            ExtraSpawnPoints = extraSpawnPoints
        };
        return WorldFileRuntimeMetadataParseResult.Parsed;
    }

    private static WorldFileRuntimeMetadataParseResult ReadString(
        ref MetadataReader reader,
        int maxBytes,
        long maxTotalBytes,
        ref long totalBytes,
        out string value)
    {
        WorldFileRuntimeMetadataParseResult result = reader.TryReadString(maxBytes, out value, out int byteCount);
        if (result != WorldFileRuntimeMetadataParseResult.Parsed)
            return result;
        if (byteCount > maxTotalBytes - totalBytes)
            return WorldFileRuntimeMetadataParseResult.StringBudgetExceeded;
        totalBytes += byteCount;
        return WorldFileRuntimeMetadataParseResult.Parsed;
    }

    private static bool TrySkipBannerSystem(
        ref MetadataReader reader,
        int maxEntries,
        out WorldFileRuntimeMetadataParseResult result)
    {
        result = WorldFileRuntimeMetadataParseResult.Parsed;
        if (!reader.TryReadInt16(out short killCount))
        {
            result = WorldFileRuntimeMetadataParseResult.Truncated;
            return false;
        }
        if (killCount < 0)
        {
            result = WorldFileRuntimeMetadataParseResult.InvalidCount;
            return false;
        }
        if (killCount > maxEntries)
        {
            result = WorldFileRuntimeMetadataParseResult.BudgetExceeded;
            return false;
        }
        for (int i = 0; i < killCount; i++)
        {
            if (!reader.TryReadInt32(out _))
            {
                result = WorldFileRuntimeMetadataParseResult.Truncated;
                return false;
            }
        }

        if (!reader.TryReadInt16(out short claimCount))
        {
            result = WorldFileRuntimeMetadataParseResult.Truncated;
            return false;
        }
        if (claimCount < 0)
        {
            result = WorldFileRuntimeMetadataParseResult.InvalidCount;
            return false;
        }
        if (claimCount > maxEntries)
        {
            result = WorldFileRuntimeMetadataParseResult.BudgetExceeded;
            return false;
        }
        for (int i = 0; i < claimCount; i++)
        {
            if (!reader.TryReadUInt16(out _))
            {
                result = WorldFileRuntimeMetadataParseResult.Truncated;
                return false;
            }
        }
        return true;
    }

    private static bool TryReadByteCompatibleInt32(
        ref MetadataReader reader,
        out byte value,
        out WorldFileRuntimeMetadataParseResult result)
    {
        value = default;
        if (!reader.TryReadInt32(out int raw))
        {
            result = WorldFileRuntimeMetadataParseResult.Truncated;
            return false;
        }
        if (raw is < byte.MinValue or > byte.MaxValue)
        {
            result = WorldFileRuntimeMetadataParseResult.InvalidScalar;
            return false;
        }
        value = (byte)raw;
        result = WorldFileRuntimeMetadataParseResult.Parsed;
        return true;
    }

    private static bool TryReadInt16CompatibleInt32(
        ref MetadataReader reader,
        out short value,
        out WorldFileRuntimeMetadataParseResult result)
    {
        value = default;
        if (!reader.TryReadInt32(out int raw))
        {
            result = WorldFileRuntimeMetadataParseResult.Truncated;
            return false;
        }
        if (raw is < short.MinValue or > short.MaxValue)
        {
            result = WorldFileRuntimeMetadataParseResult.InvalidScalar;
            return false;
        }
        value = (short)raw;
        result = WorldFileRuntimeMetadataParseResult.Parsed;
        return true;
    }

    private static bool TryReadSByteCompatibleInt32(
        ref MetadataReader reader,
        out sbyte value,
        out WorldFileRuntimeMetadataParseResult result)
    {
        value = default;
        if (!reader.TryReadInt32(out int raw))
        {
            result = WorldFileRuntimeMetadataParseResult.Truncated;
            return false;
        }
        if (raw is < sbyte.MinValue or > sbyte.MaxValue)
        {
            result = WorldFileRuntimeMetadataParseResult.InvalidScalar;
            return false;
        }
        value = (sbyte)raw;
        result = WorldFileRuntimeMetadataParseResult.Parsed;
        return true;
    }

    private static bool TryReadInt16CompatibleDouble(
        ref MetadataReader reader,
        out short value,
        out WorldFileRuntimeMetadataParseResult result)
    {
        value = default;
        if (!reader.TryReadDouble(out double raw))
        {
            result = WorldFileRuntimeMetadataParseResult.Truncated;
            return false;
        }
        if (!double.IsFinite(raw) || raw < short.MinValue || raw > short.MaxValue)
        {
            result = WorldFileRuntimeMetadataParseResult.InvalidScalar;
            return false;
        }
        value = (short)raw;
        result = WorldFileRuntimeMetadataParseResult.Parsed;
        return true;
    }

    private static bool TryReadInt32CompatibleDouble(
        ref MetadataReader reader,
        out int value,
        out WorldFileRuntimeMetadataParseResult result)
    {
        value = default;
        if (!reader.TryReadDouble(out double raw))
        {
            result = WorldFileRuntimeMetadataParseResult.Truncated;
            return false;
        }
        if (!double.IsFinite(raw) || raw < int.MinValue || raw > int.MaxValue)
        {
            result = WorldFileRuntimeMetadataParseResult.InvalidScalar;
            return false;
        }
        value = (int)raw;
        result = WorldFileRuntimeMetadataParseResult.Parsed;
        return true;
    }

    private static bool ReadBool(ref MetadataReader reader, out bool value)
    {
        if (!reader.TryReadByte(out byte raw))
        {
            value = default;
            return false;
        }
        value = raw != 0;
        return true;
    }

    private static WorldFileRuntimeMetadataParseResult Finish(
        WorldFileRuntimeMetadataParseResult result,
        ref MetadataReader reader,
        out int bytesConsumed)
    {
        bytesConsumed = reader.Offset;
        return result;
    }

    private ref struct MetadataReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        public MetadataReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Remaining => _data.Length - _offset;

        public bool TryReadByte(out byte value)
        {
            if (_offset >= _data.Length) { value = default; return false; }
            value = _data[_offset++];
            return true;
        }

        public bool TryReadInt16(out short value)
        {
            if (_data.Length - _offset < sizeof(short)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt16LittleEndian(_data[_offset..]);
            _offset += sizeof(short);
            return true;
        }

        public bool TryReadUInt16(out ushort value)
        {
            if (_data.Length - _offset < sizeof(ushort)) { value = default; return false; }
            value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_offset..]);
            _offset += sizeof(ushort);
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            if (_data.Length - _offset < sizeof(int)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
            _offset += sizeof(int);
            return true;
        }

        public bool TryReadInt64(out long value)
        {
            if (_data.Length - _offset < sizeof(long)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt64LittleEndian(_data[_offset..]);
            _offset += sizeof(long);
            return true;
        }

        public bool TryReadUInt64(out ulong value)
        {
            if (_data.Length - _offset < sizeof(ulong)) { value = default; return false; }
            value = BinaryPrimitives.ReadUInt64LittleEndian(_data[_offset..]);
            _offset += sizeof(ulong);
            return true;
        }

        public bool TryReadSingle(out float value)
        {
            if (_data.Length - _offset < sizeof(float)) { value = default; return false; }
            int bits = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
            _offset += sizeof(float);
            value = BitConverter.Int32BitsToSingle(bits);
            return true;
        }

        public bool TryReadDouble(out double value)
        {
            if (_data.Length - _offset < sizeof(double)) { value = default; return false; }
            long bits = BinaryPrimitives.ReadInt64LittleEndian(_data[_offset..]);
            _offset += sizeof(double);
            value = BitConverter.Int64BitsToDouble(bits);
            return true;
        }

        public bool TryReadGuid(out Guid value)
        {
            if (_data.Length - _offset < 16) { value = default; return false; }
            value = new Guid(_data.Slice(_offset, 16));
            _offset += 16;
            return true;
        }

        public WorldFileRuntimeMetadataParseResult TryReadString(int maxBytes, out string value, out int byteCount)
        {
            value = string.Empty;
            byteCount = 0;
            WorldFileRuntimeMetadataParseResult result = TryRead7BitEncodedInt(out int length);
            if (result != WorldFileRuntimeMetadataParseResult.Parsed)
                return result;
            if (length > maxBytes)
                return WorldFileRuntimeMetadataParseResult.StringTooLarge;
            if (_data.Length - _offset < length)
                return WorldFileRuntimeMetadataParseResult.Truncated;

            try
            {
                value = StrictUtf8.GetString(_data.Slice(_offset, length));
            }
            catch (DecoderFallbackException)
            {
                return WorldFileRuntimeMetadataParseResult.InvalidUtf8;
            }

            _offset += length;
            byteCount = length;
            return WorldFileRuntimeMetadataParseResult.Parsed;
        }

        private WorldFileRuntimeMetadataParseResult TryRead7BitEncodedInt(out int value)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (_offset >= _data.Length)
                {
                    value = default;
                    return WorldFileRuntimeMetadataParseResult.Truncated;
                }
                byte current = _data[_offset++];
                if (shift == 28 && (current & 0xF0) != 0)
                {
                    value = default;
                    return WorldFileRuntimeMetadataParseResult.InvalidStringLength;
                }
                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    if (result > int.MaxValue)
                    {
                        value = default;
                        return WorldFileRuntimeMetadataParseResult.InvalidStringLength;
                    }
                    value = (int)result;
                    return WorldFileRuntimeMetadataParseResult.Parsed;
                }
            }

            value = default;
            return WorldFileRuntimeMetadataParseResult.InvalidStringLength;
        }
    }
}
