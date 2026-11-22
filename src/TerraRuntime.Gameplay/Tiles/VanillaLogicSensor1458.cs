namespace TerraRuntime.Gameplay.Tiles;

public readonly record struct LogicSensorStep1458(bool IsOn, int CountedData, bool Changed, bool TripWire, bool BlockPlayers);

/// <summary>TELogicSensor state transitions; physical mutation and pulses belong to the world owner.</summary>
public static class VanillaLogicSensor1458
{
    public static bool TryStep(byte check, bool on, int counted, bool day, bool playerAbove,
        byte liquidAmount, bool lava, bool honey, bool shimmer, out LogicSensorStep1458 step)
    {
        step = default;
        if (check is < 1 or > 7 || counted is < 0 or > 15) return false;
        bool state = check switch
        {
            1 => day,
            2 => !day,
            3 => playerAbove,
            4 => liquidAmount > 0 && !lava && !honey && !shimmer,
            5 => liquidAmount > 0 && lava,
            6 => liquidAmount > 0 && honey,
            _ => liquidAmount > 0
        };
        // Source retains the countdown through intervening wet updates; it is not a fresh debounce timer.
        if (check >= 4 && !state && on)
        {
            counted = counted == 0 ? 15 : counted - 1;
            state = counted > 0;
        }
        bool changed = on != state;
        step = new(state, counted, changed, changed && (check >= 3 || state), check == 3);
        return true;
    }

    public static bool PlayerAbove(int tileX, int tileY, float x, float y, int width, int height) =>
        float.IsFinite(x) && float.IsFinite(y) && x is >= -2147483000f and <= 2147483000f &&
        y is >= -2147483000f and <= 2147483000f && tileX is >= 0 and <= short.MaxValue &&
        tileY is >= 0 and <= short.MaxValue && width is > 0 and <= 512 && height is > 0 and <= 512 &&
        (int)x < tileX * 16 + 49 && (int)x + width > tileX * 16 - 33 &&
        (int)y < tileY * 16 + 1 && (int)y + height > tileY * 16 - 161;
}
