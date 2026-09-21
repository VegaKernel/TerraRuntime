using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Moon-event NPCs that use the already admitted <c>AI_003_Fighters</c> movement slice. Defaults are taken from
/// TerrariaServer 1.4.5.8 <c>NPC.SetDefaults</c>; event-specific spawn selection and the remaining special-AI types
/// are owned separately.
/// </summary>
public static class VanillaMoonEventGroundFighterCatalog1458
{
    private readonly record struct Entry(VanillaNpcDefinition Definition, VanillaGroundFighterBehaviorParameters Behavior);

    private static readonly Entry[] Entries =
    [
        Fighter(305, 60, 18, 500, .4f, 1f, 2f, VanillaGroundFighterMotionProfile.MoonEventLeaper),
        Fighter(306, 52, 14, 400, .2f, 1.05f, 1.25f, VanillaGroundFighterMotionProfile.MoonEventLeaper),
        Fighter(307, 78, 16, 600, .25f, .9f, 2.25f, VanillaGroundFighterMotionProfile.MoonEventLeaper),
        Fighter(308, 66, 14, 650, .35f, .95f, 1.5f, VanillaGroundFighterMotionProfile.MoonEventLeaper),
        Fighter(309, 52, 26, 450, .5f, 1.1f, 1f, VanillaGroundFighterMotionProfile.MoonEventLeaper),
        Fighter(310, 60, 18, 500, .4f, 1f, 2f), Fighter(311, 52, 14, 400, .2f, 1.05f, 1.25f),
        Fighter(312, 78, 16, 600, .25f, .9f, 2.25f), Fighter(313, 66, 14, 650, .35f, .95f, 1.5f),
        Fighter(314, 52, 26, 450, .5f, 1.1f, 1f),
        Fighter(326, 100, 32, 1200, .2f, 1f), Fighter(342, 90, 26, 750, .2f, 1f),
        Fighter(343, 140, 50, 3500, 0f, 1f, width: 38, height: 78),
        Fighter(348, 80, 26, 1800, .4f, 1f, width: 28, height: 76),
        Fighter(349, 100, 42, 1800, .1f, 1f, 6f, reversingVelocityDamping: .99f, width: 28, height: 76), Fighter(350, 70, 30, 900, .45f, 1f),
        Fighter(351, 100, 40, 2500, .1f, 1f, width: 18, height: 90)
    ];

    public static int DefinitionCount => Entries.Length;

    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions
    {
        get
        {
            var definitions = new VanillaNpcDefinition[Entries.Length];
            for (int index = 0; index < Entries.Length; index++)
                definitions[index] = Entries[index].Definition;
            return definitions;
        }
    }

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
        short type,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist,
        float scale,
        float maximumHorizontalSpeed = 1f,
        VanillaGroundFighterMotionProfile motionProfile = VanillaGroundFighterMotionProfile.Standard,
        float reversingVelocityDamping = 1f,
        int width = 18,
        int height = 40) =>
        new(
            new VanillaNpcDefinition(
                new NpcTypeId(type),
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
                HorizontalAcceleration: .07f,
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
                ScaleAdjustsMaximumHorizontalSpeed: false,
                CloseRangeLunge: false,
                MotionProfile: motionProfile,
                ReversingVelocityDamping: reversingVelocityDamping));
}
