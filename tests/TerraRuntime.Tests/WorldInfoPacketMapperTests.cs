using Multiplicity.Packets;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldInfoPacketMapperTests
{
    [Fact]
    public void Maps_saved_and_transient_world_state_to_verified_protocol_326_bits()
    {
        Guid uniqueId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var header = new WorldFileHeader(
            "mapper-world",
            "seed",
            123UL,
            uniqueId,
            77,
            0,
            134_400,
            0,
            38_400,
            new WorldDimensions(8_400, 2_400));
        var state = new WorldFileRuntimeMetadata
        {
            GameMode = 2,
            DrunkWorld = true,
            GetGoodWorld = true,
            TenthAnniversaryWorld = true,
            RemixWorld = true,
            NoTrapsWorld = true,
            VampireSeed = true,
            TeamBasedSpawnsSeed = true,
            DualDungeonsSeed = true,
            ForceXMasForever = true,
            MoreLightningSeed = true,
            NoLightningSeed = true,
            MoonType = 3,
            TreeX = [100, 200, 300],
            TreeStyles = [1, 2, 3, 4],
            CaveBackX = [400, 500, 600],
            CaveBackStyles = [5, 6, 7, 8],
            TreeTopVariations = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
            SpawnX = 42,
            SpawnY = 84,
            WorldSurface = 300,
            RockLayer = 500,
            Time = 13_500,
            DayTime = true,
            BloodMoon = true,
            MoonPhase = 4,
            DungeonX = 123,
            DungeonY = 456,
            ShadowOrbSmashed = true,
            DownedBoss2 = true,
            HardMode = true,
            DownedPlantBoss = true,
            DownedMechBoss1 = true,
            DownedMechBoss3 = true,
            CloudBackgroundActive = true,
            Crimson = true,
            FastForwardTimeToDawn = true,
            SlimeRainTime = 1d,
            DownedQueenBee = true,
            DownedAncientCultist = true,
            DownedMoonlord = true,
            DownedChristmasTree = true,
            DownedGolemBoss = true,
            PartyManual = true,
            DownedPirates = true,
            DownedGoblins = true,
            SandstormHappening = true,
            DownedDd2InvasionT2 = true,
            CombatBookWasUsed = true,
            LanternNightGenuine = true,
            DownedTowerVortex = true,
            DownedTowerStardust = true,
            ForceHalloweenForToday = true,
            BoughtDog = true,
            DownedEmpressOfLight = true,
            DownedDeerclops = true,
            UnlockedSlimeBlueSpawn = true,
            PeddlersSatchelWasUsed = true,
            UnlockedSlimeOldSpawn = true,
            UnlockedSlimeRainbowSpawn = true,
            UnlockedSlimeYellowSpawn = true,
            FastForwardTimeToDusk = true,
            UnlockedTruffleSpawn = true,
            Raining = true,
            MaxRain = 0.4f,
            WindSpeed = -0.15f,
            CloudCount = 7,
            SandstormIntendedSeverity = 0.75f,
            SundialCooldown = 2,
            MoondialCooldown = 3,
            OreTiers = new WorldOreTiers(1, 2, 3, 4, 5, 6, 7),
            InvasionType = 4,
            ExtraSpawnPoints = [new WorldSpawnPoint(10, 20), new WorldSpawnPoint(-30, 40)]
        };
        var transient = new WorldInfoTransientState(
            PumpkinMoon: false,
            SnowMoon: true,
            Dd2EventOngoing: true,
            FreeCake: true,
            SkyblockLowTiles: true,
            LobbyId: 0x0102030405060708UL);

        WorldInfo packet = WorldInfoPacketMapper.Create(header, state, transient);

        Assert.Equal((byte)0x03, packet.DayandMoonInfo);
        Assert.Equal((byte)0x95, packet.EventInfo);
        Assert.Equal((byte)0xB5, packet.EventInfo2);
        Assert.Equal((byte)0x96, packet.EventInfo3);
        Assert.Equal((byte)0xE1, packet.EventInfo4);
        Assert.Equal((byte)0x5D, packet.EventInfo5);
        Assert.Equal((byte)0x6B, packet.EventInfo6);
        Assert.Equal((byte)0xBA, packet.EventInfo7);
        Assert.Equal((byte)0xB5, packet.EventInfo8);
        Assert.Equal((byte)0xAA, packet.EventInfo9);
        Assert.Equal((byte)0xAD, packet.EventInfo10);
        Assert.Equal((byte)0x1D, packet.EventInfo11);
        Assert.Equal((short)8_400, packet.MaxTilesX);
        Assert.Equal((short)2_400, packet.MaxTilesY);
        Assert.Equal(uniqueId.ToByteArray(), packet.WorldUniqueId);
        Assert.Equal(0.4f, packet.Rain);
        Assert.Equal(0.75f, packet.SandstormSeverity);
        Assert.Equal(2, packet.ExtraSpawnPoints.Length);
        Assert.Equal((short)123, packet.DungeonX);
        Assert.Equal((short)456, packet.DungeonY);

        using var stream = new MemoryStream();
        packet.ToStream(stream, includeHeader: false);
        byte[] payload = stream.ToArray();
        WorldInfo parsed = Assert.IsType<WorldInfo>(
            TerrariaPacket.DeserializePayload(PacketTypes.WorldInfo, payload));

        Assert.Equal(packet.EventInfo11, parsed.EventInfo11);
        Assert.Equal(packet.LobbyId, parsed.LobbyId);
        Assert.Equal(packet.DungeonX, parsed.DungeonX);
        Assert.Equal(packet.DungeonY, parsed.DungeonY);
        Assert.Equal(packet.ExtraSpawnPoints.Length, parsed.ExtraSpawnPoints.Length);
        Assert.Empty(parsed.TrailingDataMemory.ToArray());
    }

    [Fact]
    public void Clears_network_rain_when_saved_raining_flag_is_false()
    {
        var header = new WorldFileHeader(
            "dry",
            "seed",
            1UL,
            Guid.Empty,
            1,
            0,
            1_600,
            0,
            1_600,
            new WorldDimensions(100, 100));
        var state = new WorldFileRuntimeMetadata
        {
            TreeX = new int[3],
            TreeStyles = new byte[4],
            CaveBackX = new int[3],
            CaveBackStyles = new byte[4],
            TreeTopVariations = new byte[13],
            Raining = false,
            MaxRain = 0.9f
        };

        WorldInfo packet = WorldInfoPacketMapper.Create(header, state);

        Assert.Equal(0f, packet.Rain);
    }
}
