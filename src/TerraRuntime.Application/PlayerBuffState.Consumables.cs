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
            int destination = 0;
            for (int i = 0; i < Capacity - 1; i++)
                if (copy.types[i] != VanillaBuffIds.None && copy.durations[i] != 0)
                {
                    if (destination < i)
                    {
                        copy.types[destination] = copy.types[i];
                        copy.durations[destination] = copy.durations[i];
                        copy.types[i] = VanillaBuffIds.None;
                        copy.durations[i] = 0;
                    }
                    destination++;
                }
        }
        if (!incoming.SequenceEqual(copy.CaptureTypes()))
        {
            ambiguous = true;
            return true;
        }
        next = copy;
        return true;
    }

    internal PlayerBuffState Clone()
    {
        var copy = new PlayerBuffState { count = count };
        types.CopyTo(copy.types, 0);
        durations.CopyTo(copy.durations, 0);
        return copy;
    }

    internal bool HasSameSlots(PlayerBuffState other) =>
        types.AsSpan().SequenceEqual(other.types) && durations.AsSpan().SequenceEqual(other.durations);

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

    // Only the selected healing, mana and six source-backed buff additions are admitted here. The caller stages
    // this mutation on Clone; a full debuff table refuses insertion without refusing the heal.
    internal bool TryApplySelectedConsumable(BuffTypeId type, int duration, bool immune)
    {
        if (immune || duration <= 0 || type.Value is not (21 or 94 or 5 or 16 or 112 or 114 or 115 or 117)) return false;
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
                    count = Capacity;
                    return true;
                }

            types[removable] = VanillaBuffIds.None;
            durations[removable] = 0;
            int destination = 0;
            // Source Player.DelBuff compacts 0..42, leaving slot43 in place.
            for (int i = 0; i < Capacity - 1; i++)
                if (types[i] != VanillaBuffIds.None && durations[i] != 0)
                {
                    if (destination < i)
                    {
                        types[destination] = types[i];
                        durations[destination] = durations[i];
                        types[i] = VanillaBuffIds.None;
                        durations[i] = 0;
                    }
                    destination++;
                }
        }
        return false;
    }
}
