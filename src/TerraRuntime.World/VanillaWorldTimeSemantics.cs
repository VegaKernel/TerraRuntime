namespace TerraRuntime.World;

/// <summary>TerrariaServer 1.4.5.8 <c>Terraria.Enums.MoonPhase</c> identity.</summary>
public enum VanillaMoonPhase : byte
{
    Full = 0,
    ThreeQuartersAtLeft = 1,
    HalfAtLeft = 2,
    QuarterAtLeft = 3,
    Empty = 4,
    QuarterAtRight = 5,
    HalfAtRight = 6,
    ThreeQuartersAtRight = 7
}

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
