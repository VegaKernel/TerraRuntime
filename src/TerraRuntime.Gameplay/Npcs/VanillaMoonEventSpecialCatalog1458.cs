using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source <c>NPC.SetDefaults</c> entry for the Snow Moon AI_025 special selection.</summary>
public static class VanillaMoonEventSpecialCatalog1458
{
    public static readonly NpcTypeId SnowMoonAi25 = new(341);

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (type != SnowMoonAi25)
        {
            definition = default;
            return false;
        }

        definition = new VanillaNpcDefinition(
            SnowMoonAi25,
            new NpcAiStyleId(25),
            VanillaNpcBehaviorFamily.MoonEventJumpingFighter,
            VanillaNpcPhysicsFamily.GenericGround,
            NpcArchetypeRole.Ordinary,
            24, 24, 100, 32, 900, .25f, 1f,
            NoGravityAtSpawn: false,
            NoTileCollideAtSpawn: false,
            VanillaNpcSyncAnchor.TopLeft);
        return true;
    }
}
