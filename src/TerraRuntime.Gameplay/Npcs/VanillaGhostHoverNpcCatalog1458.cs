using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Normal-world SetDefaults for the three independently verified AI_022 identities.</summary>
public static class VanillaGhostHoverNpcCatalog1458
{
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Create(VanillaNpcIds.Wraith, 24, 44, 65, 16, 160, .7f, true, 100),
        Create(VanillaNpcIds.Gastropod, 20, 20, 60, 22, 220, .8f, false, 0),
        Create(VanillaNpcIds.Reaper, 24, 44, 80, 22, 700, .6f, true, 100)
    ];

    public static int DefinitionCount => Definitions.Length;
    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions => Definitions;

    public static bool IsSupported(NpcTypeId type) =>
        type == VanillaNpcIds.Wraith || type == VanillaNpcIds.Gastropod || type == VanillaNpcIds.Reaper;

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

    private static VanillaNpcDefinition Create(NpcTypeId type, int width, int height, int damage,
        int defense, int lifeMax, float knockback, bool noTileCollide, int alpha) => new(
            type, VanillaNpcAiStyles.GhostHover, VanillaNpcBehaviorFamily.GhostHover,
            VanillaNpcPhysicsFamily.GhostHover,
            NpcArchetypeRole.Ordinary, width, height, damage, defense, lifeMax, knockback, 1f,
            NoGravityAtSpawn: true, NoTileCollideAtSpawn: noTileCollide, VanillaNpcSyncAnchor.TopLeft)
        { AlphaAtSpawn = alpha };
}
