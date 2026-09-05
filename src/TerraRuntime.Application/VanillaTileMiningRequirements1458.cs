using TerraRuntime.Core.Worlds;

namespace TerraRuntime.Application;

/// <summary>
/// Source-pinned minimum pick powers for ordinary single-tile blocks admitted by the
/// current clean-room mining surface. This is intentionally conservative: unsupported
/// frame-important and structure tiles remain outside the simple kill path entirely.
/// </summary>
internal static class VanillaTileMiningRequirements1458
{
    public static bool CanMine(TileTypeId tileType, short pickPower)
    {
        int type = tileType.Value;
        int required = type switch
        {
            // Ebonstone / Pearlstone / Crimstone.
            25 or 117 or 203 => 65,
            // Chlorophyte ore.
            234 => 200,
            // Lihzahrd brick is post-Golem pickaxe tier in vanilla.
            226 => 210,
            _ => 0
        };

        return pickPower >= required;
    }
}
