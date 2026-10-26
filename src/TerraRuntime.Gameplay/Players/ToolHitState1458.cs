namespace TerraRuntime.Gameplay.Players;

public readonly record struct ToolHitEntry1458(int X, int Y, int Damage, int Kind, int TimeToLive);

/// <summary>Immutable gameplay portion of the source 501-entry mining cache.</summary>
public sealed class ToolHitState1458
{
    public const int SlotCount = 501;
    private readonly ToolHitEntry1458[] entries;
    private readonly int[] order;
    public int BufferLocation { get; }
    public static ToolHitState1458 Constructor { get; } = CreateConstructor();

    private ToolHitState1458(ToolHitEntry1458[] entries, int[] order, int buffer)
    {
        this.entries = entries;
        this.order = order;
        BufferLocation = buffer;
    }

    private static ToolHitState1458 CreateConstructor()
    {
        var order = new int[SlotCount];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        return new(new ToolHitEntry1458[SlotCount], order, 0);
    }

    public ToolHitEntry1458 GetEntry(int index) => entries[index];
    public int GetOrder(int index) => order[index];

    public static bool TryCreateSnapshot(ReadOnlySpan<ToolHitEntry1458> entries,
        ReadOnlySpan<int> order, int buffer, out ToolHitState1458? state)
    {
        state = null;
        if (entries.Length != SlotCount || order.Length != SlotCount || (uint)buffer >= SlotCount)
            return false;
        Span<bool> seen = stackalloc bool[SlotCount];
        seen.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            var entry = entries[i];
            int slot = order[i];
            if ((uint)slot >= SlotCount || seen[slot] || entry.Kind is < 0 or > 2 ||
                entry.X < 0 || entry.Y < 0 || entry.Damage < 0 || entry.TimeToLive is < 0 or > 60)
                return false;
            seen[slot] = true;
        }
        state = new(entries.ToArray(), order.ToArray(), buffer);
        return true;
    }

    public int Find(int x, int y, int kind)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            int slot = order[i];
            var entry = entries[slot];
            if (entry.Kind == kind)
            {
                if (entry.X == x && entry.Y == y) return slot;
            }
            else if (i != 0 && entry.Kind == 0) break;
        }
        return -1;
    }

    public bool TryHitObject(int x, int y, int kind, out ToolHitState1458 after, out int slot)
    {
        after = this;
        slot = -1;
        if (x < 0 || y < 0 || kind is < 1 or > 2) return false;
        slot = Find(x, y, kind);
        if (slot >= 0) return true;
        slot = BufferLocation;
        var copy = (ToolHitEntry1458[])entries.Clone();
        copy[slot] = copy[slot] with { X = x, Y = y, Kind = kind };
        after = new(copy, order, BufferLocation);
        return true;
    }

    // RandomSamples is one source Main.rand sample for each actual AddDamage,
    // including zero damage on an existing non-buffer entry. Private crack-style
    // RNG and animation presentation are outside this gameplay snapshot.
    public bool TryAddDamage(int slot, int amount, bool updateAmount,
        out ToolHitState1458 after, out int damage, out int randomSamples)
    {
        after = this;
        damage = randomSamples = 0;
        if ((uint)slot >= SlotCount) return true;
        if (slot == BufferLocation && amount == 0) return true;
        long sum = (long)entries[slot].Damage + amount;
        if (sum < 0 || sum > int.MaxValue) return false;
        damage = (int)sum;
        if (!updateAmount) return true;
        var copy = (ToolHitEntry1458[])entries.Clone();
        var sorted = (int[])order.Clone();
        copy[slot] = copy[slot] with { Damage = damage, TimeToLive = 60 };
        int buffer = BufferLocation;
        if (slot == buffer)
        {
            buffer = sorted[SlotCount - 1];
            if (slot != buffer) copy[buffer] = default;
            Array.Copy(sorted, 0, sorted, 1, SlotCount - 1);
            sorted[0] = buffer;
        }
        else
        {
            int position = Array.IndexOf(sorted, slot);
            while (position > 1)
            {
                sorted[position] = sorted[position - 1];
                position--;
            }
            sorted[1] = slot;
        }
        randomSamples = 1;
        after = new(copy, sorted, buffer);
        return true;
    }

    public ToolHitState1458 Clear(int slot)
    {
        if ((uint)slot >= SlotCount) return this;
        var copy = (ToolHitEntry1458[])entries.Clone();
        var sorted = (int[])order.Clone();
        copy[slot] = default;
        int position = Array.IndexOf(sorted, slot);
        Array.Copy(sorted, position + 1, sorted, position, SlotCount - position - 1);
        sorted[SlotCount - 1] = slot;
        return new(copy, sorted, BufferLocation);
    }

    // Presence is an indexed input captured by the caller from the exact world
    // read set: tile active for kind 1, nonzero wall for kind 2. No world query
    // callback is invoked while producing the immutable successor.
    public bool TryPrune(ReadOnlySpan<bool> present, out ToolHitState1458 after)
    {
        after = this;
        if (present.Length != SlotCount) return false;
        var copy = (ToolHitEntry1458[])entries.Clone();
        bool cleared = false;
        for (int i = 0; i < SlotCount; i++)
        {
            var entry = copy[i];
            if (entry.Kind == 0) continue;
            int ttl = entry.TimeToLive - 1;
            int damage = entry.Damage;
            if (ttl < 12) damage -= 10;
            else if (ttl < 24) damage -= 7;
            else if (ttl < 36) damage -= 5;
            else if (ttl < 48) damage -= 2;
            if (ttl <= 0 || damage < 0 || !present[i])
            {
                copy[i] = default;
                cleared = true;
            }
            else copy[i] = entry with { TimeToLive = ttl, Damage = damage };
        }
        int[] sorted = order;
        if (cleared)
        {
            sorted = (int[])order.Clone();
            int destination = 1;
            for (int i = 1; i < SlotCount; i++)
                if (copy[sorted[i]].Kind != 0)
                {
                    int slot = sorted[i];
                    Array.Copy(sorted, destination, sorted, destination + 1, i - destination);
                    sorted[destination++] = slot;
                }
        }
        after = new(copy, sorted, BufferLocation);
        return true;
    }

    public ToolHitState1458 UpdatePosition(int slot, int x, int y)
    {
        if ((uint)slot >= SlotCount) return this;
        var copy = (ToolHitEntry1458[])entries.Clone();
        copy[slot] = copy[slot] with { X = x, Y = y };
        return new(copy, order, BufferLocation);
    }

    public bool TryClearAndPrune(int x, int y, int kind, ReadOnlySpan<bool> present,
        out ToolHitState1458 after)
    {
        after = this;
        if (present.Length != SlotCount) return false;
        int slot = Find(x, y, kind);
        return slot < 0 || Clear(slot).TryPrune(present, out after);
    }

    public bool TryClearThisTile(int x, int y, ReadOnlySpan<bool> present,
        out ToolHitState1458 after)
    {
        after = this;
        if (present.Length != SlotCount) return false;
        for (int i = 0; i < SlotCount; i++)
        {
            var entry = after.entries[after.order[i]];
            if (entry.X != x || entry.Y != y) continue;
            // The original passes the order POSITION i to Clear, rather than
            // the matched entry id. Preserve that observable source behavior.
            if (!after.Clear(i).TryPrune(present, out after)) return false;
        }
        return true;
    }
}
