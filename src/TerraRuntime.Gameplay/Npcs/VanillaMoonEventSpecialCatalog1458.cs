using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source <c>NPC.SetDefaults</c> entry for the Snow Moon AI_025 special selection.</summary>
public static class VanillaMoonEventSpecialCatalog1458
{
    public static readonly NpcTypeId SnowMoonAi25 = new(341);
    public static readonly NpcTypeId PumpkinMoonAi26 = new(329);

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (type == SnowMoonAi25)
        {
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

        if (type == PumpkinMoonAi26)
        {
            definition = new VanillaNpcDefinition(
                PumpkinMoonAi26,
                new NpcAiStyleId(26),
                VanillaNpcBehaviorFamily.MoonEventUnicorn,
                VanillaNpcPhysicsFamily.UnicornGround,
                NpcArchetypeRole.Ordinary,
                46, 30, 80, 38, 1800, .3f, 1f,
                NoGravityAtSpawn: false,
                NoTileCollideAtSpawn: false,
                VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        definition = default;
        return false;
    }
}
