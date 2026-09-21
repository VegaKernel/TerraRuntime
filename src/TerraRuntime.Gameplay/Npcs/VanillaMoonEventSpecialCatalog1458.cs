using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source <c>NPC.SetDefaults</c> entry for the Snow Moon AI_025 special selection.</summary>
public static class VanillaMoonEventSpecialCatalog1458
{
    public static readonly NpcTypeId SnowMoonAi25 = new(341);
    public static readonly NpcTypeId PumpkinMoonAi26MourningWood = new(315);
    public static readonly NpcTypeId PumpkinMoonAi26 = new(329);
    public static readonly NpcTypeId PumpkinMoonAi22 = new(330);

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

        if (type == PumpkinMoonAi26MourningWood)
        {
            definition = new VanillaNpcDefinition(
                PumpkinMoonAi26MourningWood,
                new NpcAiStyleId(26),
                VanillaNpcBehaviorFamily.MoonEventUnicorn,
                VanillaNpcPhysicsFamily.UnicornGround,
                NpcArchetypeRole.Ordinary,
                74, 70, 130, 40, 5000, 0f, 1f,
                NoGravityAtSpawn: false,
                NoTileCollideAtSpawn: false,
                VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == PumpkinMoonAi22)
        {
            definition = new VanillaNpcDefinition(
                PumpkinMoonAi22,
                new NpcAiStyleId(22),
                VanillaNpcBehaviorFamily.MoonEventGhost,
                VanillaNpcPhysicsFamily.NoClipFlight,
                NpcArchetypeRole.Ordinary,
                24, 44, 90, 44, 1250, .4f, 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                VanillaNpcSyncAnchor.TopLeft)
            {
                AlphaAtSpawn = 100
            };
            return true;
        }

        definition = default;
        return false;
    }
}
