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
/// Mutable server-owned clock/event values that must override the persisted world snapshot in live packet 7 broadcasts.
/// </summary>
public readonly record struct WorldInfoRuntimeState(
    int Time,
    bool DayTime,
    byte MoonPhase,
    bool BloodMoon,
    bool SlimeRainActive);

/// <summary>
/// Maps validated Terraria 1.4.5.8 world state plus live runtime flags to protocol 326 packet 7.
/// Bit assignments remain at the Multiplicity boundary rather than leaking wire layout into the world model.
/// </summary>
public static class WorldInfoPacketMapper
{
    public static WorldInfo Create(
        WorldFileData world,
        WorldInfoTransientState transient = default,
        WorldInfoRuntimeState? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Create(world.Header, world.RuntimeMetadata, transient, runtime);
    }

    public static WorldInfo Create(
        WorldFileHeader header,
        WorldFileRuntimeMetadata state,
        WorldInfoTransientState transient = default,
        WorldInfoRuntimeState? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(state);

        if (state.TreeX.Length != 3 || state.TreeStyles.Length != 4 ||
            state.CaveBackX.Length != 3 || state.CaveBackStyles.Length != 4 ||
            state.TreeTopVariations.Length != 13)
        {
            throw new InvalidOperationException("Validated world runtime metadata has an invalid fixed-array shape.");
        }

        var extraSpawns = new global::Multiplicity.Packets.WorldSpawnPoint[state.ExtraSpawnPoints.Length];
        for (int i = 0; i < extraSpawns.Length; i++)
        {
            TerraRuntime.World.WorldSpawnPoint source = state.ExtraSpawnPoints[i];
            extraSpawns[i] = new global::Multiplicity.Packets.WorldSpawnPoint(source.X, source.Y);
        }

        WorldInfoRuntimeState live = runtime ?? new WorldInfoRuntimeState(
            state.Time, state.DayTime, state.MoonPhase, state.BloodMoon, state.SlimeRainActive);

        return new WorldInfo
        {
            Time = live.Time,
            DayandMoonInfo = Bits(live.DayTime, live.BloodMoon, state.Eclipse),
            MoonPhase = live.MoonPhase,
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
            EventInfo = Bits(
                state.ShadowOrbSmashed,
                state.DownedBoss1,
                state.DownedBoss2,
                state.DownedBoss3,
                state.HardMode,
                state.DownedClown,
                false,
                state.DownedPlantBoss),
            EventInfo2 = Bits(
                state.DownedMechBoss1,
                state.DownedMechBoss2,
                state.DownedMechBoss3,
                state.DownedMechBossAny,
                state.CloudBackgroundActive,
                state.Crimson,
                transient.PumpkinMoon,
                transient.SnowMoon),
            EventInfo3 = Bits(
                false,
                state.FastForwardTimeToDawn,
                live.SlimeRainActive,
                state.DownedSlimeKing,
                state.DownedQueenBee,
                state.DownedFishron,
                state.DownedMartians,
                state.DownedAncientCultist),
            EventInfo4 = Bits(
                state.DownedMoonlord,
                state.DownedHalloweenKing,
                state.DownedHalloweenTree,
                state.DownedChristmasIceQueen,
                state.DownedChristmasSantank,
                state.DownedChristmasTree,
                state.DownedGolemBoss,
                state.PartyIsUp),
            EventInfo5 = Bits(
                state.DownedPirates,
                state.DownedFrost,
                state.DownedGoblins,
                state.SandstormHappening,
                transient.Dd2EventOngoing,
                state.DownedDd2InvasionT1,
                state.DownedDd2InvasionT2,
                state.DownedDd2InvasionT3),
            EventInfo6 = Bits(
                state.CombatBookWasUsed,
                state.LanternsUp,
                state.DownedTowerSolar,
                state.DownedTowerVortex,
                state.DownedTowerNebula,
                state.DownedTowerStardust,
                state.ForceHalloweenForToday,
                state.ForceXMasForToday),
            EventInfo7 = Bits(
                state.BoughtCat,
                state.BoughtDog,
                state.BoughtBunny,
                transient.FreeCake,
                state.DrunkWorld,
                state.DownedEmpressOfLight,
                state.DownedQueenSlime,
                state.GetGoodWorld),
            EventInfo8 = Bits(
                state.TenthAnniversaryWorld,
                state.DontStarveWorld,
                state.DownedDeerclops,
                state.NotTheBeesWorld,
                state.RemixWorld,
                state.UnlockedSlimeBlueSpawn,
                state.CombatBookVolumeTwoWasUsed,
                state.PeddlersSatchelWasUsed),
            EventInfo9 = Bits(
                state.UnlockedSlimeGreenSpawn,
                state.UnlockedSlimeOldSpawn,
                state.UnlockedSlimePurpleSpawn,
                state.UnlockedSlimeRainbowSpawn,
                state.UnlockedSlimeRedSpawn,
                state.UnlockedSlimeYellowSpawn,
                state.UnlockedSlimeCopperSpawn,
                state.FastForwardTimeToDusk),
            EventInfo10 = Bits(
                state.NoTrapsWorld,
                state.ZenithWorld,
                state.UnlockedTruffleSpawn,
                state.VampireSeed,
                state.InfectedSeed,
                state.TeamBasedSpawnsSeed,
                state.SkyblockWorld,
                state.DualDungeonsSeed),
            EventInfo11 = Bits(
                transient.SkyblockLowTiles,
                state.ForceHalloweenForever,
                state.ForceXMasForever,
                state.MoreLightningSeed,
                state.NoLightningSeed),
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

    private static byte Bits(
        bool bit0 = false,
        bool bit1 = false,
        bool bit2 = false,
        bool bit3 = false,
        bool bit4 = false,
        bool bit5 = false,
        bool bit6 = false,
        bool bit7 = false)
    {
        int value = 0;
        if (bit0) value |= 1 << 0;
        if (bit1) value |= 1 << 1;
        if (bit2) value |= 1 << 2;
        if (bit3) value |= 1 << 3;
        if (bit4) value |= 1 << 4;
        if (bit5) value |= 1 << 5;
        if (bit6) value |= 1 << 6;
        if (bit7) value |= 1 << 7;
        return (byte)value;
    }
}
