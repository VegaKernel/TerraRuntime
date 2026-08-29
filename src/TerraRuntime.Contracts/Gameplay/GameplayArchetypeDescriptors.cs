namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Server-defined NPC identity paired with a vanilla client-visible presentation. The archetype ID is runtime/host
/// identity only and is never serialized into vanilla NPC type fields. BehaviorId may be unassigned when the
/// archetype intentionally uses the normal vanilla behavior for its presentation type.
/// </summary>
public readonly record struct NpcArchetypeDescriptor(
    GameplayArchetypeId Id,
    NpcTypeId VanillaPresentationType,
    GameplayExtensionId BehaviorId = default);

/// <summary>
/// Server-defined projectile identity paired with a vanilla client-visible presentation. Official clients only see
/// VanillaPresentationType; custom identity remains runtime metadata. BehaviorId may be unassigned for vanilla AI.
/// </summary>
public readonly record struct ProjectileArchetypeDescriptor(
    GameplayArchetypeId Id,
    ProjectileTypeId VanillaPresentationType,
    GameplayExtensionId BehaviorId = default);
