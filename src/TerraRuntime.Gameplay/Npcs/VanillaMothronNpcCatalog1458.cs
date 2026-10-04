using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Ordinary-world defaults for the source AI_088/089/090 parent, egg and child.</summary>
public static class VanillaMothronNpcCatalog1458
{
    public const int DefinitionCount = 3;
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Create(VanillaNpcIds.Mothron, VanillaNpcAiStyles.Mothron, 80, 50, 80, 30, 6000, .2f, true),
        Create(VanillaNpcIds.MothronEgg, VanillaNpcAiStyles.MothronEgg, 34, 34, 0, 30, 200, .7f, false),
        Create(VanillaNpcIds.BabyMothron, VanillaNpcAiStyles.BabyMothron, 46, 30, 50, 14, 700, .3f, false)
    ];

    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions => Definitions;

    public static bool IsSupported(NpcTypeId type) => type == VanillaNpcIds.Mothron ||
        type == VanillaNpcIds.MothronEgg || type == VanillaNpcIds.BabyMothron;

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        foreach (var candidate in Definitions)
            if (candidate.Type == type)
            {
                definition = candidate;
                return true;
            }
        definition = default;
        return false;
    }

    private static VanillaNpcDefinition Create(NpcTypeId type, NpcAiStyleId style, int width, int height,
        int damage, int defense, int lifeMax, float knockback, bool noGravity) => new(
            type, style, VanillaNpcBehaviorFamily.Mothron, VanillaNpcPhysicsFamily.Mothron,
            NpcArchetypeRole.Ordinary, width, height, damage, defense, lifeMax, knockback, 1f,
            noGravity, false, VanillaNpcSyncAnchor.TopLeft);
}
