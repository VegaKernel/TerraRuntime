using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 defaults for the Muse-audited AI_017/020/021 slice.
/// These identities are admitted only because both SetDefaults and their complete server-relevant motion
/// branches are represented; the rest of the Muse branch remains fail-closed.
/// </summary>
public static class VanillaNpcAi17_20_21Catalog1458
{
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        new VanillaNpcDefinition(
            VanillaNpcIds.Vulture,
            VanillaNpcAiStyles.Vulture,
            VanillaNpcBehaviorFamily.Vulture,
            VanillaNpcPhysicsFamily.Vulture,
            NpcArchetypeRole.Ordinary,
            36,
            36,
            15,
            4,
            40,
            0.8f,
            1f,
            false,
            false,
            VanillaNpcSyncAnchor.TopLeft),
        new VanillaNpcDefinition(
            VanillaNpcIds.Raven,
            VanillaNpcAiStyles.Vulture,
            VanillaNpcBehaviorFamily.Vulture,
            VanillaNpcPhysicsFamily.Vulture,
            NpcArchetypeRole.Ordinary,
            36,
            26,
            12,
            2,
            35,
            0.85f,
            1f,
            false,
            false,
            VanillaNpcSyncAnchor.TopLeft),
        new VanillaNpcDefinition(
            VanillaNpcIds.SpikeBall,
            VanillaNpcAiStyles.SpikeBall,
            VanillaNpcBehaviorFamily.SpikeBall,
            VanillaNpcPhysicsFamily.SpikeBall,
            NpcArchetypeRole.Ordinary,
            34,
            34,
            32,
            100,
            100,
            0f,
            1.5f,
            true,
            true,
            VanillaNpcSyncAnchor.TopLeft)
        {
            DontTakeDamageAtSpawn = true
        },
        new VanillaNpcDefinition(
            VanillaNpcIds.BlazingWheel,
            VanillaNpcAiStyles.BlazingWheel,
            VanillaNpcBehaviorFamily.BlazingWheel,
            VanillaNpcPhysicsFamily.BlazingWheel,
            NpcArchetypeRole.Ordinary,
            34,
            34,
            24,
            100,
            100,
            0f,
            1.2f,
            true,
            false,
            VanillaNpcSyncAnchor.TopLeft)
        {
            DontTakeDamageAtSpawn = true
        }
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
}
