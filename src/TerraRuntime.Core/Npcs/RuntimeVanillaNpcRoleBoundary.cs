using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Generation-safe role classification for one live vanilla NPC. Unlike runtime/custom archetype role identity,
/// this classification is derived only from the version-pinned vanilla definition catalog and the exact current
/// NPC generation occupying the requested slot.
/// </summary>
public readonly record struct VanillaNpcRoleClassification(
    NpcHandle Npc,
    NpcTypeId Type,
    NpcArchetypeRole Role)
{
    public bool IsValid =>
        Npc.IsAssigned &&
        Type.IsAssigned &&
        Enum.IsDefined(Role);

    public bool AllowsTownInteraction => Role == NpcArchetypeRole.Town;

    public bool RequiresBossLifecycle => Role == NpcArchetypeRole.Boss;

    public bool UsesOrdinaryLifecycle => Role == NpcArchetypeRole.Ordinary;
}

/// <summary>
/// Resolves role policy for current vanilla NPC generations without depending on custom-archetype identity.
/// A stale handle, unsupported vanilla type or missing catalog definition fails closed. This keeps boss/town
/// policy selection separate from AI-style dispatch and from presentation/network identity.
/// </summary>
public sealed class RuntimeVanillaNpcRoleBoundary
{
    private readonly RuntimeNpcStore _npcs;

    public RuntimeVanillaNpcRoleBoundary(RuntimeNpcStore npcs) =>
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));

    public bool TryClassify(NpcHandle npc, out VanillaNpcRoleClassification classification)
    {
        if (!_npcs.TryGet(npc, out NpcSnapshot snapshot) ||
            !NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition))
        {
            classification = default;
            return false;
        }

        classification = new VanillaNpcRoleClassification(
            snapshot.Handle,
            definition.Type,
            definition.Role);
        return classification.IsValid;
    }
}
