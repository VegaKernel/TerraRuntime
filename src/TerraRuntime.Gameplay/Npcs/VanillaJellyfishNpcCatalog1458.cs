using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source-backed TerrariaServer 1.4.5.8 AI_018 definition admitted for the hostile Jellyfish.</summary>
public static class VanillaJellyfishNpcCatalog1458
{
    private static readonly VanillaNpcDefinition Definition = new(
        VanillaNpcIds.Jellyfish,
        VanillaNpcAiStyles.Jellyfish,
        VanillaNpcBehaviorFamily.Jellyfish,
        VanillaNpcPhysicsFamily.Jellyfish,
        NpcArchetypeRole.Ordinary,
        BaseWidth: 26,
        BaseHeight: 26,
        Damage: 90,
        Defense: 20,
        LifeMax: 140,
        KnockBackResist: 1f,
        Scale: 1f,
        NoGravityAtSpawn: true,
        NoTileCollideAtSpawn: false,
        VanillaNpcSyncAnchor.TopLeft)
    {
        AlphaAtSpawn = 20
    };

    private static readonly VanillaNpcDefinition[] Definitions = [Definition];

    public static int DefinitionCount => 1;

    public static ReadOnlySpan<VanillaNpcDefinition> AllDefinitions => Definitions;

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (type == VanillaNpcIds.Jellyfish)
        {
            definition = Definition;
            return true;
        }

        definition = default;
        return false;
    }
}
