using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 AI_003 door-pressure policy facts. These ids are the exact source
/// membership of the restricted-reset branch plus the source type-specific progress/force-open exceptions.
/// The policy owns no tile mutation; type 26's destroy-door side effect is surfaced explicitly instead of being
/// silently treated as an ordinary open.
/// </summary>
public readonly record struct VanillaGroundFighterDoorPressureDecision(
    bool ResetProgress,
    int BonusProgress,
    bool ForceOpen,
    bool DestroyDoorInsteadOfOpen);

public static class VanillaGroundFighterDoorPressurePolicy
{
    private static readonly int[] RestrictedResetTypes =
    [
        3, 21, 44, 77, 132, 161, 167, 186, 187, 188, 189, 196, 197, 200, 201, 202, 203, 223,
        319, 320, 321, 322, 323, 324, 331, 332, 430, 449, 450, 451, 452, 481, 590, 635, 691
    ];

    public static VanillaGroundFighterDoorPressureDecision Resolve(
        NpcTypeId type,
        bool bloodMoonActive,
        bool getGoodWorld,
        bool graveyardRollSucceeded,
        bool targetInsideUnbreakableWalls)
    {
        bool restricted = Array.BinarySearch(RestrictedResetTypes, type.Value) >= 0;
        bool resetProgress =
            !targetInsideUnbreakableWalls &&
            !graveyardRollSucceeded &&
            (!bloodMoonActive || getGoodWorld) &&
            restricted;

        int bonusProgress = targetInsideUnbreakableWalls
            ? 6
            : type.Value switch
            {
                27 => 1,
                31 or 294 or 295 or 296 => 6,
                _ => 0
            };

        return new VanillaGroundFighterDoorPressureDecision(
            resetProgress,
            bonusProgress,
            ForceOpen: type.Value == 460,
            DestroyDoorInsteadOfOpen: type.Value == 26);
    }

    public static bool IsRestrictedResetType(NpcTypeId type) =>
        Array.BinarySearch(RestrictedResetTypes, type.Value) >= 0;
}
