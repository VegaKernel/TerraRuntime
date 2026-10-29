using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Explicit NPC.Transform edges required by the admitted amphibious, Lihzahrd and rescue callers.</summary>
public static class VanillaNpcTransformation1458
{
    private static readonly NpcTypeId[] CritterTypes = [new(230), new(593), new(687)];
    public static ReadOnlySpan<NpcTypeId> GroundCritterTypes => CritterTypes;

    public static bool IsNewDefinition(NpcTypeId type) => type.Value is 198 or 230 or 532 or 534 or 586 or 593 or 687;

    public static bool IsNewFighter(NpcTypeId type) => type.Value is 198 or 532 or 534 or 586;
    public static bool IsZombieMerman(NpcTypeId type) => type.Value == 586;
    public static bool UsesLihzahrdIdleSound(NpcTypeId type) => type.Value is 198 or 199;

    public static bool TryGetLifeThresholdTarget(NpcTypeId type, int life, int lifeMax, out NpcTypeId target)
    {
        target = default;
        if (type.Value != 198 || lifeMax <= 0 || (double)life > (double)lifeMax * .55d) return false;
        target = VanillaNpcIds.Lihzahrd;
        return true;
    }

    public static bool Supports(NpcTypeId source, NpcTypeId target) => source.Value switch
    {
        198 => target == VanillaNpcIds.Lihzahrd,
        230 => target == VanillaNpcIds.Goldfish || IsEvilFish(target),
        593 => target == VanillaNpcIds.GoldGoldfish || IsEvilFish(target),
        55 => target.Value == 230 || IsEvilFish(target),
        592 => target.Value == 593 || IsEvilFish(target),
        534 => target == VanillaNpcIds.TaxCollector,
        687 => target == VanillaNpcIds.TownSlimeYellow,
        _ => false
    };

    public static bool IsRetainedFish(NpcTypeId type) => type.Value is 55 or 57 or 465 or 592;

    public static bool IsDestination(NpcTypeId type) => type.Value is 55 or 57 or 199 or 230 or 441 or 465 or 592 or 593 or 683;

    // Main.Initialize_TileAndNPCData1 owns this distinction. catchItem alone is
    // insufficient (for example, the legacy seasonal bunny identities).
    public static bool HasDestinationCatchProtection(NpcTypeId type) => type.Value is 55 or 592 or 593;

    public static bool HasPhaseCatchProtection(NpcTypeId type) => type.Value is 55 or 592 or 593;

    public static bool TryGetGroundCritterFamily(NpcTypeId type, out VanillaGroundCritterFamily1458 family)
    {
        family = type.Value switch
        {
            230 or 593 => VanillaGroundCritterFamily1458.Goldfish,
            687 => VanillaGroundCritterFamily1458.Frog,
            _ => default
        };
        return family != default;
    }

    private static bool IsEvilFish(NpcTypeId type) =>
        type == VanillaNpcIds.CorruptGoldfish || type == VanillaNpcIds.CrimsonGoldfish;
}
