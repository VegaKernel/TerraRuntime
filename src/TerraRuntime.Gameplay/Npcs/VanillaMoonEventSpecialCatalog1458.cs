using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source <c>NPC.SetDefaults</c> entry for the Snow Moon AI_025 special selection.</summary>
public static class VanillaMoonEventSpecialCatalog1458
{
    public static readonly NpcTypeId SnowMoonAi25 = new(341);
    public static readonly NpcTypeId SnowMoonAi57Everscream = new(344);
    public static readonly NpcTypeId SnowMoonAi60Santank = new(345);
    public static readonly NpcTypeId SnowMoonAi61IceQueen = new(346);
    public static readonly NpcTypeId SnowMoonAi62 = new(347);
    public static readonly NpcTypeId SnowMoonAi63 = new(352);
    public static readonly NpcTypeId PumpkinMoonAi57MourningWood = new(325);
    public static readonly NpcTypeId PumpkinMoonAi26MourningWood = new(315);
    public static readonly NpcTypeId PumpkinMoonAi26 = new(329);
    public static readonly NpcTypeId PumpkinMoonAi22 = new(330);
    public static readonly NpcTypeId PumpkinMoonAi58Pumpking = new(327);
    public static readonly NpcTypeId PumpkinMoonAi59PumpkingBlade = new(328);

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

        if (type == SnowMoonAi57Everscream)
        {
            definition = new VanillaNpcDefinition(
                SnowMoonAi57Everscream,
                new NpcAiStyleId(57),
                VanillaNpcBehaviorFamily.MoonEventEverscream,
                VanillaNpcPhysicsFamily.NoClipFlight,
                NpcArchetypeRole.Ordinary,
                172, 130, 110, 38, 13000, 0f, 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == SnowMoonAi60Santank)
        {
            definition = new VanillaNpcDefinition(
                SnowMoonAi60Santank,
                new NpcAiStyleId(60),
                VanillaNpcBehaviorFamily.SnowMoonSantank,
                VanillaNpcPhysicsFamily.NoClipFlight,
                NpcArchetypeRole.Ordinary,
                130, 140, 120, 38, 34000, 0f, 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == SnowMoonAi61IceQueen)
        {
            definition = new VanillaNpcDefinition(
                SnowMoonAi61IceQueen,
                new NpcAiStyleId(61),
                VanillaNpcBehaviorFamily.SnowMoonIceQueen,
                VanillaNpcPhysicsFamily.NoClipFlight,
                NpcArchetypeRole.Ordinary,
                112, 140, 120, 56, 18000, 0f, 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == SnowMoonAi62)
        {
            definition = new VanillaNpcDefinition(SnowMoonAi62, new NpcAiStyleId(62), VanillaNpcBehaviorFamily.SnowMoonAi62,
                VanillaNpcPhysicsFamily.NoClipFlight, NpcArchetypeRole.Ordinary, 50, 50, 60, 28, 1200, .4f, 1f,
                NoGravityAtSpawn: true, NoTileCollideAtSpawn: true, VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == SnowMoonAi63)
        {
            definition = new VanillaNpcDefinition(SnowMoonAi63, new NpcAiStyleId(63), VanillaNpcBehaviorFamily.SnowMoonAi63,
                VanillaNpcPhysicsFamily.NoClipFlight, NpcArchetypeRole.Ordinary, 54, 54, 75, 8, 450, .4f, 1f,
                NoGravityAtSpawn: true, NoTileCollideAtSpawn: true, VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == PumpkinMoonAi57MourningWood)
        {
            definition = new VanillaNpcDefinition(
                PumpkinMoonAi57MourningWood,
                new NpcAiStyleId(57),
                VanillaNpcBehaviorFamily.MoonEventEverscream,
                VanillaNpcPhysicsFamily.NoClipFlight,
                NpcArchetypeRole.Ordinary,
                164, 154, 120, 34, 14000, 0f, 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == PumpkinMoonAi58Pumpking)
        {
            definition = new VanillaNpcDefinition(PumpkinMoonAi58Pumpking, new NpcAiStyleId(58),
                VanillaNpcBehaviorFamily.PumpkinMoonPumpking, VanillaNpcPhysicsFamily.NoClipFlight,
                NpcArchetypeRole.Boss, 100, 100, 50, 40, 26000, 0f, 1f, true, true,
                VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == PumpkinMoonAi59PumpkingBlade)
        {
            definition = new VanillaNpcDefinition(PumpkinMoonAi59PumpkingBlade, new NpcAiStyleId(59),
                VanillaNpcBehaviorFamily.PumpkinMoonPumpking, VanillaNpcPhysicsFamily.NoClipFlight,
                NpcArchetypeRole.Ordinary, 80, 80, 65, 14, 5000, 0f, 1f, true, true,
                VanillaNpcSyncAnchor.TopLeft) { DontTakeDamageAtSpawn = true };
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
