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
        Fighter(VanillaNpcIds.FemaleZombie, 18, 40, 12, 4, 38, 0.6f, 1f, 0.87f, scaleAdjustsSpeed: true)
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
        VanillaNpcIds.FemaleZombie
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
        bool closeRangeLunge = false) =>
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
                HorizontalAcceleration: 0.07f,
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
                CloseRangeLunge: closeRangeLunge));
}
