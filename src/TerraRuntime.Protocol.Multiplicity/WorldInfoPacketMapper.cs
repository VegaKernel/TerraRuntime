using Multiplicity.Packets;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Live values used by Terraria's WorldInfo packet that are not authoritative persistence fields.
/// </summary>
public readonly record struct WorldInfoTransientState(
    bool PumpkinMoon,
    bool SnowMoon,
    bool Dd2EventOngoing,
    bool FreeCake,
    bool SkyblockLowTiles,
    ulong LobbyId);

/// <summary>
/// Maps a fully validated Terraria 1.4.5.8 world plus live runtime flags to protocol 326 packet 7.
/// Bit assignments are kept here at the Multiplicity boundary rather than leaking wire layout into the world model.
/// </summary>
public static class WorldInfoPacketMapper
{
    public static WorldInfo Create(WorldFileData world, WorldInfoTransientState transient = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        WorldFileHeader header = world.Header;
        WorldFileRuntimeMetadata state = world.RuntimeMetadata;

        if (state.TreeX.Length != 3 || state.TreeStyles.Length != 4 ||
            state.CaveBackX.Length != 3 || state.CaveBackStyles.Length != 4 ||
            state.TreeTopVariations.Length != 13)
        {
            throw new InvalidOperationException("Validated world runtime metadata has an invalid fixed-array shape.");
        }

        var extraSpawns = new Multiplicity.Packets.WorldSpawnPoint[state.ExtraSpawnPoints.Length];
        for (int i = 0; i < extraSpawns.Length; i++)
        {
            World.WorldSpawnPoint source = state.ExtraSpawnPoints[i];
            extraSpawns[i] = new Multiplicity.Packets.WorldSpawnPoint(source.X, source.Y);
        }

        return new WorldInfo
        {
            Time = state.Time,
            DayandMoonInfo = Pack(
                (0, state.DayTime),
                (1, state.BloodMoon),
                (2, state.Eclipse)),
            MoonPhase = state.MoonPhase,
            MaxTilesX = checked((short)header.Dimensions.WidthTiles),
            MaxTilesY = checked((short)header.Dimensions.HeightTiles),
            SpawnX = state.SpawnX,
            SpawnY = state.SpawnY,
            WorldSurface = state.WorldSurface,
            RockLayer = state.RockLayer,
            WorldId = header.WorldId,
            WorldName = header.Name,
            GameMode = state.GameMode,
            WorldUniqueId = header.UniqueId.ToByteArray(),
            WorldGeneratorVersion = header.WorldGeneratorVersion,
            MoonType = state.MoonType,
            TreeBackground = state.TreeBackground,
            TreeBackground2 = state.TreeBackground2,
            TreeBackground3 = state.TreeBackground3,
            TreeBackground4 = state.TreeBackground4,
            CorruptionBackground = state.CorruptionBackground,
            JungleBackground = state.JungleBackground,
            SnowBackground = state.SnowBackground,
            HallowBackground = state.HallowBackground,
            CrimsonBackground = state.CrimsonBackground,
            DesertBackground = state.DesertBackground,
            OceanBackground = state.OceanBackground,
            MushroomBackground = state.MushroomBackground,
            UnderworldBackground = state.UnderworldBackground,
            IceBackStyle = state.IceBackStyle,
            JungleBackStyle = state.JungleBackStyle,
            HellBackStyle = state.HellBackStyle,
            WindSpeedSet = state.WindSpeed,
            CloudNumber = state.CloudCount,
            Tree1 = state.TreeX[0],
            Tree2 = state.TreeX[1],
            Tree3 = state.TreeX[2],
            TreeStyle1 = state.TreeStyles[0],
            TreeStyle2 = state.TreeStyles[1],
            TreeStyle3 = state.TreeStyles[2],
            TreeStyle4 = state.TreeStyles[3],
            CaveBack1 = state.CaveBackX[0],
            CaveBack2 = state.CaveBackX[1],
            CaveBack3 = state.CaveBackX[2],
            CaveBackStyle1 = state.CaveBackStyles[0],
            CaveBackStyle2 = state.CaveBackStyles[1],
            CaveBackStyle3 = state.CaveBackStyles[2],
            CaveBackStyle4 = state.CaveBackStyles[3],
            TreeTopVariations = (byte[])state.TreeTopVariations.Clone(),
            Rain = state.NetworkRain,
            EventInfo = Pack(
                (0, state.ShadowOrbSmashed),
                (1, state.DownedBoss1),
                (2, state.DownedBoss2),
                (3, state.DownedBoss3),
                (4, state.HardMode),
                (5, state.DownedClown),
                (7, state.DownedPlantBoss)),
            EventInfo2 = Pack(
                (0, state.DownedMechBoss1),
                (1, state.DownedMechBoss2),
                (2, state.DownedMechBoss3),
                (3, state.DownedMechBossAny),
                (4, state.CloudBackgroundActive),
                (5, state.Crimson),
                (6, transient.PumpkinMoon),
                (7, transient.SnowMoon)),
            EventInfo3 = Pack(
                (1, state.FastForwardTimeToDawn),
                (2, state.SlimeRainActive),
                (3, state.DownedSlimeKing),
                (4, state.DownedQueenBee),
                (5, state.DownedFishron),
                (6, state.DownedMartians),
                (7, state.DownedAncientCultist)),
            EventInfo4 = Pack(
                (0, state.DownedMoonlord),
                (1, state.DownedHalloweenKing),
                (2, state.DownedHalloweenTree),
                (3, state.DownedChristmasIceQueen),
                (4, state.DownedChristmasSantank),
                (5, state.DownedChristmasTree),
                (6, state.DownedGolemBoss),
                (7, state.PartyIsUp)),
            EventInfo5 = Pack(
                (0, state.DownedPirates),
                (1, state.DownedFrost),
                (2, state.DownedGoblins),
                (3, state.SandstormHappening),
                (4, transient.Dd2EventOngoing),
                (5, state.DownedDd2InvasionT1),
                (6, state.DownedDd2InvasionT2),
                (7, state.DownedDd2InvasionT3)),
            EventInfo6 = Pack(
                (0, state.CombatBookWasUsed),
                (1, state.LanternsUp),
                (2, state.DownedTowerSolar),
                (3, state.DownedTowerVortex),
                (4, state.DownedTowerNebula),
                (5, state.DownedTowerStardust),
                (6, state.ForceHalloweenForToday),
                (7, state.ForceXMasForToday)),
            EventInfo7 = Pack(
                (0, state.BoughtCat),
                (1, state.BoughtDog),
                (2, state.BoughtBunny),
                (3, transient.FreeCake),
                (4, state.DrunkWorld),
                (5, state.DownedEmpressOfLight),
                (6, state.DownedQueenSlime),
                (7, state.GetGoodWorld)),
            EventInfo8 = Pack(
                (0, state.TenthAnniversaryWorld),
                (1, state.DontStarveWorld),
                (2, state.DownedDeerclops),
                (3, state.NotTheBeesWorld),
                (4, state.RemixWorld),
                (5, state.UnlockedSlimeBlueSpawn),
                (6, state.CombatBookVolumeTwoWasUsed),
                (7, state.PeddlersSatchelWasUsed)),
            EventInfo9 = Pack(
                (0, state.UnlockedSlimeGreenSpawn),
                (1, state.UnlockedSlimeOldSpawn),
                (2, state.UnlockedSlimePurpleSpawn),
                (3, state.UnlockedSlimeRainbowSpawn),
                (4, state.UnlockedSlimeRedSpawn),
                (5, state.UnlockedSlimeYellowSpawn),
                (6, state.UnlockedSlimeCopperSpawn),
                (7, state.FastForwardTimeToDusk)),
            EventInfo10 = Pack(
                (0, state.NoTrapsWorld),
                (1, state.ZenithWorld),
                (2, state.UnlockedTruffleSpawn),
                (3, state.VampireSeed),
                (4, state.InfectedSeed),
                (5, state.TeamBasedSpawnsSeed),
                (6, state.SkyblockWorld),
                (7, state.DualDungeonsSeed)),
            EventInfo11 = Pack(
                (0, transient.SkyblockLowTiles),
                (1, state.ForceHalloweenForever),
                (2, state.ForceXMasForever),
                (3, state.MoreLightningSeed),
                (4, state.NoLightningSeed)),
            SunDialCooldown = state.SundialCooldown,
            MoonDialCooldown = state.MoondialCooldown,
            OreTierCopper = state.OreTiers.Copper,
            OreTierIron = state.OreTiers.Iron,
            OreTierSilver = state.OreTiers.Silver,
            OreTierGold = state.OreTiers.Gold,
            OreTierCobalt = state.OreTiers.Cobalt,
            OreTierMythril = state.OreTiers.Mythril,
            OreTierAdamantite = state.OreTiers.Adamantite,
            InvasionType = state.InvasionType,
            LobbyId = transient.LobbyId,
            SandstormSeverity = state.SandstormIntendedSeverity,
            ExtraSpawnPoints = extraSpawns,
            DungeonX = state.DungeonX,
            DungeonY = state.DungeonY
        };
    }

    private static byte Pack(params (int Bit, bool Value)[] values)
    {
        byte result = 0;
        foreach ((int bit, bool value) in values)
        {
            if (value)
                result |= checked((byte)(1 << bit));
        }
        return result;
    }
}
