using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Source-backed hostile AI_003 definitions and movement profiles from TerrariaServer 1.4.5.8
/// NPC.SetDefaults and NPC.AI_003_Fighters. This catalog admits only the common ground-fighter slice;
/// subtype-only projectiles, transformations, door destruction and seed/event behavior remain separate capabilities.
/// </summary>
public static class VanillaGroundFighterNpcCatalog
{
    private readonly record struct Entry(
        VanillaNpcDefinition Definition,
        VanillaGroundFighterBehaviorParameters Behavior);

    private static readonly Entry[] Entries =
    [
        Fighter(VanillaNpcIds.Zombie, 18, 40, 14, 6, 45, 0.5f, 1f, 1f, scaleAdjustsSpeed: true),
        Fighter(VanillaNpcIds.Skeleton, 18, 40, 20, 8, 60, 0.5f, 1f, 1.5f, scaleAdjustsSpeed: true),
        Fighter(VanillaNpcIds.GoblinPeon, 18, 38, 12, 4, 60, 0.8f, 0.9f, 1.5f),
        Fighter(VanillaNpcIds.GoblinThief, 18, 38, 20, 6, 80, 0.7f, 0.95f, 2f),
        Fighter(VanillaNpcIds.GoblinWarrior, 18, 38, 25, 8, 110, 0.5f, 1.1f, 1f),
        Fighter(VanillaNpcIds.AngryBones, 18, 40, 26, 8, 80, 0.8f, 1f, 1.5f, closeRangeLunge: true),
        Fighter(VanillaNpcIds.DoctorBones, 18, 40, 20, 10, 500, 0.5f, 1f, 1f),
        Fighter(VanillaNpcIds.TheGroom, 18, 40, 14, 8, 200, 0.5f, 1f, 1f),
        Fighter(VanillaNpcIds.GoblinScout, 18, 40, 20, 6, 80, 0.7f, 0.95f, 1.5f),
        Fighter(VanillaNpcIds.ArmoredSkeleton, 18, 40, 40, 28, 260, 0.4f, 1f, 2f, closeRangeLunge: true),
        Fighter(VanillaNpcIds.BaldZombie, 18, 40, 15, 5, 40, 0.5f, 1f, 0.95f, scaleAdjustsSpeed: true),
        Fighter(VanillaNpcIds.ZombieEskimo, 18, 40, 16, 8, 50, 0.45f, 1f, 1f),
        Fighter(VanillaNpcIds.UndeadViking, 18, 40, 24, 10, 70, 0.5f, 1f, 1.5f),
        Fighter(VanillaNpcIds.PincushionZombie, 18, 40, 16, 8, 50, 0.45f, 1f, 1.1f, scaleAdjustsSpeed: true),
        Fighter(VanillaNpcIds.SlimedZombie, 18, 40, 13, 6, 40, 0.55f, 1f, 0.9f, scaleAdjustsSpeed: true),
        Fighter(VanillaNpcIds.SwampZombie, 18, 40, 13, 8, 45, 0.45f, 1f, 1.2f, scaleAdjustsSpeed: true),
        Fighter(VanillaNpcIds.TwiggyZombie, 18, 40, 16, 4, 45, 0.55f, 1f, 0.8f, scaleAdjustsSpeed: true),
        Fighter(VanillaNpcIds.FemaleZombie, 18, 40, 12, 4, 38, 0.6f, 1f, 0.87f, scaleAdjustsSpeed: true),
        Fighter(VanillaNpcIds.VampireHumanoid, 18, 40, 80, 24, 750, 0.4f, 1f, 6f,
            reversingVelocityDamping: 0.95f)
        // TerrariaServer 1.4.5.8 NPC.SetDefaults (338..340) and AI_003_Fighters num84 bands.
        ,Fighter(VanillaNpcIds.ZombieElf, 18, 40, 65, 18, 600, .4f, 1f, 1.75f)
        ,Fighter(VanillaNpcIds.ZombieElfBeard, 18, 40, 52, 24, 700, .2f, 1.05f, 1.25f)
        ,Fighter(VanillaNpcIds.ZombieElfGirl, 18, 40, 78, 14, 500, .25f, .9f, 2f)
        // TerrariaServer 1.4.5.8 NPC.SetDefaults (319..324); each reaches AI_003's generic num108 branch.
        ,Fighter(VanillaNpcIds.ZombieDoctor, 18, 40, 20, 6, 40, .6f, .9f, 1f)
        ,Fighter(VanillaNpcIds.ZombieSuperman, 18, 40, 15, 8, 60, .5f, 1.05f, 1f)
        ,Fighter(VanillaNpcIds.ZombiePixie, 18, 40, 20, 14, 34, .3f, 1.1f, 1f)
        ,Fighter(VanillaNpcIds.SkeletonTopHat, 18, 40, 23, 0, 115, .65f, 1f, 1f)
        ,Fighter(VanillaNpcIds.SkeletonAstronaut, 18, 40, 18, 10, 65, .5f, 1f, 1f)
        ,Fighter(VanillaNpcIds.SkeletonAlien, 18, 40, 22, 10, 70, .4f, 1.05f, 1f)
        // TerrariaServer 1.4.5.8 NPC.SetDefaults (331/332) and AI_003_Fighters num108 scaling.
        ,Fighter(VanillaNpcIds.ZombieXmas, 18, 40, 14, 6, 45, .5f, 1f, 1f, scaleAdjustsSpeed: true)
        ,Fighter(VanillaNpcIds.ZombieSweater, 18, 40, 14, 6, 45, .5f, 1f, 1f, scaleAdjustsSpeed: true)
        // TerrariaServer 1.4.5.8 NPC.SetDefaults (430..436) and AI_003 armed melee state.
        ,Fighter(VanillaNpcIds.ArmedZombie, 18, 40, 14, 6, 45, .5f, 1f, 1f,
            motionProfile: VanillaGroundFighterMotionProfile.ArmedZombie)
        ,Fighter(VanillaNpcIds.ArmedZombieEskimo, 18, 40, 16, 8, 50, .45f, 1f, 1f,
            motionProfile: VanillaGroundFighterMotionProfile.ArmedZombie)
        ,Fighter(VanillaNpcIds.ArmedZombiePincushion, 18, 40, 16, 8, 50, .45f, 1f, 1f,
            motionProfile: VanillaGroundFighterMotionProfile.ArmedZombie)
        ,Fighter(VanillaNpcIds.ArmedZombieSlimed, 18, 40, 13, 6, 40, .55f, 1f, 1f,
            motionProfile: VanillaGroundFighterMotionProfile.ArmedZombie)
        ,Fighter(VanillaNpcIds.ArmedZombieSwamp, 18, 40, 13, 8, 45, .45f, 1f, 1f,
            motionProfile: VanillaGroundFighterMotionProfile.ArmedZombie)
        ,Fighter(VanillaNpcIds.ArmedZombieTwiggy, 18, 40, 16, 4, 45, .55f, 1f, 1f,
            motionProfile: VanillaGroundFighterMotionProfile.ArmedZombie)
        ,Fighter(VanillaNpcIds.ArmedZombieCenx, 18, 40, 12, 4, 38, .6f, 1f, 1f,
            motionProfile: VanillaGroundFighterMotionProfile.ArmedZombie)
        ,Fighter(new NpcTypeId(78), 18, 40, 50, 16, 130, .6f, 1f, 1f,
            acceleration: .05f, motionProfile: VanillaGroundFighterMotionProfile.HalfHealthBerserker, overspeedGroundDamping: .7f)
        ,Fighter(new NpcTypeId(79), 18, 40, 60, 18, 180, .5f, 1f, 1f,
            acceleration: .05f, motionProfile: VanillaGroundFighterMotionProfile.HalfHealthBerserker, halfHealthSpeedMultiplier: 1.5f, overspeedGroundDamping: .7f)
        ,Fighter(new NpcTypeId(80), 18, 40, 55, 18, 200, .55f, 1f, 1f,
            acceleration: .05f, motionProfile: VanillaGroundFighterMotionProfile.HalfHealthBerserker, overspeedGroundDamping: .7f)
        ,Fighter(new NpcTypeId(287), 18, 40, 90, 42, 1000, .3f, 1f, 5f,
            acceleration: .2f, overspeedGroundDamping: .7f)
        ,Fighter(new NpcTypeId(630), 18, 40, 60, 18, 180, .5f, 1f, 1f,
            acceleration: .05f, motionProfile: VanillaGroundFighterMotionProfile.HalfHealthBerserker, halfHealthSpeedMultiplier: 1.5f, overspeedGroundDamping: .7f)
        ,Fighter(new NpcTypeId(243), 30, 114, 60, 32, 4000, .05f, 1f, 1f,
            motionProfile: VanillaGroundFighterMotionProfile.MissingHealthBerserker, overspeedGroundDamping: .7f,
            missingHealthSpeedBonus: 1.5f, missingHealthAccelerationBonus: .15f)
        ,Fighter(new NpcTypeId(251), 18, 40, 50, 30, 1000, .3f, 1f, 1f,
            acceleration: .08f, motionProfile: VanillaGroundFighterMotionProfile.MissingHealthBerserker, overspeedGroundDamping: .7f,
            missingHealthSpeedBonus: 2f, missingHealthAccelerationBonus: .2f)
        ,Fighter(new NpcTypeId(254), 18, 40, 40, 10, 180, .4f, 1f, 1.5f, daySurfaceEncouragesDespawn: false)
        ,Fighter(new NpcTypeId(255), 18, 40, 38, 16, 220, .3f, 1f, 1f, daySurfaceEncouragesDespawn: false)
        ,Fighter(new NpcTypeId(257), 44, 34, 38, 24, 230, .3f, 1f, 2f, daySurfaceEncouragesDespawn: false)
        ,Fighter(new NpcTypeId(258), 30, 24, 60, 16, 220, .3f, 1f, 3f,
            reversingVelocityDamping: .99f, daySurfaceEncouragesDespawn: false)
        ,Fighter(new NpcTypeId(269), 18, 40, 70, 34, 550, .3f, 1f, 2f)
        ,Fighter(new NpcTypeId(270), 18, 40, 55, 50, 400, .2f, 1f, 1f)
        ,Fighter(new NpcTypeId(271), 18, 40, 70, 40, 450, .25f, 1f, 1.5f)
        ,Fighter(new NpcTypeId(272), 18, 40, 75, 28, 400, .35f, 1f, 3f)
        ,Fighter(new NpcTypeId(273), 18, 40, 45, 50, 500, .15f, 1f, 1.25f)
        ,Fighter(new NpcTypeId(274), 18, 40, 65, 34, 350, .4f, 1f, 3f)
        ,Fighter(new NpcTypeId(275), 18, 40, 45, 50, 550, .15f, 1f, 3.25f)
        ,Fighter(new NpcTypeId(276), 18, 40, 85, 54, 500, .2f, 1f, 2f)
        ,Fighter(new NpcTypeId(277), 18, 40, 70, 32, 400, .4f, 1f, 2.75f)
        ,Fighter(new NpcTypeId(278), 18, 40, 65, 48, 450, .3f, 1f, 1.8f)
        ,Fighter(new NpcTypeId(279), 18, 40, 40, 54, 500, .2f, 1f, 1.3f)
        ,Fighter(new NpcTypeId(280), 18, 40, 75, 34, 500, .4f, 1f, 2.5f)
    ];

    private static readonly NpcTypeId[] AdditionalTypes =
    [
        VanillaNpcIds.GoblinPeon,
        VanillaNpcIds.GoblinThief,
        VanillaNpcIds.GoblinWarrior,
        VanillaNpcIds.AngryBones,
        VanillaNpcIds.DoctorBones,
        VanillaNpcIds.TheGroom,
        VanillaNpcIds.GoblinScout,
        VanillaNpcIds.ArmoredSkeleton,
        VanillaNpcIds.BaldZombie,
        VanillaNpcIds.ZombieEskimo,
        VanillaNpcIds.UndeadViking,
        VanillaNpcIds.PincushionZombie,
        VanillaNpcIds.SlimedZombie,
        VanillaNpcIds.SwampZombie,
        VanillaNpcIds.TwiggyZombie,
        VanillaNpcIds.FemaleZombie,
        VanillaNpcIds.VampireHumanoid,
        VanillaNpcIds.ZombieElf,
        VanillaNpcIds.ZombieElfBeard,
        VanillaNpcIds.ZombieElfGirl,
        VanillaNpcIds.ZombieDoctor,
        VanillaNpcIds.ZombieSuperman,
        VanillaNpcIds.ZombiePixie,
        VanillaNpcIds.SkeletonTopHat,
        VanillaNpcIds.SkeletonAstronaut,
        VanillaNpcIds.SkeletonAlien,
        VanillaNpcIds.ZombieXmas,
        VanillaNpcIds.ZombieSweater,
        VanillaNpcIds.ArmedZombie,
        VanillaNpcIds.ArmedZombieEskimo,
        VanillaNpcIds.ArmedZombiePincushion,
        VanillaNpcIds.ArmedZombieSlimed,
        VanillaNpcIds.ArmedZombieSwamp,
        VanillaNpcIds.ArmedZombieTwiggy,
        VanillaNpcIds.ArmedZombieCenx,
        new(78), new(79), new(80), new(243), new(251), new(254), new(255), new(257), new(258), new(287), new(630),
        new(269), new(270), new(271), new(272), new(273), new(274), new(275), new(276), new(277), new(278), new(279), new(280)
    ];

    public static int DefinitionCount => Entries.Length;
    public static int AdditionalDefinitionCount => AdditionalTypes.Length;
    public static ReadOnlySpan<NpcTypeId> AdditionalHostileTypes => AdditionalTypes;

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        foreach (Entry entry in Entries)
        {
            if (entry.Definition.Type == type)
            {
                definition = entry.Definition;
                return true;
            }
        }

        definition = default;
        return false;
    }

    public static bool TryGetBehavior(NpcTypeId type, out VanillaGroundFighterBehaviorParameters behavior)
    {
        foreach (Entry entry in Entries)
        {
            if (entry.Definition.Type == type)
            {
                behavior = entry.Behavior;
                return true;
            }
        }

        behavior = default;
        return false;
    }

    private static Entry Fighter(
        NpcTypeId type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist,
        float scale,
        float maximumHorizontalSpeed,
        bool scaleAdjustsSpeed = false,
        bool closeRangeLunge = false,
        float reversingVelocityDamping = 1f,
        float acceleration = .07f,
        VanillaGroundFighterMotionProfile motionProfile = VanillaGroundFighterMotionProfile.Standard,
        float halfHealthSpeedMultiplier = 1f,
        float overspeedGroundDamping = .8f,
        float missingHealthSpeedBonus = 0f,
        float missingHealthAccelerationBonus = 0f,
        bool daySurfaceEncouragesDespawn = true) =>
        new(
            new VanillaNpcDefinition(
                type,
                VanillaNpcAiStyles.Fighter,
                VanillaNpcBehaviorFamily.GroundFighter,
                VanillaNpcPhysicsFamily.GroundFighter,
                NpcArchetypeRole.Ordinary,
                width,
                height,
                damage,
                defense,
                lifeMax,
                knockBackResist,
                scale,
                NoGravityAtSpawn: false,
                NoTileCollideAtSpawn: false,
                VanillaNpcSyncAnchor.TopLeft),
            new VanillaGroundFighterBehaviorParameters(
                BaseMaximumHorizontalSpeed: maximumHorizontalSpeed,
                HorizontalAcceleration: acceleration,
                StuckThreshold: 60f,
                MaximumStuckCounter: 600f,
                EncouragedDespawnTime: 10,
                StuckHopVelocity: -5f,
                LowStepJumpVelocity: -5f,
                OneTileJumpVelocity: -6f,
                TwoTileJumpVelocity: -7f,
                ThreeTileJumpVelocity: -8f,
                PursuitGapJumpVelocity: -8f,
                PursuitGapSpeedMultiplier: 1.5f,
                ScaleAdjustsMaximumHorizontalSpeed: scaleAdjustsSpeed,
                CloseRangeLunge: closeRangeLunge,
                ReversingVelocityDamping: reversingVelocityDamping,
                MotionProfile: motionProfile,
                HalfHealthSpeedMultiplier: halfHealthSpeedMultiplier,
                OverspeedGroundDamping: overspeedGroundDamping,
                MissingHealthSpeedBonus: missingHealthSpeedBonus,
                MissingHealthAccelerationBonus: missingHealthAccelerationBonus,
                DaySurfaceEncouragesDespawn: daySurfaceEncouragesDespawn));
}
