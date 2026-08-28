using System.Text;

namespace TerraRuntime.World;

/// <summary>
/// Compact binary representation of the already validated non-tile runtime world state.
/// The enclosing runtime snapshot owns compatibility and integrity; this codec intentionally has no
/// migration/version contract. If its layout changes, the disposable snapshot is rebuilt from .wld.
/// </summary>
internal static class RuntimeWorldPreparedStateCodec
{
    public const int MaximumPayloadBytes = 128 * 1024 * 1024;

    public static byte[] Encode(WorldFileData world)
    {
        ArgumentNullException.ThrowIfNull(world);

        using var stream = new MemoryStream(capacity: 64 * 1024);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        WriteEnvelope(writer, world.Envelope);
        WriteHeader(writer, world.Header);
        WriteMetadata(writer, world.RuntimeMetadata);
        WriteChests(writer, world.Chests);
        WriteSigns(writer, world.Signs);
        WriteNpcs(writer, world.Npcs);
        WriteTileEntities(writer, world.TileEntities);
        WritePressurePlates(writer, world.PressurePlates);
        WriteTownRooms(writer, world.TownRooms);
        WriteBestiary(writer, world.Bestiary);
        WriteCreativePowers(writer, world.CreativePowers);
        writer.Flush();

        if (stream.Length > MaximumPayloadBytes)
            throw new InvalidDataException("Prepared runtime world payload exceeds the snapshot budget.");

        return stream.ToArray();
    }

    public static bool TryDecode(byte[] payload, WorldTileStore tiles, out WorldFileData? world)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(tiles);
        world = null;

        if (payload.Length == 0 || payload.Length > MaximumPayloadBytes)
            return false;

        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            WorldFileEnvelope envelope = ReadEnvelope(reader);
            WorldFileHeader header = ReadHeader(reader);
            WorldFileRuntimeMetadata metadata = ReadMetadata(reader);
            WorldChest[] chests = ReadChests(reader);
            WorldSign[] signs = ReadSigns(reader);
            WorldNpcPersistence npcs = ReadNpcs(reader);
            WorldTileEntity[] tileEntities = ReadTileEntities(reader);
            WorldPressurePlate[] pressurePlates = ReadPressurePlates(reader);
            WorldTownRoom[] townRooms = ReadTownRooms(reader);
            WorldBestiaryData bestiary = ReadBestiary(reader);
            WorldCreativePowersData creativePowers = ReadCreativePowers(reader);

            if (stream.Position != stream.Length ||
                header.Dimensions.WidthTiles != tiles.Dimensions.WidthTiles ||
                header.Dimensions.HeightTiles != tiles.Dimensions.HeightTiles)
            {
                return false;
            }

            world = new WorldFileData(
                envelope,
                header,
                metadata,
                tiles,
                chests,
                signs,
                npcs,
                tileEntities,
                pressurePlates,
                townRooms,
                bestiary,
                creativePowers);
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            world = null;
            return false;
        }
    }

    private static void WriteEnvelope(BinaryWriter writer, WorldFileEnvelope value)
    {
        writer.Write(value.FormatVersion);
        writer.Write(value.Revision);
        writer.Write(value.FavoriteFlags);
        writer.Write(value.SectionOffsets.Count);
        for (int i = 0; i < value.SectionOffsets.Count; i++)
            writer.Write(value.SectionOffsets[i]);
        writer.Write(value.FrameImportanceCount);
        WriteBytes(writer, value.FrameImportanceBits.Span);
    }

    private static WorldFileEnvelope ReadEnvelope(BinaryReader reader)
    {
        int formatVersion = reader.ReadInt32();
        uint revision = reader.ReadUInt32();
        ulong favoriteFlags = reader.ReadUInt64();
        int[] offsets = ReadInt32Array(reader, 256);
        int frameImportanceCount = reader.ReadInt32();
        byte[] frameImportanceBits = ReadBytes(reader, 1 * 1024 * 1024);
        if (frameImportanceCount < 0 || frameImportanceCount > frameImportanceBits.Length * 8)
            throw new InvalidDataException("Invalid frame-importance state.");
        return new WorldFileEnvelope(
            formatVersion,
            revision,
            favoriteFlags,
            offsets,
            frameImportanceCount,
            frameImportanceBits);
    }

    private static void WriteHeader(BinaryWriter writer, WorldFileHeader value)
    {
        writer.Write(value.Name);
        writer.Write(value.SeedText);
        writer.Write(value.WorldGeneratorVersion);
        writer.Write(value.UniqueId.ToByteArray());
        writer.Write(value.WorldId);
        writer.Write(value.LeftWorld);
        writer.Write(value.RightWorld);
        writer.Write(value.TopWorld);
        writer.Write(value.BottomWorld);
        writer.Write(value.Dimensions.WidthTiles);
        writer.Write(value.Dimensions.HeightTiles);
    }

    private static WorldFileHeader ReadHeader(BinaryReader reader)
    {
        string name = reader.ReadString();
        string seed = reader.ReadString();
        ulong generatorVersion = reader.ReadUInt64();
        byte[] guid = reader.ReadBytes(16);
        if (guid.Length != 16)
            throw new EndOfStreamException();
        int worldId = reader.ReadInt32();
        int left = reader.ReadInt32();
        int right = reader.ReadInt32();
        int top = reader.ReadInt32();
        int bottom = reader.ReadInt32();
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        return new WorldFileHeader(
            name,
            seed,
            generatorVersion,
            new Guid(guid),
            worldId,
            left,
            right,
            top,
            bottom,
            new WorldDimensions(width, height));
    }

    private static void WriteMetadata(BinaryWriter writer, WorldFileRuntimeMetadata m)
    {
        writer.Write(m.GameMode);
        WriteBools(writer,
            m.DrunkWorld, m.GetGoodWorld, m.TenthAnniversaryWorld, m.DontStarveWorld,
            m.NotTheBeesWorld, m.RemixWorld, m.NoTrapsWorld, m.ZenithWorld,
            m.SkyblockWorld, m.VampireSeed, m.InfectedSeed, m.TeamBasedSpawnsSeed,
            m.DualDungeonsSeed, m.MoreLightningSeed, m.NoLightningSeed);

        writer.Write(m.MoonType);
        WriteInt32Array(writer, m.TreeX);
        WriteBytes(writer, m.TreeStyles);
        WriteInt32Array(writer, m.CaveBackX);
        WriteBytes(writer, m.CaveBackStyles);
        writer.Write(m.IceBackStyle);
        writer.Write(m.JungleBackStyle);
        writer.Write(m.HellBackStyle);

        writer.Write(m.SpawnX);
        writer.Write(m.SpawnY);
        writer.Write(m.WorldSurface);
        writer.Write(m.RockLayer);
        writer.Write(m.Time);
        writer.Write(m.DayTime);
        writer.Write(m.MoonPhase);
        writer.Write(m.BloodMoon);
        writer.Write(m.Eclipse);
        writer.Write(m.DungeonX);
        writer.Write(m.DungeonY);

        WriteBools(writer,
            m.Crimson, m.DownedBoss1, m.DownedBoss2, m.DownedBoss3, m.DownedQueenBee,
            m.DownedMechBoss1, m.DownedMechBoss2, m.DownedMechBoss3, m.DownedMechBossAny,
            m.DownedPlantBoss, m.DownedGolemBoss, m.DownedSlimeKing, m.DownedGoblins,
            m.DownedClown, m.DownedFrost, m.DownedPirates, m.ShadowOrbSmashed, m.HardMode);

        writer.Write(m.SlimeRainTime);
        writer.Write(m.SundialCooldown);
        writer.Write(m.Raining);
        writer.Write(m.MaxRain);
        writer.Write(m.OreTiers.Copper);
        writer.Write(m.OreTiers.Iron);
        writer.Write(m.OreTiers.Silver);
        writer.Write(m.OreTiers.Gold);
        writer.Write(m.OreTiers.Cobalt);
        writer.Write(m.OreTiers.Mythril);
        writer.Write(m.OreTiers.Adamantite);

        writer.Write(m.TreeBackground);
        writer.Write(m.TreeBackground2);
        writer.Write(m.TreeBackground3);
        writer.Write(m.TreeBackground4);
        writer.Write(m.CorruptionBackground);
        writer.Write(m.JungleBackground);
        writer.Write(m.SnowBackground);
        writer.Write(m.HallowBackground);
        writer.Write(m.CrimsonBackground);
        writer.Write(m.DesertBackground);
        writer.Write(m.OceanBackground);
        writer.Write(m.MushroomBackground);
        writer.Write(m.UnderworldBackground);
        writer.Write(m.CloudBackgroundActive);
        writer.Write(m.CloudCount);
        writer.Write(m.WindSpeed);

        WriteBools(writer,
            m.FastForwardTimeToDawn, m.DownedFishron, m.DownedMartians, m.DownedAncientCultist,
            m.DownedMoonlord, m.DownedHalloweenKing, m.DownedHalloweenTree,
            m.DownedChristmasIceQueen, m.DownedChristmasSantank, m.DownedChristmasTree,
            m.DownedTowerSolar, m.DownedTowerVortex, m.DownedTowerNebula, m.DownedTowerStardust,
            m.PartyManual, m.PartyGenuine, m.SandstormHappening);
        writer.Write(m.SandstormIntendedSeverity);
        WriteBools(writer, m.DownedDd2InvasionT1, m.DownedDd2InvasionT2, m.DownedDd2InvasionT3,
            m.CombatBookWasUsed, m.LanternNightGenuine, m.LanternNightManual);
        WriteBytes(writer, m.TreeTopVariations);
        WriteBools(writer, m.ForceHalloweenForToday, m.ForceXMasForToday,
            m.BoughtCat, m.BoughtDog, m.BoughtBunny, m.DownedEmpressOfLight,
            m.DownedQueenSlime, m.DownedDeerclops, m.UnlockedSlimeBlueSpawn,
            m.UnlockedTruffleSpawn, m.CombatBookVolumeTwoWasUsed, m.PeddlersSatchelWasUsed,
            m.UnlockedSlimeGreenSpawn, m.UnlockedSlimeOldSpawn, m.UnlockedSlimePurpleSpawn,
            m.UnlockedSlimeRainbowSpawn, m.UnlockedSlimeRedSpawn, m.UnlockedSlimeYellowSpawn,
            m.UnlockedSlimeCopperSpawn, m.FastForwardTimeToDusk);
        writer.Write(m.MoondialCooldown);
        WriteBools(writer, m.ForceHalloweenForever, m.ForceXMasForever);
        writer.Write(m.InvasionType);
        writer.Write(m.ExtraSpawnPoints.Length);
        foreach (WorldSpawnPoint spawn in m.ExtraSpawnPoints)
        {
            writer.Write(spawn.X);
            writer.Write(spawn.Y);
        }
    }

    private static WorldFileRuntimeMetadata ReadMetadata(BinaryReader reader)
    {
        byte gameMode = reader.ReadByte();
        bool[] seedFlags = ReadBools(reader, 15);
        byte moonType = reader.ReadByte();
        int[] treeX = ReadInt32Array(reader, 3, exact: true);
        byte[] treeStyles = ReadBytes(reader, 4, exact: true);
        int[] caveBackX = ReadInt32Array(reader, 3, exact: true);
        byte[] caveBackStyles = ReadBytes(reader, 4, exact: true);
        byte iceBackStyle = reader.ReadByte();
        byte jungleBackStyle = reader.ReadByte();
        byte hellBackStyle = reader.ReadByte();

        short spawnX = reader.ReadInt16();
        short spawnY = reader.ReadInt16();
        short worldSurface = reader.ReadInt16();
        short rockLayer = reader.ReadInt16();
        int time = reader.ReadInt32();
        bool dayTime = reader.ReadBoolean();
        byte moonPhase = reader.ReadByte();
        bool bloodMoon = reader.ReadBoolean();
        bool eclipse = reader.ReadBoolean();
        short dungeonX = reader.ReadInt16();
        short dungeonY = reader.ReadInt16();

        bool[] progression = ReadBools(reader, 18);
        double slimeRainTime = reader.ReadDouble();
        byte sundialCooldown = reader.ReadByte();
        bool raining = reader.ReadBoolean();
        float maxRain = reader.ReadSingle();
        var oreTiers = new WorldOreTiers(
            reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(),
            reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());

        byte treeBackground = reader.ReadByte();
        byte treeBackground2 = reader.ReadByte();
        byte treeBackground3 = reader.ReadByte();
        byte treeBackground4 = reader.ReadByte();
        byte corruptionBackground = reader.ReadByte();
        byte jungleBackground = reader.ReadByte();
        byte snowBackground = reader.ReadByte();
        byte hallowBackground = reader.ReadByte();
        byte crimsonBackground = reader.ReadByte();
        byte desertBackground = reader.ReadByte();
        byte oceanBackground = reader.ReadByte();
        byte mushroomBackground = reader.ReadByte();
        byte underworldBackground = reader.ReadByte();
        bool cloudBackgroundActive = reader.ReadBoolean();
        byte cloudCount = reader.ReadByte();
        float windSpeed = reader.ReadSingle();

        bool[] eventFlags = ReadBools(reader, 17);
        float sandstormIntendedSeverity = reader.ReadSingle();
        bool[] dd2AndLantern = ReadBools(reader, 6);
        byte[] treeTopVariations = ReadBytes(reader, 13, exact: true);
        bool[] lateFlags = ReadBools(reader, 20);
        byte moondialCooldown = reader.ReadByte();
        bool[] foreverFlags = ReadBools(reader, 2);
        sbyte invasionType = reader.ReadSByte();
        int spawnCount = ReadCount(reader, 4096);
        var extraSpawnPoints = new WorldSpawnPoint[spawnCount];
        for (int i = 0; i < spawnCount; i++)
            extraSpawnPoints[i] = new WorldSpawnPoint(reader.ReadInt16(), reader.ReadInt16());

        return new WorldFileRuntimeMetadata
        {
            GameMode = gameMode,
            DrunkWorld = seedFlags[0],
            GetGoodWorld = seedFlags[1],
            TenthAnniversaryWorld = seedFlags[2],
            DontStarveWorld = seedFlags[3],
            NotTheBeesWorld = seedFlags[4],
            RemixWorld = seedFlags[5],
            NoTrapsWorld = seedFlags[6],
            ZenithWorld = seedFlags[7],
            SkyblockWorld = seedFlags[8],
            VampireSeed = seedFlags[9],
            InfectedSeed = seedFlags[10],
            TeamBasedSpawnsSeed = seedFlags[11],
            DualDungeonsSeed = seedFlags[12],
            MoreLightningSeed = seedFlags[13],
            NoLightningSeed = seedFlags[14],
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
            Crimson = progression[0],
            DownedBoss1 = progression[1],
            DownedBoss2 = progression[2],
            DownedBoss3 = progression[3],
            DownedQueenBee = progression[4],
            DownedMechBoss1 = progression[5],
            DownedMechBoss2 = progression[6],
            DownedMechBoss3 = progression[7],
            DownedMechBossAny = progression[8],
            DownedPlantBoss = progression[9],
            DownedGolemBoss = progression[10],
            DownedSlimeKing = progression[11],
            DownedGoblins = progression[12],
            DownedClown = progression[13],
            DownedFrost = progression[14],
            DownedPirates = progression[15],
            ShadowOrbSmashed = progression[16],
            HardMode = progression[17],
            SlimeRainTime = slimeRainTime,
            SundialCooldown = sundialCooldown,
            Raining = raining,
            MaxRain = maxRain,
            OreTiers = oreTiers,
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
            CloudBackgroundActive = cloudBackgroundActive,
            CloudCount = cloudCount,
            WindSpeed = windSpeed,
            FastForwardTimeToDawn = eventFlags[0],
            DownedFishron = eventFlags[1],
            DownedMartians = eventFlags[2],
            DownedAncientCultist = eventFlags[3],
            DownedMoonlord = eventFlags[4],
            DownedHalloweenKing = eventFlags[5],
            DownedHalloweenTree = eventFlags[6],
            DownedChristmasIceQueen = eventFlags[7],
            DownedChristmasSantank = eventFlags[8],
            DownedChristmasTree = eventFlags[9],
            DownedTowerSolar = eventFlags[10],
            DownedTowerVortex = eventFlags[11],
            DownedTowerNebula = eventFlags[12],
            DownedTowerStardust = eventFlags[13],
            PartyManual = eventFlags[14],
            PartyGenuine = eventFlags[15],
            SandstormHappening = eventFlags[16],
            SandstormIntendedSeverity = sandstormIntendedSeverity,
            DownedDd2InvasionT1 = dd2AndLantern[0],
            DownedDd2InvasionT2 = dd2AndLantern[1],
            DownedDd2InvasionT3 = dd2AndLantern[2],
            CombatBookWasUsed = dd2AndLantern[3],
            LanternNightGenuine = dd2AndLantern[4],
            LanternNightManual = dd2AndLantern[5],
            TreeTopVariations = treeTopVariations,
            ForceHalloweenForToday = lateFlags[0],
            ForceXMasForToday = lateFlags[1],
            BoughtCat = lateFlags[2],
            BoughtDog = lateFlags[3],
            BoughtBunny = lateFlags[4],
            DownedEmpressOfLight = lateFlags[5],
            DownedQueenSlime = lateFlags[6],
            DownedDeerclops = lateFlags[7],
            UnlockedSlimeBlueSpawn = lateFlags[8],
            UnlockedTruffleSpawn = lateFlags[9],
            CombatBookVolumeTwoWasUsed = lateFlags[10],
            PeddlersSatchelWasUsed = lateFlags[11],
            UnlockedSlimeGreenSpawn = lateFlags[12],
            UnlockedSlimeOldSpawn = lateFlags[13],
            UnlockedSlimePurpleSpawn = lateFlags[14],
            UnlockedSlimeRainbowSpawn = lateFlags[15],
            UnlockedSlimeRedSpawn = lateFlags[16],
            UnlockedSlimeYellowSpawn = lateFlags[17],
            UnlockedSlimeCopperSpawn = lateFlags[18],
            FastForwardTimeToDusk = lateFlags[19],
            MoondialCooldown = moondialCooldown,
            ForceHalloweenForever = foreverFlags[0],
            ForceXMasForever = foreverFlags[1],
            InvasionType = invasionType,
            ExtraSpawnPoints = extraSpawnPoints
        };
    }

    private static void WriteChests(BinaryWriter writer, WorldChest[] values)
    {
        writer.Write(values.Length);
        foreach (WorldChest chest in values)
        {
            writer.Write(chest.SlotId);
            writer.Write(chest.X);
            writer.Write(chest.Y);
            writer.Write(chest.Name);
            writer.Write(chest.Items.Length);
            foreach (WorldChestItem item in chest.Items)
            {
                writer.Write(item.Stack);
                writer.Write(item.ItemType);
                writer.Write(item.Prefix);
            }
        }
    }

    private static WorldChest[] ReadChests(BinaryReader reader)
    {
        int count = ReadCount(reader, 1_000_000);
        var values = new WorldChest[count];
        for (int i = 0; i < count; i++)
        {
            short slotId = reader.ReadInt16();
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            string name = reader.ReadString();
            int itemCount = ReadCount(reader, 4096);
            var items = new WorldChestItem[itemCount];
            for (int j = 0; j < itemCount; j++)
                items[j] = new WorldChestItem(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte());
            values[i] = new WorldChest(slotId, x, y, name, items);
        }
        return values;
    }

    private static void WriteSigns(BinaryWriter writer, WorldSign[] values)
    {
        writer.Write(values.Length);
        foreach (WorldSign sign in values)
        {
            writer.Write(sign.SlotId);
            writer.Write(sign.Text);
            writer.Write(sign.X);
            writer.Write(sign.Y);
        }
    }

    private static WorldSign[] ReadSigns(BinaryReader reader)
    {
        int count = ReadCount(reader, 1_000_000);
        var values = new WorldSign[count];
        for (int i = 0; i < count; i++)
            values[i] = new WorldSign(reader.ReadInt16(), reader.ReadString(), reader.ReadInt32(), reader.ReadInt32());
        return values;
    }

    private static void WriteNpcs(BinaryWriter writer, WorldNpcPersistence value)
    {
        WriteInt32Array(writer, value.ShimmeredTownNpcIndices);
        writer.Write(value.TownNpcs.Length);
        foreach (WorldTownNpc npc in value.TownNpcs)
        {
            writer.Write(npc.NetId);
            writer.Write(npc.GivenName);
            writer.Write(npc.X);
            writer.Write(npc.Y);
            writer.Write(npc.Homeless);
            writer.Write(npc.HomeTileX);
            writer.Write(npc.HomeTileY);
            writer.Write(npc.TownNpcVariationIndex.HasValue);
            if (npc.TownNpcVariationIndex.HasValue)
                writer.Write(npc.TownNpcVariationIndex.Value);
            writer.Write(npc.HomelessDespawn);
        }

        writer.Write(value.PersistentNpcs.Length);
        foreach (WorldPersistentNpc npc in value.PersistentNpcs)
        {
            writer.Write(npc.NetId);
            writer.Write(npc.X);
            writer.Write(npc.Y);
        }
    }

    private static WorldNpcPersistence ReadNpcs(BinaryReader reader)
    {
        int[] shimmered = ReadInt32Array(reader, 1_000_000);
        int townCount = ReadCount(reader, 1_000_000);
        var town = new WorldTownNpc[townCount];
        for (int i = 0; i < townCount; i++)
        {
            int netId = reader.ReadInt32();
            string name = reader.ReadString();
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            bool homeless = reader.ReadBoolean();
            int homeX = reader.ReadInt32();
            int homeY = reader.ReadInt32();
            int? variation = reader.ReadBoolean() ? reader.ReadInt32() : null;
            bool homelessDespawn = reader.ReadBoolean();
            town[i] = new WorldTownNpc(netId, name, x, y, homeless, homeX, homeY, variation, homelessDespawn);
        }

        int persistentCount = ReadCount(reader, 1_000_000);
        var persistent = new WorldPersistentNpc[persistentCount];
        for (int i = 0; i < persistentCount; i++)
            persistent[i] = new WorldPersistentNpc(reader.ReadInt32(), reader.ReadSingle(), reader.ReadSingle());
        return new WorldNpcPersistence(shimmered, town, persistent);
    }

    private static void WriteTileEntities(BinaryWriter writer, WorldTileEntity[] values)
    {
        writer.Write(values.Length);
        foreach (WorldTileEntity entity in values)
        {
            writer.Write(entity.PersistedId);
            writer.Write(entity.X);
            writer.Write(entity.Y);
            writer.Write((byte)entity.Kind);
            WriteTileEntityPayload(writer, entity.Kind, entity.Payload);
        }
    }

    private static WorldTileEntity[] ReadTileEntities(BinaryReader reader)
    {
        int count = ReadCount(reader, 1_000_000);
        var values = new WorldTileEntity[count];
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            short x = reader.ReadInt16();
            short y = reader.ReadInt16();
            var kind = (WorldTileEntityKind)reader.ReadByte();
            values[i] = new WorldTileEntity(id, x, y, kind, ReadTileEntityPayload(reader, kind));
        }
        return values;
    }

    private static void WriteTileEntityPayload(BinaryWriter writer, WorldTileEntityKind kind, WorldTileEntityPayload payload)
    {
        switch (kind)
        {
            case WorldTileEntityKind.TrainingDummy:
                writer.Write(((WorldTrainingDummyPayload)payload).NpcIndex);
                return;
            case WorldTileEntityKind.ItemFrame:
            case WorldTileEntityKind.WeaponsRack:
            case WorldTileEntityKind.FoodPlatter:
            case WorldTileEntityKind.DeadCellsDisplayJar:
                WriteTileEntityItem(writer, ((WorldItemTileEntityPayload)payload).Item);
                return;
            case WorldTileEntityKind.LogicSensor:
                WorldLogicSensorPayload sensor = (WorldLogicSensorPayload)payload;
                writer.Write(sensor.LogicCheck);
                writer.Write(sensor.IsOn);
                return;
            case WorldTileEntityKind.DisplayDoll:
                WorldDisplayDollPayload doll = (WorldDisplayDollPayload)payload;
                writer.Write(doll.Pose);
                WriteNullableItems(writer, doll.Equipment);
                WriteNullableItems(writer, doll.Dyes);
                writer.Write(doll.Misc.HasValue);
                if (doll.Misc.HasValue)
                    WriteTileEntityItem(writer, doll.Misc.Value);
                return;
            case WorldTileEntityKind.HatRack:
                WorldHatRackPayload rack = (WorldHatRackPayload)payload;
                WriteNullableItems(writer, rack.Items);
                WriteNullableItems(writer, rack.Dyes);
                return;
            case WorldTileEntityKind.TeleportationPylon:
                return;
            case WorldTileEntityKind.KiteAnchor:
            case WorldTileEntityKind.CritterAnchor:
                writer.Write(((WorldLeashedAnchorPayload)payload).ItemType);
                return;
            default:
                throw new InvalidDataException("Unknown tile entity kind.");
        }
    }

    private static WorldTileEntityPayload ReadTileEntityPayload(BinaryReader reader, WorldTileEntityKind kind) =>
        kind switch
        {
            WorldTileEntityKind.TrainingDummy => new WorldTrainingDummyPayload(reader.ReadInt16()),
            WorldTileEntityKind.ItemFrame or WorldTileEntityKind.WeaponsRack or WorldTileEntityKind.FoodPlatter or
                WorldTileEntityKind.DeadCellsDisplayJar => new WorldItemTileEntityPayload(ReadTileEntityItem(reader)),
            WorldTileEntityKind.LogicSensor => new WorldLogicSensorPayload(reader.ReadByte(), reader.ReadBoolean()),
            WorldTileEntityKind.DisplayDoll => ReadDisplayDoll(reader),
            WorldTileEntityKind.HatRack => new WorldHatRackPayload(ReadNullableItems(reader, 32), ReadNullableItems(reader, 32)),
            WorldTileEntityKind.TeleportationPylon => WorldEmptyTileEntityPayload.Instance,
            WorldTileEntityKind.KiteAnchor or WorldTileEntityKind.CritterAnchor => new WorldLeashedAnchorPayload(reader.ReadInt16()),
            _ => throw new InvalidDataException("Unknown tile entity kind.")
        };

    private static WorldDisplayDollPayload ReadDisplayDoll(BinaryReader reader)
    {
        byte pose = reader.ReadByte();
        WorldTileEntityItem?[] equipment = ReadNullableItems(reader, 32);
        WorldTileEntityItem?[] dyes = ReadNullableItems(reader, 32);
        WorldTileEntityItem? misc = reader.ReadBoolean() ? ReadTileEntityItem(reader) : null;
        return new WorldDisplayDollPayload(pose, equipment, dyes, misc);
    }

    private static void WriteTileEntityItem(BinaryWriter writer, WorldTileEntityItem item)
    {
        writer.Write(item.Type);
        writer.Write(item.Prefix);
        writer.Write(item.Stack);
    }

    private static WorldTileEntityItem ReadTileEntityItem(BinaryReader reader) =>
        new(reader.ReadInt16(), reader.ReadByte(), reader.ReadInt16());

    private static void WriteNullableItems(BinaryWriter writer, WorldTileEntityItem?[] items)
    {
        writer.Write(items.Length);
        foreach (WorldTileEntityItem? item in items)
        {
            writer.Write(item.HasValue);
            if (item.HasValue)
                WriteTileEntityItem(writer, item.Value);
        }
    }

    private static WorldTileEntityItem?[] ReadNullableItems(BinaryReader reader, int maximum)
    {
        int count = ReadCount(reader, maximum);
        var items = new WorldTileEntityItem?[count];
        for (int i = 0; i < count; i++)
            items[i] = reader.ReadBoolean() ? ReadTileEntityItem(reader) : null;
        return items;
    }

    private static void WritePressurePlates(BinaryWriter writer, WorldPressurePlate[] values)
    {
        writer.Write(values.Length);
        foreach (WorldPressurePlate value in values)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
        }
    }

    private static WorldPressurePlate[] ReadPressurePlates(BinaryReader reader)
    {
        int count = ReadCount(reader, 1_000_000);
        var values = new WorldPressurePlate[count];
        for (int i = 0; i < count; i++)
            values[i] = new WorldPressurePlate(reader.ReadInt32(), reader.ReadInt32());
        return values;
    }

    private static void WriteTownRooms(BinaryWriter writer, WorldTownRoom[] values)
    {
        writer.Write(values.Length);
        foreach (WorldTownRoom value in values)
        {
            writer.Write(value.NpcType);
            writer.Write(value.X);
            writer.Write(value.Y);
        }
    }

    private static WorldTownRoom[] ReadTownRooms(BinaryReader reader)
    {
        int count = ReadCount(reader, 1_000_000);
        var values = new WorldTownRoom[count];
        for (int i = 0; i < count; i++)
            values[i] = new WorldTownRoom(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        return values;
    }

    private static void WriteBestiary(BinaryWriter writer, WorldBestiaryData value)
    {
        writer.Write(value.Kills.Length);
        foreach (WorldBestiaryKill kill in value.Kills)
        {
            writer.Write(kill.PersistentId);
            writer.Write(kill.KillCount);
        }
        WriteStrings(writer, value.Sightings);
        WriteStrings(writer, value.Chats);
    }

    private static WorldBestiaryData ReadBestiary(BinaryReader reader)
    {
        int killCount = ReadCount(reader, 1_000_000);
        var kills = new WorldBestiaryKill[killCount];
        for (int i = 0; i < killCount; i++)
            kills[i] = new WorldBestiaryKill(reader.ReadString(), reader.ReadInt32());
        return new WorldBestiaryData(kills, ReadStrings(reader, 1_000_000), ReadStrings(reader, 1_000_000));
    }

    private static void WriteCreativePowers(BinaryWriter writer, WorldCreativePowersData value)
    {
        writer.Write(value.FreezeTime);
        writer.Write(value.TimeRateSlider);
        writer.Write(value.FreezeRain);
        writer.Write(value.FreezeWind);
        writer.Write(value.DifficultySlider);
        writer.Write(value.StopBiomeSpread);
    }

    private static WorldCreativePowersData ReadCreativePowers(BinaryReader reader) =>
        new(reader.ReadBoolean(), reader.ReadSingle(), reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadSingle(), reader.ReadBoolean());

    private static void WriteStrings(BinaryWriter writer, string[] values)
    {
        writer.Write(values.Length);
        foreach (string value in values)
            writer.Write(value);
    }

    private static string[] ReadStrings(BinaryReader reader, int maximum)
    {
        int count = ReadCount(reader, maximum);
        var values = new string[count];
        for (int i = 0; i < count; i++)
            values[i] = reader.ReadString();
        return values;
    }

    private static void WriteInt32Array(BinaryWriter writer, IReadOnlyList<int> values)
    {
        writer.Write(values.Count);
        for (int i = 0; i < values.Count; i++)
            writer.Write(values[i]);
    }

    private static int[] ReadInt32Array(BinaryReader reader, int maximum, bool exact = false)
    {
        int count = ReadCount(reader, maximum);
        if (exact && count != maximum)
            throw new InvalidDataException("Prepared array has an invalid fixed length.");
        var values = new int[count];
        for (int i = 0; i < count; i++)
            values[i] = reader.ReadInt32();
        return values;
    }

    private static void WriteBytes(BinaryWriter writer, ReadOnlySpan<byte> values)
    {
        writer.Write(values.Length);
        writer.Write(values);
    }

    private static byte[] ReadBytes(BinaryReader reader, int maximum, bool exact = false)
    {
        int count = ReadCount(reader, maximum);
        if (exact && count != maximum)
            throw new InvalidDataException("Prepared byte array has an invalid fixed length.");
        byte[] values = reader.ReadBytes(count);
        if (values.Length != count)
            throw new EndOfStreamException();
        return values;
    }

    private static void WriteBools(BinaryWriter writer, params bool[] values)
    {
        foreach (bool value in values)
            writer.Write(value);
    }

    private static bool[] ReadBools(BinaryReader reader, int count)
    {
        var values = new bool[count];
        for (int i = 0; i < count; i++)
            values[i] = reader.ReadBoolean();
        return values;
    }

    private static int ReadCount(BinaryReader reader, int maximum)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > maximum)
            throw new InvalidDataException("Prepared collection count is invalid.");
        return count;
    }
}
