using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Runtime-owned implementation family selected for one verified vanilla NPC definition.
/// AiStyle remains source metadata; this family is an explicit opt-in to a TerraRuntime behavior
/// implementation so a future NPC that happens to share an aiStyle cannot silently inherit a
/// behavior path that has not been verified for that type.
/// </summary>
public enum VanillaNpcBehaviorFamily : byte
{
    None = 0,
    SlimeGround = 1,
    FlyingEye = 2,
    GroundFighter = 3
}

/// <summary>
/// Runtime-owned physics family selected independently from AI behavior. A shared AI style/family does not
/// prove identical gravity, collision, platform or obstacle semantics, so every admitted definition opts in
/// explicitly to the world-motion path that has been verified for it.
/// </summary>
public enum VanillaNpcPhysicsFamily : byte
{
    None = 0,
    SlimeGround = 1,
    FlyingEye = 2,
    GroundFighter = 3
}

/// <summary>
/// Source-backed vanilla NPC defaults required by authoritative lifecycle and AI bring-up.
/// Values are clean-room facts extracted from TerrariaServer 1.4.5.8 SetDefaults; behavior stays
/// independently implemented in TerraRuntime. <see cref="BehaviorFamily"/> and <see cref="PhysicsFamily"/>
/// are runtime-owned metadata assigned only after the corresponding implementations are verified.
/// </summary>
public readonly record struct VanillaNpcDefinition(
    NpcTypeId Type,
    NpcAiStyleId AiStyle,
    VanillaNpcBehaviorFamily BehaviorFamily,
    VanillaNpcPhysicsFamily PhysicsFamily,
    int Width,
    int Height,
    int Damage,
    int Defense,
    int LifeMax,
    float KnockBackResist,
    float Scale);

/// <summary>
/// Initial verified slice of the Terraria 1.4.5.8 NPC defaults catalog.
/// Reference TerrariaServer.exe SHA-256:
/// d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e.
/// </summary>
public static class VanillaNpcDefinitionCatalog
{
    public const ushort DefaultTarget = byte.MaxValue;
    public const int DefaultTimeLeft = 750;
    public const int DefaultSpriteDirection = -1;

    /// <summary>
    /// Raw-id compatibility boundary for protocol/bootstrap callers that have not yet crossed into
    /// typed gameplay identity. Authoritative gameplay should prefer the typed overload.
    /// </summary>
    public static bool TryGet(int type, out VanillaNpcDefinition definition)
    {
        if (!NpcTypeId.TryCreate(type, out NpcTypeId npcType))
        {
            definition = default;
            return false;
        }

        return TryGet(npcType, out definition);
    }

    public static bool TryGet(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (type == VanillaNpcIds.BlueSlime)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.BlueSlime,
                AiStyle: VanillaNpcAiStyles.Slime,
                BehaviorFamily: VanillaNpcBehaviorFamily.SlimeGround,
                PhysicsFamily: VanillaNpcPhysicsFamily.SlimeGround,
                Width: 24,
                Height: 18,
                Damage: 7,
                Defense: 2,
                LifeMax: 25,
                KnockBackResist: 1f,
                Scale: 1f);
            return true;
        }

        if (type == VanillaNpcIds.DemonEye)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.DemonEye,
                AiStyle: VanillaNpcAiStyles.DemonEye,
                BehaviorFamily: VanillaNpcBehaviorFamily.FlyingEye,
                PhysicsFamily: VanillaNpcPhysicsFamily.FlyingEye,
                Width: 30,
                Height: 32,
                Damage: 18,
                Defense: 2,
                LifeMax: 60,
                KnockBackResist: 0.8f,
                Scale: 1f);
            return true;
        }

        if (type == VanillaNpcIds.Zombie)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.Zombie,
                AiStyle: VanillaNpcAiStyles.Fighter,
                BehaviorFamily: VanillaNpcBehaviorFamily.GroundFighter,
                PhysicsFamily: VanillaNpcPhysicsFamily.GroundFighter,
                Width: 18,
                Height: 40,
                Damage: 14,
                Defense: 6,
                LifeMax: 45,
                KnockBackResist: 0.5f,
                Scale: 1f);
            return true;
        }

        definition = default;
        return false;
    }
}