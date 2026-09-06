using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public enum VanillaFishBottomSlope1458 : sbyte
{
    None = 0,
    FaceLeft = -1,
    FaceRight = 1
}

/// <summary>World facts read by the ordinary TerrariaServer 1.4.5.8 AI_016 swimming branch.</summary>
public interface IVanillaFishEnvironment1458
{
    bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight);

    VanillaFishBottomSlope1458 GetBottomSlope(float positionX, float positionY, int width, int height);

    bool HasDeepLiquidAboveAndActiveTileBelow(float positionX, float positionY, int width, int height);

    bool CanHitLineStraightAbove(float positionX, float positionY, int width, int height, float distance);

    bool TryGetWaterLineAtTop(float positionX, float positionY, int width, out float waterLineHeight);
}

/// <summary>
/// Source-backed AI_016 definitions, including Dolphin jump/surface state and Pufferfish inflation state.
/// </summary>
public static class VanillaFishNpcCatalog1458
{
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Fish(VanillaNpcIds.Goldfish, 20, 18, 0, 0, 5, 0.5f),
        Fish(VanillaNpcIds.CorruptGoldfish, 18, 20, 30, 6, 100, 1f),
        Fish(VanillaNpcIds.Piranha, 18, 20, 25, 2, 30, 1f),
        Fish(VanillaNpcIds.Shark, 100, 24, 40, 2, 300, 0.7f),
        Fish(VanillaNpcIds.AnglerFish, 18, 20, 80, 22, 90, 1f),
        Fish(VanillaNpcIds.Arapaima, 74, 20, 75, 30, 200, 1f),
        Fish(VanillaNpcIds.BloodFeeder, 18, 20, 50, 20, 150, 1f),
        Fish(VanillaNpcIds.CrimsonGoldfish, 18, 20, 31, 7, 110, 1f),
        Fish(VanillaNpcIds.GoldGoldfish, 20, 18, 0, 0, 5, 0.5f),
        Fish(VanillaNpcIds.Pupfish, 20, 18, 0, 0, 5, 0.5f),
        Fish(VanillaNpcIds.Dolphin, 20, 18, 0, 0, 5, 0.5f),
        Fish(VanillaNpcIds.Pufferfish, 32, 16, 0, 0, 5, 0.5f),
        Fish(VanillaNpcIds.Orca, 120, 34, 50, 20, 400, 0.7f)
    ];

    public static int DefinitionCount => Definitions.Length;

    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions => Definitions;

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        foreach (VanillaNpcDefinition candidate in Definitions)
        {
            if (candidate.Type == type)
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }

    public static bool IsPassiveFish(NpcTypeId type) =>
        type == VanillaNpcIds.Goldfish ||
        type == VanillaNpcIds.GoldGoldfish ||
        type == VanillaNpcIds.Pupfish ||
        type == VanillaNpcIds.Dolphin ||
        type == VanillaNpcIds.Pufferfish;

    public static bool UsesFastPursuit(NpcTypeId type) => type == VanillaNpcIds.Arapaima;

    public static bool UsesLargePursuit(NpcTypeId type) =>
        type == VanillaNpcIds.Shark ||
        type == VanillaNpcIds.AnglerFish ||
        type == VanillaNpcIds.Orca;

    public static bool UsesGroundedHorizontalDamping(NpcTypeId type) =>
        type == VanillaNpcIds.Shark || type == VanillaNpcIds.Orca;

    private static VanillaNpcDefinition Fish(
        NpcTypeId type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist) =>
        new(
            type,
            VanillaNpcAiStyles.Fish,
            VanillaNpcBehaviorFamily.Fish,
            VanillaNpcPhysicsFamily.FishSwimming,
            NpcArchetypeRole.Ordinary,
            width,
            height,
            damage,
            defense,
            lifeMax,
            knockBackResist,
            Scale: 1f,
            NoGravityAtSpawn: true,
            NoTileCollideAtSpawn: false,
            VanillaNpcSyncAnchor.TopLeft);
}
