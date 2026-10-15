using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Application;

internal sealed partial class PlayerBuffState
{
    // All slot origins come from actual clock DelBuff operations, never a client report.
    // The semantic token rejects even equal-looking nonclock replacements. Validate at most
    //44 slots rather than iterating a client- or world-selected number of elapsed ticks.
    internal bool IsFoodClockContinuation(PlayerBuffState current)
    {
        if (HasSameSlots(current)) return true;
        if (!ReferenceEquals(foodClockLineage, current.foodClockLineage) ||
            lastOwnedFoodTick == long.MinValue || current.lastOwnedFoodTick < lastOwnedFoodTick)
            return false;
        long elapsed = current.lastOwnedFoodTick - lastOwnedFoodTick;
        if (elapsed <= 0) return false;
        Span<bool> retained = stackalloc bool[Capacity];
        bool finalEffect = false;
        for (int i = 0; i < Capacity; i++)
            if (ownedFood[i] && durations[i] > 0 && elapsed <= durations[i]) finalEffect = true;
        if (current.ownedFoodEffectAtTick != finalEffect) return false;
        for (int destination = 0; destination < Capacity; destination++)
        {
            byte origin = current.foodClockOrigins[destination];
            if (origin == byte.MaxValue)
            {
                if (current.types[destination] != default || current.durations[destination] != 0 ||
                    current.ownedFood[destination]) return false;
                continue;
            }
            int source = Array.IndexOf(foodClockOrigins, origin);
            if (source < 0 || retained[source]) return false;
            retained[source] = true;
            int remaining = ownedFood[source] ? (int)Math.Max(0L, durations[source] - elapsed) : durations[source];
            if (current.types[destination] != types[source] || current.durations[destination] != remaining ||
                current.ownedFood[destination] != ownedFood[source] ||
                (source == Capacity - 1 && destination != source)) return false;
        }
        for (int i = 0; i < Capacity; i++)
            if (!retained[i] && types[i] != default &&
                (durations[i] > 0 && (!ownedFood[i] || durations[i] > elapsed))) return false;
        return true;
    }
}
