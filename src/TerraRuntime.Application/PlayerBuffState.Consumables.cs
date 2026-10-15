using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Application;

internal sealed partial class PlayerBuffState
{
    internal bool TryPrepareSourceSubsetRemoval(ReadOnlySpan<BuffTypeId> incoming,
        out PlayerBuffState? next, out bool ambiguous)
    {
        next = null;
        ambiguous = false;
        var active = CaptureTypes();
        if (incoming.Length >= active.Length) return false;
        int match = 0;
        foreach (var type in active)
            if (match < incoming.Length && incoming[match] == type) match++;
        if (match != incoming.Length) return false;

        // A type-only snapshot cannot identify which duplicate slot survived.
        // Refuse this explanation instead of choosing an arbitrary duration.
        foreach (var type in active)
        {
            int originalOccurrences = 0;
            int retainedOccurrences = 0;
            foreach (var original in active) if (original == type) originalOccurrences++;
            foreach (var retained in incoming) if (retained == type) retainedOccurrences++;
            if (retainedOccurrences > 0 && retainedOccurrences < originalOccurrences)
            {
                ambiguous = true;
                return true;
            }
        }

        var copy = Clone();
        // Inspect the current copy after every removal: DelBuff may compact
        // pre-existing holes. It compacts only 0..42; the final slot stays in place.
        for (int removed = Capacity - 1; removed >= 0; removed--)
        {
            if (copy.types[removed] == VanillaBuffIds.None || copy.durations[removed] <= 0) continue;
            bool retained = false;
            foreach (var type in incoming) if (type == copy.types[removed]) retained = true;
            if (retained) continue;
            copy.types[removed] = VanillaBuffIds.None;
            copy.durations[removed] = 0;
            copy.ownedFood[removed] = false;
            int destination = 0;
            for (int i = 0; i < Capacity - 1; i++)
                if (copy.types[i] != VanillaBuffIds.None && copy.durations[i] != 0)
                {
                    if (destination < i)
                    {
                        copy.types[destination] = copy.types[i];
                        copy.durations[destination] = copy.durations[i];
                        copy.ownedFood[destination] = copy.ownedFood[i];
                        copy.types[i] = VanillaBuffIds.None;
                        copy.durations[i] = 0;
                        copy.ownedFood[i] = false;
                    }
                    destination++;
                }
        }
        if (!incoming.SequenceEqual(copy.CaptureTypes()))
        {
            ambiguous = true;
            return true;
        }
        copy.InvalidateFoodClockLineage();
        next = copy;
        return true;
    }

    internal PlayerBuffState Clone()
    {
        var copy = new PlayerBuffState { count = count, lastOwnedFoodTick = lastOwnedFoodTick, ownedFoodEffectAtTick = ownedFoodEffectAtTick, foodClockLineage = foodClockLineage };
        types.CopyTo(copy.types, 0);
        durations.CopyTo(copy.durations, 0);
        ownedFood.CopyTo(copy.ownedFood, 0);
        foodClockOrigins.CopyTo(copy.foodClockOrigins, 0);
        return copy;
    }

    internal bool HasSameSlots(PlayerBuffState other) =>
        types.AsSpan().SequenceEqual(other.types) && durations.AsSpan().SequenceEqual(other.durations) &&
        ownedFood.AsSpan().SequenceEqual(other.ownedFood) &&
        (!(HasOwnedFoodSlots || other.HasOwnedFoodSlots) || ReferenceEquals(foodClockLineage, other.foodClockLineage)) &&
        foodClockOrigins.AsSpan().SequenceEqual(other.foodClockOrigins) &&
        lastOwnedFoodTick == other.lastOwnedFoodTick && ownedFoodEffectAtTick == other.ownedFoodEffectAtTick;

    internal void CaptureSlots(out BuffTypeId[] slotTypes, out int[] slotDurations)
    {
        slotTypes = (BuffTypeId[])types.Clone();
        slotDurations = (int[])durations.Clone();
    }

    internal static PlayerBuffState FromSlots(ReadOnlySpan<BuffTypeId> slotTypes, ReadOnlySpan<int> slotDurations)
    {
        if (slotTypes.Length != Capacity || slotDurations.Length != Capacity)
            throw new ArgumentException("A source player buff snapshot contains exactly 44 slots.");
        for (int i = 0; i < Capacity; i++)
            if (slotDurations[i] < 0 || (slotTypes[i] != VanillaBuffIds.None &&
                !VanillaBuffIds.TryCreate(slotTypes[i].Value, out _)))
                throw new ArgumentException("Invalid source player buff slot.");
        var state = new PlayerBuffState { count = Capacity };
        slotTypes.CopyTo(state.types);
        slotDurations.CopyTo(state.durations);
        return state;
    }

    internal int GetLastActiveDuration(BuffTypeId type)
    {
        for (int i = Capacity - 1; i >= 0; i--)
            if (types[i] == type && durations[i] > 0) return durations[i];
        return 0;
    }

    // Only the selected healing, mana, six source-backed buff potions and food are admitted here. The caller stages
    // this mutation on Clone; a full debuff table refuses insertion without refusing the heal.
    internal bool TryApplySelectedConsumable(BuffTypeId type, int duration, bool immune)
    {
        bool applied = TryApplySelectedConsumableCore(type, duration, immune);
        if (applied) InvalidateFoodClockLineage();
        return applied;
    }

    private bool TryApplySelectedConsumableCore(BuffTypeId type, int duration, bool immune)
    {
        if (immune || duration <= 0 || type.Value is not (21 or 94 or 5 or 16 or 112 or 114 or 115 or 117 or 26 or 206 or 207)) return false;
        // Player.AddBuff removes fed-state entries before looking for an existing timer.
        // Forward iteration deliberately follows original DelBuff compaction, including slot43.
        if (type.Value is 26 or 206 or 207)
            for (int i = 0; i < Capacity; i++)
                if (types[i].Value is 26 or 206 or 207 or 332 or 333 or 334)
                    RemoveFedStateSlot(i);
        for (int i = 0; i < Capacity; i++)
            if (types[i] == type)
            {
                if (type.Value == 94)
                {
                    // Imported integer-overflow inputs are outside this source-owned offer.
                    if (durations[i] > int.MaxValue - duration) return false;
                    durations[i] = Math.Min(durations[i] + duration, 600);
                }
                else durations[i] = Math.Max(durations[i], duration);
                if (type.Value is 26 or 206 or 207) ownedFood[i] = true;
                return true;
            }

        for (int attempt = 0; attempt < Capacity; attempt++)
        {
            int removable = 0;
            while (removable < Capacity && VanillaBuffDefinitionCatalog.IsDebuff(types[removable])) removable++;
            if (removable == Capacity) return false;
            for (int i = removable; i < Capacity; i++)
                if (types[i] == VanillaBuffIds.None)
                {
                    types[i] = type;
                    durations[i] = duration;
                    ownedFood[i] = type.Value is 26 or 206 or 207;
                    count = Capacity;
                    return true;
                }

            types[removable] = VanillaBuffIds.None;
            durations[removable] = 0;
            ownedFood[removable] = false;
            int destination = 0;
            // Source Player.DelBuff compacts 0..42, leaving slot43 in place.
            for (int i = 0; i < Capacity - 1; i++)
                if (types[i] != VanillaBuffIds.None && durations[i] != 0)
                {
                    if (destination < i)
                    {
                        types[destination] = types[i];
                        durations[destination] = durations[i];
                        ownedFood[destination] = ownedFood[i];
                        types[i] = VanillaBuffIds.None;
                        durations[i] = 0;
                        ownedFood[i] = false;
                    }
                    destination++;
                }
        }
        return false;
    }
    private void RemoveFedStateSlot(int slot)
    {
        types[slot] = VanillaBuffIds.None;
        durations[slot] = 0;
        ownedFood[slot] = false;
        foodClockOrigins[slot] = byte.MaxValue;
        int destination = 0;
        for (int i = 0; i < Capacity - 1; i++)
            if (types[i] != VanillaBuffIds.None && durations[i] != 0)
            {
                if (destination < i)
                {
                    types[destination] = types[i];
                    durations[destination] = durations[i];
                    ownedFood[destination] = ownedFood[i];
                    foodClockOrigins[destination] = foodClockOrigins[i];
                    foodClockOrigins[i] = byte.MaxValue;
                    types[i] = VanillaBuffIds.None;
                    durations[i] = 0;
                    ownedFood[i] = false;
                }
                destination++;
            }
    }

    // New paid food owns a local-style budget in authoritative world updates.
    // The other remote buff durations retain their established presence-only policy.
    internal bool AdvanceOwnedFoodClock(long tick, bool compactExpired)
    {
        if (!HasOwnedFoodSlots || lastOwnedFoodTick == tick) return false;
        lastOwnedFoodTick = tick;
        ownedFoodEffectAtTick = HasOwnedFood;
        bool expired = false;
        for (int i = 0; i < Capacity; i++)
            if (ownedFood[i] && durations[i] > 0 && --durations[i] == 0) expired = true;
        if (compactExpired)
            for (int i = 0; i < Capacity; i++)
                if (ownedFood[i] && durations[i] <= 0) RemoveFedStateSlot(i);
        return expired;
    }

    internal void StampOwnedFoodGrant(long tick)
    {
        if (lastOwnedFoodTick == tick) return;
        lastOwnedFoodTick = tick;
        ownedFoodEffectAtTick = false;
    }

    internal void ClearOwnedFood()
    {
        InvalidateFoodClockLineage();
        for (int i = Capacity - 1; i >= 0; i--)
            if (ownedFood[i]) RemoveFedStateSlot(i);
        ownedFoodEffectAtTick = false;
    }

}
