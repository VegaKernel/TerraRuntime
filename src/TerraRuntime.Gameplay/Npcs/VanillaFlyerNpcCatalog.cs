using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Deterministic movement parameters used by the AI_005 pursuit core.</summary>
public readonly record struct VanillaFlyerMotionProfile(
    float MaximumSpeed,
    float Acceleration,
    bool TurnsHard,
    float BounceFactor,
    float WaterRiseAcceleration,
    float WaterRiseSpeedCap)
{
    public bool HasBounce => BounceFactor > 0f;
    public bool RisesInWater => WaterRiseAcceleration > 0f;

    public bool IsValid =>
        float.IsFinite(MaximumSpeed) && MaximumSpeed > 0f &&
        float.IsFinite(Acceleration) && Acceleration > 0f &&
        float.IsFinite(BounceFactor) && BounceFactor is >= 0f and <= 1f &&
        float.IsFinite(WaterRiseAcceleration) && WaterRiseAcceleration >= 0f &&
        float.IsFinite(WaterRiseSpeedCap) && WaterRiseSpeedCap >= 0f &&
        (WaterRiseAcceleration == 0f) == (WaterRiseSpeedCap == 0f);
}

/// <summary>Source-backed hostile AI_005 definitions and classic-mode pursuit profiles.</summary>
public static class VanillaFlyerNpcCatalog
{
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Flyer(VanillaNpcIds.EaterOfSouls, 30, 30, 22, 8, 40, 0.5f),
        Flyer(VanillaNpcIds.MeteorHead, 22, 22, 40, 6, 26, 0.4f, noTileCollide: true),
        Flyer(VanillaNpcIds.Hornet, 34, 32, 26, 12, 48, 0.5f),
        Flyer(VanillaNpcIds.Corruptor, 44, 44, 60, 32, 230, 0.55f),
        Flyer(VanillaNpcIds.Probe, 30, 30, 50, 20, 200, 0.8f, noTileCollide: true),
        Flyer(VanillaNpcIds.Crimera, 30, 30, 22, 8, 40, 0.5f),
        Flyer(VanillaNpcIds.MossHornet, 34, 32, 70, 22, 220, 0.5f),
        Flyer(VanillaNpcIds.Moth, 40, 40, 70, 28, 1000, 0.4f),
        Flyer(VanillaNpcIds.Bee, 12, 12, 20, 5, 20, 0.5f),
        Flyer(VanillaNpcIds.SmallBee, 8, 8, 15, 2, 10, 0.5f),
        Flyer(VanillaNpcIds.FattyHornet, 34, 32, 22, 16, 50, 0.3f),
        Flyer(VanillaNpcIds.HoneyHornet, 34, 32, 28, 12, 42, 0.6f),
        Flyer(VanillaNpcIds.LeafyHornet, 34, 32, 30, 14, 38, 0.45f),
        Flyer(VanillaNpcIds.SpikeyHornet, 34, 32, 32, 6, 42, 0.55f),
        Flyer(VanillaNpcIds.StingyHornet, 34, 32, 34, 4, 38, 0.6f),
        Flyer(VanillaNpcIds.Parrot, 32, 32, 80, 12, 100, 0.7f),
        Flyer(VanillaNpcIds.BloodSquid, 44, 44, 60, 16, 750, 0f)
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

    public static bool TryGetMotionProfile(NpcTypeId type, out VanillaFlyerMotionProfile profile)
    {
        if (type == VanillaNpcIds.ServantOfCthulhu)
            profile = Profile(5f, 0.03f, turnsHard: true);
        else if (type == VanillaNpcIds.EaterOfSouls || type == VanillaNpcIds.Crimera)
            profile = Profile(4f, 0.02f, turnsHard: false, bounce: 0.4f, waterAcceleration: 0.3f, waterCap: 2f);
        else if (type == VanillaNpcIds.Corruptor)
            profile = Profile(4.2f, 0.022f, turnsHard: false, bounce: 0.7f, waterAcceleration: 0.3f, waterCap: 2f);
        else if (type == VanillaNpcIds.BloodSquid)
            profile = Profile(6f, 0.1f, turnsHard: false, bounce: 0.7f, waterAcceleration: 0.3f, waterCap: 2f);
        else if (type == VanillaNpcIds.FattyHornet)
            profile = Profile(3f, 0.017f, turnsHard: false, bounce: 0.7f, waterAcceleration: 0.5f, waterCap: 4f);
        else if (IsCommonHornet(type))
            profile = Profile(3.5f, 0.021f, turnsHard: false, bounce: 0.7f, waterAcceleration: 0.5f, waterCap: 4f);
        else if (type == VanillaNpcIds.Moth)
            profile = Profile(3.25f, 0.018f, turnsHard: true, bounce: 0.7f, waterAcceleration: 0.5f, waterCap: 4f);
        else if (type == VanillaNpcIds.MossHornet)
            profile = Profile(4f, 0.017f, turnsHard: true, bounce: 0.7f, waterAcceleration: 0.5f, waterCap: 4f);
        else if (type == VanillaNpcIds.MeteorHead)
            profile = Profile(1f, 0.03f, turnsHard: true, bounce: 0.7f);
        else if (type == VanillaNpcIds.Probe ||
                 type == VanillaNpcIds.Bee ||
                 type == VanillaNpcIds.SmallBee)
            profile = Profile(6f, 0.05f, turnsHard: type != VanillaNpcIds.Probe, bounce: 0.7f);
        else if (type == VanillaNpcIds.Parrot)
            profile = Profile(6f, 0.05f, turnsHard: true);
        else
        {
            profile = default;
            return false;
        }

        return true;
    }

    public static bool UsesScaleSpeedHandicap(NpcTypeId type) =>
        type == VanillaNpcIds.Hornet ||
        type == VanillaNpcIds.FattyHornet ||
        type == VanillaNpcIds.HoneyHornet ||
        type == VanillaNpcIds.LeafyHornet ||
        type == VanillaNpcIds.SpikeyHornet ||
        type == VanillaNpcIds.StingyHornet;

    private static bool IsCommonHornet(NpcTypeId type) =>
        type == VanillaNpcIds.Hornet ||
        type == VanillaNpcIds.HoneyHornet ||
        type == VanillaNpcIds.LeafyHornet ||
        type == VanillaNpcIds.SpikeyHornet ||
        type == VanillaNpcIds.StingyHornet;

    private static VanillaFlyerMotionProfile Profile(
        float maximumSpeed,
        float acceleration,
        bool turnsHard,
        float bounce = 0f,
        float waterAcceleration = 0f,
        float waterCap = 0f) =>
        new(maximumSpeed, acceleration, turnsHard, bounce, waterAcceleration, waterCap);

    private static VanillaNpcDefinition Flyer(
        NpcTypeId type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist,
        bool noTileCollide = false) =>
        new(
            type,
            VanillaNpcAiStyles.Flyer,
            VanillaNpcBehaviorFamily.Flyer,
            noTileCollide ? VanillaNpcPhysicsFamily.NoClipFlight : VanillaNpcPhysicsFamily.FlyingEye,
            NpcArchetypeRole.Ordinary,
            width,
            height,
            damage,
            defense,
            lifeMax,
            knockBackResist,
            Scale: 1f,
            NoGravityAtSpawn: true,
            NoTileCollideAtSpawn: noTileCollide,
            VanillaNpcSyncAnchor.TopLeft);
}
