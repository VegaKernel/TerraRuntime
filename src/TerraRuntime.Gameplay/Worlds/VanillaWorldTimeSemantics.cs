using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Worlds;

public static class VanillaMoonPhases
{
    public const int Count = 8;

    public static bool TryCreate(byte rawValue, out VanillaMoonPhase phase)
    {
        if (rawValue >= Count)
        {
            phase = default;
            return false;
        }

        phase = (VanillaMoonPhase)rawValue;
        return true;
    }

    public static VanillaMoonPhase Next(VanillaMoonPhase phase)
    {
        if (!Enum.IsDefined(phase))
            throw new ArgumentOutOfRangeException(nameof(phase));

        return phase == VanillaMoonPhase.ThreeQuartersAtRight
            ? VanillaMoonPhase.Full
            : phase + 1;
    }
}
