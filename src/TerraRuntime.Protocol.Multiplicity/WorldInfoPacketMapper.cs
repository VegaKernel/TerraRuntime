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
    bool SlimeRainActive)
{
    /// <summary>Live <c>Main.windSpeedTarget</c>; absent callers retain the persisted header value.</summary>
    public float? WindSpeedTarget { get; init; }

    /// <summary>Live packet-7 rain intensity; absent callers retain the persisted header value.</summary>
    public float? Rain { get; init; }

    /// <summary>Live solar eclipse; absent callers retain the persisted header value.</summary>
    public bool? Eclipse { get; init; }

    /// <summary>Owned temporary-season rewards; null retains the loaded header flags.</summary>
    public bool? ForceHalloweenForToday { get; init; }
    public bool? ForceXMasForToday { get; init; }

    /// <summary>Owned current invasion identity; null preserves the loaded header value.</summary>
    public sbyte? InvasionType { get; init; }

    /// <summary>Detached milestones completed by the authoritative runtime since world load.</summary>
    public RuntimeWorldProgressionMutationSnapshot? ProgressionMutations { get; init; }
}

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

        bool Done(VanillaWorldProgressionId milestone, bool persisted) =>
            persisted || live.ProgressionMutations?.IsCompleted(milestone) == true;

        return new WorldInfo
        {
            Time = live.Time,
            DayandMoonInfo = Bits(live.DayTime, live.BloodMoon, live.Eclipse ?? state.Eclipse),
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
            WindSpeedSet = live.WindSpeedTarget ?? state.WindSpeed,
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
            Rain = live.Rain ?? state.NetworkRain,
            EventInfo = Bits(
                Done(VanillaWorldProgressionId.ShadowOrbSmashed, state.ShadowOrbSmashed),
                Done(VanillaWorldProgressionId.EyeOfCthulhu, state.DownedBoss1),
                Done(VanillaWorldProgressionId.EvilBoss, state.DownedBoss2),
                Done(VanillaWorldProgressionId.Skeletron, state.DownedBoss3),
                Done(VanillaWorldProgressionId.Hardmode, state.HardMode),
                Done(VanillaWorldProgressionId.Clown, state.DownedClown),
                false,
                Done(VanillaWorldProgressionId.Plantera, state.DownedPlantBoss)),
            EventInfo2 = Bits(
                Done(VanillaWorldProgressionId.Destroyer, state.DownedMechBoss1),
                Done(VanillaWorldProgressionId.Twins, state.DownedMechBoss2),
                Done(VanillaWorldProgressionId.SkeletronPrime, state.DownedMechBoss3),
                Done(VanillaWorldProgressionId.AnyMechanicalBoss, state.DownedMechBossAny),
                state.CloudBackgroundActive,
                state.Crimson,
                transient.PumpkinMoon,
                transient.SnowMoon),
            EventInfo3 = Bits(
                false,
                state.FastForwardTimeToDawn,
                live.SlimeRainActive,
                Done(VanillaWorldProgressionId.KingSlime, state.DownedSlimeKing),
                Done(VanillaWorldProgressionId.QueenBee, state.DownedQueenBee),
                Done(VanillaWorldProgressionId.DukeFishron, state.DownedFishron),
                Done(VanillaWorldProgressionId.MartianMadness, state.DownedMartians),
                Done(VanillaWorldProgressionId.LunaticCultist, state.DownedAncientCultist)),
            EventInfo4 = Bits(
                Done(VanillaWorldProgressionId.MoonLord, state.DownedMoonlord),
                Done(VanillaWorldProgressionId.Pumpking, state.DownedHalloweenKing),
                Done(VanillaWorldProgressionId.MourningWood, state.DownedHalloweenTree),
                Done(VanillaWorldProgressionId.IceQueen, state.DownedChristmasIceQueen),
                Done(VanillaWorldProgressionId.SantaNk1, state.DownedChristmasSantank),
                Done(VanillaWorldProgressionId.Everscream, state.DownedChristmasTree),
                Done(VanillaWorldProgressionId.Golem, state.DownedGolemBoss),
                state.PartyIsUp),
            EventInfo5 = Bits(
                Done(VanillaWorldProgressionId.PirateInvasion, state.DownedPirates),
                Done(VanillaWorldProgressionId.FrostLegion, state.DownedFrost),
                Done(VanillaWorldProgressionId.GoblinArmy, state.DownedGoblins),
                state.SandstormHappening,
                transient.Dd2EventOngoing,
                Done(VanillaWorldProgressionId.OldOnesArmyTier1, state.DownedDd2InvasionT1),
                Done(VanillaWorldProgressionId.OldOnesArmyTier2, state.DownedDd2InvasionT2),
                Done(VanillaWorldProgressionId.OldOnesArmyTier3, state.DownedDd2InvasionT3)),
            EventInfo6 = Bits(
                state.CombatBookWasUsed,
                state.LanternsUp,
                Done(VanillaWorldProgressionId.SolarPillar, state.DownedTowerSolar),
                Done(VanillaWorldProgressionId.VortexPillar, state.DownedTowerVortex),
                Done(VanillaWorldProgressionId.NebulaPillar, state.DownedTowerNebula),
                Done(VanillaWorldProgressionId.StardustPillar, state.DownedTowerStardust),
                live.ForceHalloweenForToday ?? state.ForceHalloweenForToday,
                live.ForceXMasForToday ?? state.ForceXMasForToday),
            EventInfo7 = Bits(
                state.BoughtCat,
                state.BoughtDog,
                state.BoughtBunny,
                transient.FreeCake,
                state.DrunkWorld,
                Done(VanillaWorldProgressionId.EmpressOfLight, state.DownedEmpressOfLight),
                Done(VanillaWorldProgressionId.QueenSlime, state.DownedQueenSlime),
                state.GetGoodWorld),
            EventInfo8 = Bits(
                state.TenthAnniversaryWorld,
                state.DontStarveWorld,
                Done(VanillaWorldProgressionId.Deerclops, state.DownedDeerclops),
                state.NotTheBeesWorld,
                state.RemixWorld,
                state.UnlockedSlimeBlueSpawn || live.ProgressionMutations?.UnlockSlimeBlueSpawn == true,
                state.CombatBookVolumeTwoWasUsed,
                state.PeddlersSatchelWasUsed),
            EventInfo9 = Bits(
                state.UnlockedSlimeGreenSpawn,
                state.UnlockedSlimeOldSpawn,
                state.UnlockedSlimePurpleSpawn,
                state.UnlockedSlimeRainbowSpawn,
                state.UnlockedSlimeRedSpawn || live.ProgressionMutations?.UnlockSlimeRedSpawn == true,
                state.UnlockedSlimeYellowSpawn || live.ProgressionMutations?.UnlockSlimeYellowSpawn == true,
                state.UnlockedSlimeCopperSpawn,
                state.FastForwardTimeToDusk),
            EventInfo10 = Bits(
                state.NoTrapsWorld,
                state.ZenithWorld,
                state.UnlockedTruffleSpawn || live.ProgressionMutations?.UnlockTruffleSpawn == true,
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
            InvasionType = live.InvasionType ?? state.InvasionType,
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
