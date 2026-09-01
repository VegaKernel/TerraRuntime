using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaFlyingEyeAxisProfile(
    float Acceleration,
    float OvershootAcceleration,
    float WrongDirectionBrake,
    float MaximumSpeed,
    float OvershootThreshold,
    float PositiveEngagementThreshold)
{
    public bool IsValid =>
        float.IsFinite(Acceleration) && Acceleration >= 0f &&
        float.IsFinite(OvershootAcceleration) && OvershootAcceleration >= 0f &&
        float.IsFinite(WrongDirectionBrake) &&
        float.IsFinite(MaximumSpeed) && MaximumSpeed > 0f &&
        float.IsFinite(OvershootThreshold) && OvershootThreshold > 0f &&
        float.IsFinite(PositiveEngagementThreshold) && PositiveEngagementThreshold > 0f;
}

public readonly record struct VanillaFlyingEyeMotionProfile(
    VanillaFlyingEyeAxisProfile Horizontal,
    VanillaFlyingEyeAxisProfile Vertical,
    bool RisesInWater)
{
    public bool IsValid => Horizontal.IsValid && Vertical.IsValid;
}

/// <summary>Source-backed hostile AI_002 defaults and steering families.</summary>
public static class VanillaFlyingEyeNpcCatalog
{
    private static readonly VanillaFlyingEyeAxisProfile DefaultHorizontal =
        Axis(0.1f, 0.1f, 0.05f, 4f);
    private static readonly VanillaFlyingEyeAxisProfile DefaultVertical =
        Axis(0.04f, 0.05f, 0.03f, 1.5f);
    private static readonly VanillaFlyingEyeAxisProfile PigronHorizontal =
        Axis(0.08f, 0.04f, -0.2f, 4f);
    private static readonly VanillaFlyingEyeAxisProfile PigronVertical =
        Axis(0.1f, 0.05f, -0.15f, 2.5f);
    private static readonly VanillaFlyingEyeAxisProfile HungryHorizontal =
        Axis(0.1f, 0.1f, -0.2f, 6f);
    private static readonly VanillaFlyingEyeAxisProfile HungryVertical =
        new(0.04f, 0.05f, -0.15f, 2.5f, 2.5f, 1.5f);
    private static readonly VanillaFlyingEyeAxisProfile EnragedWanderingHorizontal =
        Axis(0.1f, 0.1f, 0.05f, 6f);
    private static readonly VanillaFlyingEyeAxisProfile EnragedWanderingVertical =
        Axis(0.1f, 0.1f, 0.05f, 4f);

    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Eye(VanillaNpcIds.TheHungryII, 30, 32, 30, 6, 80, 0.8f),
        Eye(VanillaNpcIds.WanderingEye, 30, 32, 40, 20, 300, 0.8f),
        Eye(VanillaNpcIds.PigronCorruption, 44, 36, 70, 16, 210, 0.5f),
        Eye(VanillaNpcIds.PigronHallow, 44, 36, 70, 16, 210, 0.5f),
        Eye(VanillaNpcIds.PigronCrimson, 44, 36, 70, 16, 210, 0.5f),
        Eye(VanillaNpcIds.CataractEye, 30, 32, 18, 4, 65, 0.7f),
        Eye(VanillaNpcIds.SleepyEye, 30, 32, 16, 2, 60, 0.85f),
        Eye(VanillaNpcIds.DilatedEye, 30, 32, 18, 2, 50, 0.8f),
        Eye(VanillaNpcIds.GreenEye, 30, 32, 20, 0, 60, 0.8f),
        Eye(VanillaNpcIds.PurpleEye, 30, 32, 14, 4, 60, 0.8f),
        Eye(VanillaNpcIds.DemonEyeOwl, 30, 32, 16, 6, 75, 0.7f),
        Eye(VanillaNpcIds.DemonEyeSpaceship, 30, 32, 20, 4, 60, 0.65f)
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

    public static bool TryGetMotionProfile(
        NpcTypeId type,
        int life,
        int lifeMax,
        out VanillaFlyingEyeMotionProfile profile)
    {
        if (type != VanillaNpcIds.DemonEye && !TryGetDefinition(type, out _))
        {
            profile = default;
            return false;
        }

        if (IsPigron(type))
        {
            profile = new(PigronHorizontal, PigronVertical, RisesInWater: false);
        }
        else if (type == VanillaNpcIds.TheHungryII)
        {
            profile = new(HungryHorizontal, HungryVertical, RisesInWater: true);
        }
        else if (type == VanillaNpcIds.WanderingEye && lifeMax > 0 && life < lifeMax / 2)
        {
            profile = new(
                EnragedWanderingHorizontal,
                EnragedWanderingVertical,
                RisesInWater: true);
        }
        else
        {
            profile = new(DefaultHorizontal, DefaultVertical, RisesInWater: true);
        }

        return true;
    }

    public static bool FleesDaylight(NpcTypeId type) =>
        type == VanillaNpcIds.DemonEye ||
        type == VanillaNpcIds.WanderingEye ||
        type == VanillaNpcIds.CataractEye ||
        type == VanillaNpcIds.SleepyEye ||
        type == VanillaNpcIds.DilatedEye ||
        type == VanillaNpcIds.GreenEye ||
        type == VanillaNpcIds.PurpleEye ||
        type == VanillaNpcIds.DemonEyeOwl ||
        type == VanillaNpcIds.DemonEyeSpaceship;

    public static bool IsPigron(NpcTypeId type) =>
        type == VanillaNpcIds.PigronCorruption ||
        type == VanillaNpcIds.PigronHallow ||
        type == VanillaNpcIds.PigronCrimson;

    private static VanillaFlyingEyeAxisProfile Axis(
        float acceleration,
        float overshootAcceleration,
        float wrongDirectionBrake,
        float maximumSpeed) =>
        new(
            acceleration,
            overshootAcceleration,
            wrongDirectionBrake,
            maximumSpeed,
            maximumSpeed,
            maximumSpeed);

    private static VanillaNpcDefinition Eye(
        NpcTypeId type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist) =>
        new(
            type,
            VanillaNpcAiStyles.DemonEye,
            VanillaNpcBehaviorFamily.FlyingEye,
            VanillaNpcPhysicsFamily.FlyingEye,
            NpcArchetypeRole.Ordinary,
            width,
            height,
            damage,
            defense,
            lifeMax,
            knockBackResist,
            Scale: 1f,
            NoGravityAtSpawn: false,
            NoTileCollideAtSpawn: false,
            VanillaNpcSyncAnchor.TopLeft);
}
