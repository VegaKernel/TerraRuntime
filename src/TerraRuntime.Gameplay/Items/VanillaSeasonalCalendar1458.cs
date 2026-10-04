using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Main.checkHalloween/checkXMas date predicates; the caller owns local date and forced flags.</summary>
public static class VanillaSeasonalCalendar1458
{
    public static bool IsHalloween(DateTime localDate, bool forceHalloween) => forceHalloween ||
        (localDate.Month == 10 && localDate.Day >= 10) || (localDate.Month == 11 && localDate.Day <= 1);

    public static bool IsChristmas(DateTime localDate, bool forceChristmas) => forceChristmas ||
        (localDate.Month == 12 && localDate.Day >= 15);
}
