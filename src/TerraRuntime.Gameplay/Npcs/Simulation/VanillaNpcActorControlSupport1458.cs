using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Source-backed motion family admitted for trusted external actor-control intent. This is narrower than the full
/// vanilla AI surface: controlled actors reuse verified movement primitives while ordinary AI-only side effects stay
/// outside the controlled lane unless they are separately admitted.
/// </summary>
public enum VanillaNpcActorControlMotionFamily1458 : byte
{
    GroundFighter = 1,
    FlyingEye = 2,
    Flyer = 3,
    Bat = 4
}

/// <summary>
/// TerrariaServer 1.4.5.8 NPC families for which TerraRuntime has enough verified authoritative
/// physics/traversal coverage to accept external actor-control intents. This is a gameplay capability
/// boundary, not bot policy: callers must apply their own role/hostility restrictions separately.
/// </summary>
public static class VanillaNpcActorControlSupport1458
{
    private const VanillaNpcAiCapability CommonRequiredCapabilities =
        VanillaNpcAiCapability.DefinitionDefaults |
        VanillaNpcAiCapability.WorldPhysicsSlice |
        VanillaNpcAiCapability.PacketSync;

    public static bool IsSupported(NpcTypeId type) => TryGetMotionFamily(type, out _);

    public static bool TryGetMotionFamily(
        NpcTypeId type,
        out VanillaNpcActorControlMotionFamily1458 family)
    {
        family = default;
        if (!type.IsAssigned ||
            !VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition) ||
            !VanillaNpcAiCoverageCatalog.TryGet(type, out VanillaNpcAiCoverage coverage) ||
            !coverage.Has(CommonRequiredCapabilities))
        {
            return false;
        }

        if (definition.PhysicsFamily == VanillaNpcPhysicsFamily.GroundFighter &&
            definition.AiStyle == VanillaNpcAiStyles.Fighter &&
            coverage.Has(VanillaNpcAiCapability.GroundFighterTraversalSlice))
        {
            family = VanillaNpcActorControlMotionFamily1458.GroundFighter;
            return true;
        }

        if (definition.PhysicsFamily == VanillaNpcPhysicsFamily.FlyingEye &&
            definition.AiStyle == VanillaNpcAiStyles.DemonEye &&
            coverage.Has(VanillaNpcAiCapability.FlyingEyeSteeringProfileSlice) &&
            VanillaFlyingEyeNpcCatalog.TryGetMotionProfile(type, definition.LifeMax, definition.LifeMax, out _))
        {
            family = VanillaNpcActorControlMotionFamily1458.FlyingEye;
            return true;
        }

        if (definition.AiStyle == VanillaNpcAiStyles.Flyer &&
            definition.PhysicsFamily is VanillaNpcPhysicsFamily.FlyingEye or VanillaNpcPhysicsFamily.NoClipFlight &&
            coverage.Has(VanillaNpcAiCapability.FlyerPursuitProfileSlice) &&
            VanillaFlyerNpcCatalog.TryGetMotionProfile(type, out _))
        {
            family = VanillaNpcActorControlMotionFamily1458.Flyer;
            return true;
        }

        if (definition.AiStyle == VanillaNpcAiStyles.Bat &&
            definition.PhysicsFamily == VanillaNpcPhysicsFamily.BatFlight &&
            coverage.Has(VanillaNpcAiCapability.BatMotionSlice) &&
            VanillaBatNpcCatalog1458.TryGetDefinition(type, out _))
        {
            family = VanillaNpcActorControlMotionFamily1458.Bat;
            return true;
        }

        return false;
    }
}
