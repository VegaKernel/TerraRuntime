using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Immutable generation-safe role classification for one runtime-defined NPC occupation. RegistryRevision pins
/// the published descriptor image used for the decision; the role is never inferred from the presentation type.
/// </summary>
public readonly record struct RuntimeNpcRoleClassification(
    NpcHandle Npc,
    GameplayArchetypeId ArchetypeId,
    NpcArchetypeRole Role,
    ulong RegistryRevision)
{
    public bool IsValid =>
        Npc.IsAssigned &&
        ArchetypeId.IsAssigned &&
        Enum.IsDefined(Role) &&
        RegistryRevision > 0;

    public bool AllowsTownInteraction => Role == NpcArchetypeRole.Town;

    public bool RequiresBossLifecycle => Role == NpcArchetypeRole.Boss;

    public bool UsesOrdinaryLifecycle => Role == NpcArchetypeRole.Ordinary;
}

/// <summary>
/// Separates ordinary, town and boss policy selection from NPC AI and vanilla presentation identity. Vanilla NPC
/// roles remain unsupported until source-backed role metadata is imported; this boundary classifies only explicit
/// runtime archetypes and fails closed for missing, stale or unpublished identity.
/// </summary>
public sealed class RuntimeNpcRoleBoundary
{
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeNpcArchetypeIdentityStore identities;
    private readonly RuntimeNpcArchetypeRegistry archetypes;

    public RuntimeNpcRoleBoundary(
        RuntimeNpcStore npcs,
        RuntimeNpcArchetypeIdentityStore identities,
        RuntimeNpcArchetypeRegistry archetypes)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
        this.archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
    }

    public bool TryClassify(NpcHandle npc, out RuntimeNpcRoleClassification classification)
    {
        RuntimeGameplayArchetypeSnapshot<NpcArchetypeDescriptor> snapshot = archetypes.Snapshot;
        if (!npcs.TryGet(npc, out _) ||
            !identities.TryGet(npc, out GameplayArchetypeId archetypeId) ||
            !snapshot.TryGet(archetypeId, out NpcArchetypeDescriptor descriptor))
        {
            classification = default;
            return false;
        }

        classification = new RuntimeNpcRoleClassification(
            npc,
            archetypeId,
            descriptor.Role,
            snapshot.Revision);
        return classification.IsValid;
    }
}
