using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Exact source motion/presentation identities; Rat and SeaTurtle retain their distinct frame branches.</summary>
public enum VanillaGroundCritterFamily1458 : byte
{
    Bunny = 1, Penguin = 2, Squirrel = 3, Mouse = 4, Rat = 5,
    Frog = 6, Scorpion = 7, Turtle = 8, SeaTurtle = 9
}

/// <summary>Explicit nonresident ground critter source identities; AI style membership alone grants no capability.</summary>
public static class VanillaNonresidentGroundCritterCatalog1458
{
    private static readonly string[] TurningNames = ["cenx", "cblox", "jade lightning", "cenigit"];
    private static readonly int[] NewIdentities = [148, 149, 299, 300, 361, 366, 367, 445, 447, 538, 539,
        610, 616, 617, 625, 639, 640, 641, 642, 643, 644, 645];
    private static readonly VanillaNpcLootTable[] SpecificTables = NewIdentities.Select(type =>
        new VanillaNpcLootTable(new(type), [])).ToArray();

    public static ReadOnlySpan<int> Types => [46, 148, 149, 299, 300, 303, 337, 361, 366, 367,
        443, 445, 447, 538, 539, 540, 610, 616, 617, 625, 639, 640, 641, 642, 643, 644, 645,
        646, 647, 648, 649, 650, 651, 652];

    public static bool IsSupported(int type) => type is 46 or 148 or 149 or 299 or 300 or 303 or 337 or
        361 or 366 or 367 or 443 or 445 or 447 or 538 or 539 or 540 or 610 or 616 or 617 or 625 or >= 639 and <= 652;

    // UpdateNPC_BloodMoonTransformations runs before AI007 for the five bunnies and both penguins.
    public static bool IsEvilConvertible(int type) => type is 46 or 148 or 149 or 303 or 337 or 443 or 540;

    public static bool IsTurningSquirrel(int type) => type is 299 or 538 or 539 or >= 639 and <= 645;
    // Player.IsThisCenx deliberately uses the current culture's ToLower, not an ordinal-ignore-case lookup.
    public static bool IsSpecialTurningName(string name) => Array.IndexOf(TurningNames, name.ToLower()) >= 0;
    public static bool IsGoldCritter(int type) => type is 443 or 445 or 447 or 539;
    // Initialized Main.npcCatchable controls SetDefaults/SubAI protection, independently
    // of CatchNPC's positive catchItem test. Seasonal bunnies retain a catchItem but
    // do not enter the temporary-friendly protection branch in the original server.
    public static bool HasCatchProtection(int type) =>
        TryGetFamily(type, out _) && type is not (303 or 337 or 540 or 625);

    public static bool SupportsAiStyleOverride(int type, NpcAiStyleId? style) =>
        style is null || IsTurningSquirrel(type) && style.Value.Value is 3 or 7;

    public static bool TryGetHostileFighterParameters(NpcTypeId type, out VanillaGroundFighterBehaviorParameters parameters)
    {
        parameters = default;
        if (!IsTurningSquirrel(type.Value)) return false;
        parameters = new(3.5f, .1f, 60f, 600f, 10, -5f, -5f, -6f, -7f, -8f, -8f, 1.5f,
            MotionProfile: VanillaGroundFighterMotionProfile.TurningCritter, DaySurfaceEncouragesDespawn: false);
        return true;
    }

    public static bool TryGetEffectiveFighterDefinition(int type, in NpcSimulationState simulation,
        in VanillaNpcDefinition definition, out VanillaNpcDefinition effective)
    {
        effective = default;
        if (!IsTurningSquirrel(type) || simulation.AiStyleOverride?.Value != 3 || definition.Type.Value != type)
            return false;
        effective = definition with { AiStyle = VanillaNpcAiStyles.Fighter,
            BehaviorFamily = VanillaNpcBehaviorFamily.GroundFighter, PhysicsFamily = VanillaNpcPhysicsFamily.GroundFighter };
        return true;
    }

    // Actual populated original GetRulesForNPCID(id,false) independently proves these22 empty tables.
    public static bool TryGetNpcSpecificTable(NpcTypeId type, out VanillaNpcLootTable table)
    {
        foreach (var candidate in SpecificTables)
            if (candidate.NpcType == type) { table = candidate; return true; }
        table = default;
        return false;
    }

    public static bool TryGetFamily(int type, out VanillaGroundCritterFamily1458 family)
    {
        family = type switch
        {
            46 or 303 or 337 or 443 or 540 or >= 646 and <= 652 => VanillaGroundCritterFamily1458.Bunny,
            148 or 149 => VanillaGroundCritterFamily1458.Penguin,
            299 or 538 or 539 or >= 639 and <= 645 => VanillaGroundCritterFamily1458.Squirrel,
            300 or 447 => VanillaGroundCritterFamily1458.Mouse,
            610 => VanillaGroundCritterFamily1458.Rat,
            361 or 445 => VanillaGroundCritterFamily1458.Frog,
            366 or 367 => VanillaGroundCritterFamily1458.Scorpion,
            616 or 617 => VanillaGroundCritterFamily1458.Turtle,
            625 => VanillaGroundCritterFamily1458.SeaTurtle,
            _ => default
        };
        return family != default;
    }
}
