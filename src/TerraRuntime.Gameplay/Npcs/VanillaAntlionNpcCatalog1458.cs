using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source-backed TerrariaServer 1.4.5.8 AI_019 definition admitted for Antlion.</summary>
public static class VanillaAntlionNpcCatalog1458
{
    private static readonly VanillaNpcDefinition Definition = new(
        VanillaNpcIds.Antlion,
        VanillaNpcAiStyles.Antlion,
        VanillaNpcBehaviorFamily.Antlion,
        VanillaNpcPhysicsFamily.GenericGround,
        NpcArchetypeRole.Ordinary,
        BaseWidth: 24,
        BaseHeight: 24,
        Damage: 10,
        Defense: 6,
        LifeMax: 45,
        KnockBackResist: 0f,
        Scale: 1f,
        NoGravityAtSpawn: false,
        NoTileCollideAtSpawn: false,
        VanillaNpcSyncAnchor.TopLeft);

    private static readonly VanillaNpcDefinition[] Definitions = [Definition];

    public static int DefinitionCount => 1;

    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions => Definitions;

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (type == VanillaNpcIds.Antlion)
        {
            definition = Definition;
            return true;
        }

        definition = default;
        return false;
    }
}
