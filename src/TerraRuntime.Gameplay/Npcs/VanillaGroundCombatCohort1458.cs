using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Exact AI003 identities admitted to the retained source-ordered combat phase.
/// Membership supplies no natural spawn, event, projectile or loot ownership.
/// </summary>
public static class VanillaGroundCombatCohort1458
{
    public static bool IsSupported(NpcTypeId type, NpcNetId netId) =>
        IsNewDefinition(type, netId) ||
        netId.Value == type.Value && (type.Value == 77 ||
            VanillaGroundFighterNpcCatalog.IsOrdinaryMeleeCohort1458(type));

    /// <summary>The sixteen previous creation-only identities; positive77 is a stability control.</summary>
    public static bool IsNewDefinition(NpcTypeId type, NpcNetId netId) =>
        type.Value == 77 && netId.Value == -15 || netId.Value == type.Value &&
        type.Value is 185 or 385 or 482 or 508 or 580 or 582 or 590 or
            381 or 382 or 449 or 450 or 451 or 452 or 481 or 520;

    public static bool IsRanged(NpcTypeId type) =>
        type.Value is 381 or 382 or 449 or 450 or 451 or 452 or 481 or 520;

    public static bool IsDayDespawnCandidate(NpcTypeId type) =>
        VanillaGroundFighterNpcCatalog.IsOrdinaryMeleeDayDespawnCandidate1458(type) ||
        type.Value is 385 or 482 or 590 or 381 or 382 or 449 or 450 or 451 or 452 or 481 or 520;

    public static int Ai003AmbientBound(NpcTypeId type) => type.Value is
        77 or 590 or 449 or 450 or 451 or 452 or 481 ? 1000 :
        VanillaGroundFighterNpcCatalog.OrdinaryMeleeAi003AmbientBound1458(type);
}
