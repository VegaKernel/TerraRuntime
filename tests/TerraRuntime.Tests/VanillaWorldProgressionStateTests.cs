using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldProgressionStateTests
{
    [Fact]
    public void Every_named_progression_milestone_maps_from_persistence_state()
    {
        var metadata = new WorldFileRuntimeMetadata
        {
            DownedBoss1 = true,
            DownedBoss2 = true,
            DownedBoss3 = true,
            DownedQueenBee = true,
            DownedMechBoss1 = true,
            DownedMechBoss2 = true,
            DownedMechBoss3 = true,
            DownedMechBossAny = true,
            DownedPlantBoss = true,
            DownedGolemBoss = true,
            DownedSlimeKing = true,
            DownedGoblins = true,
            DownedClown = true,
            DownedFrost = true,
            DownedPirates = true,
            ShadowOrbSmashed = true,
            HardMode = true,
            DownedFishron = true,
            DownedMartians = true,
            DownedAncientCultist = true,
            DownedMoonlord = true,
            DownedHalloweenKing = true,
            DownedHalloweenTree = true,
            DownedChristmasIceQueen = true,
            DownedChristmasSantank = true,
            DownedChristmasTree = true,
            DownedTowerSolar = true,
            DownedTowerVortex = true,
            DownedTowerNebula = true,
            DownedTowerStardust = true,
            DownedDd2InvasionT1 = true,
            DownedDd2InvasionT2 = true,
            DownedDd2InvasionT3 = true,
            DownedEmpressOfLight = true,
            DownedQueenSlime = true,
            DownedDeerclops = true
        };

        Assert.Equal(
            VanillaWorldProgressionState.MilestoneCount,
            Enum.GetValues<VanillaWorldProgressionId>().Length);
        foreach (VanillaWorldProgressionId milestone in Enum.GetValues<VanillaWorldProgressionId>())
            Assert.True(metadata.Progression.IsComplete(milestone), milestone.ToString());
    }

    [Fact]
    public void Sparse_progression_projection_does_not_mix_unrelated_milestones()
    {
        var metadata = new WorldFileRuntimeMetadata
        {
            DownedSlimeKing = true,
            HardMode = true,
            DownedMoonlord = true
        };

        VanillaWorldProgressionState state = metadata.Progression;
        Assert.True(state.IsComplete(VanillaWorldProgressionId.KingSlime));
        Assert.True(state.IsComplete(VanillaWorldProgressionId.Hardmode));
        Assert.True(state.IsComplete(VanillaWorldProgressionId.MoonLord));
        Assert.False(state.IsComplete(VanillaWorldProgressionId.EyeOfCthulhu));
        Assert.False(state.IsComplete(VanillaWorldProgressionId.QueenSlime));
        Assert.False(state.IsComplete((VanillaWorldProgressionId)byte.MaxValue));
    }

    [Fact]
    public void Event_projection_keeps_transient_events_and_invasion_identity_separate()
    {
        var metadata = new WorldFileRuntimeMetadata
        {
            InvasionType = (sbyte)VanillaWorldInvasionId.MartianMadness,
            BloodMoon = true,
            SlimeRainTime = 1d,
            PartyGenuine = true,
            LanternNightManual = true,
            SandstormHappening = true,
            ForceHalloweenForever = true
        };

        VanillaWorldEventState events = metadata.Events;
        Assert.True(events.HasKnownInvasionIdentity);
        Assert.True(events.HasActiveInvasion);
        Assert.Equal(VanillaWorldInvasionId.MartianMadness, events.Invasion);
        Assert.True(events.IsActive(VanillaWorldEventId.BloodMoon));
        Assert.True(events.IsActive(VanillaWorldEventId.SlimeRain));
        Assert.True(events.IsActive(VanillaWorldEventId.Party));
        Assert.True(events.IsActive(VanillaWorldEventId.LanternNight));
        Assert.True(events.IsActive(VanillaWorldEventId.Sandstorm));
        Assert.True(events.IsActive(VanillaWorldEventId.Halloween));
        Assert.False(events.IsActive(VanillaWorldEventId.Eclipse));
        Assert.False(events.IsActive(VanillaWorldEventId.Christmas));
    }

    [Fact]
    public void Unknown_persisted_invasion_fails_closed()
    {
        var metadata = new WorldFileRuntimeMetadata { InvasionType = 99 };

        VanillaWorldEventState events = metadata.Events;
        Assert.False(events.HasKnownInvasionIdentity);
        Assert.False(events.HasActiveInvasion);
        Assert.Equal(VanillaWorldInvasionId.Unknown, events.Invasion);
        Assert.False(VanillaWorldInvasionIds.TryCreate(-1, out _));
        Assert.False(VanillaWorldInvasionIds.TryCreate(VanillaWorldInvasionIds.Count, out _));
    }
}
