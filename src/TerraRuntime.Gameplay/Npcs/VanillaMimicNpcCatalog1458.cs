using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source-backed TerrariaServer 1.4.5.8 AI_025 Mimic defaults.</summary>
public static class VanillaMimicNpcCatalog1458
{
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Mimic(85, 80, 30, 500, .3f),
        Mimic(341, 100, 32, 900, .25f),
        Mimic(629, 80, 30, 500, .3f)
    ];

    public static int DefinitionCount => Definitions.Length;
    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions => Definitions;

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        foreach (VanillaNpcDefinition candidate in Definitions)
        {
            if (candidate.Type == type) { definition = candidate; return true; }
        }
        definition = default;
        return false;
    }

    public static bool IsMimic(NpcTypeId type) => type.Value is 85 or 341 or 629;

    public static bool HasPreHardModeDefaults(NpcTypeId type) => type.Value is 85 or 629;

    private static VanillaNpcDefinition Mimic(int type, int damage, int defense, int lifeMax, float knockBackResist) =>
        new(new NpcTypeId(type), new NpcAiStyleId(25), VanillaNpcBehaviorFamily.MoonEventJumpingFighter,
            VanillaNpcPhysicsFamily.GenericGround, NpcArchetypeRole.Ordinary, 24, 24, damage, defense, lifeMax,
            knockBackResist, 1f, NoGravityAtSpawn: false, NoTileCollideAtSpawn: false, VanillaNpcSyncAnchor.TopLeft);
}
