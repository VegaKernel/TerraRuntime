using TerraRuntime.Contracts.Gameplay;
namespace TerraRuntime.Gameplay.Items;

public readonly record struct WorldItemAllocationPlayer1458(byte Slot, float X, float Y, int Width, int Height);
public readonly record struct WorldItemAllocationState1458(bool Active, bool Blocked, int Type, int Stack, byte Prefix,
    float X, float Y, int Age, int ReuseTicks, byte Owner, float Shimmer, ulong Generation);
public readonly record struct WorldItemStackTransfer1458(short Source, short Destination, ulong SourceGeneration,
    ulong DestinationGeneration, int DistanceOrder, int PreservationOrder, int Distance);

/// <summary>1.4.5.8 Item.PickAnItemSlotToSpawnItemOn and EmergencyStacking ranking, without runtime mutation.</summary>
public static class VanillaWorldItemAllocation1458
{
    public const int Capacity = 400;
    public const int PickupReplacementAge = 1200;
    public const int EmergencyThreshold = 360;
    public const int InitialTransfers = 20;
    public const int MaximumTransferDistance = 2400;
    public const int AgeCeiling = int.MaxValue - 100;

    public static int FindFreeOrPickup(ReadOnlySpan<WorldItemAllocationState1458> items, out bool emergency)
    {
        int free = Capacity, pickup = -1, oldest = PickupReplacementAge;
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item.Blocked) continue;
            if (!item.Active && item.ReuseTicks == 0) { free = i; break; }
            if (item.Active && VanillaWorldItemAllocationCatalog1458.TryGet(item.Type, out var facts) && facts.Pickup && item.Age > oldest)
            { oldest = item.Age; pickup = i; }
        }
        emergency = free >= EmergencyThreshold;
        if (emergency && pickup >= 0) { emergency = false; return pickup; }
        return free;
    }

    public static int FindOldest(ReadOnlySpan<WorldItemAllocationState1458> items)
    {
        int chosen = Capacity, best = 0;
        for (int i = 0; i < items.Length; i++)
            if (!items[i].Blocked && items[i].ReuseTicks == 0 && items[i].Age > best)
            { best = items[i].Age; chosen = i; }
        if (chosen != Capacity) return chosen;
        for (int i = 0; i < items.Length; i++)
            if (!items[i].Blocked && (long)items[i].Age - items[i].ReuseTicks > best)
            { best = items[i].Age - items[i].ReuseTicks; chosen = i; }
        return chosen;
    }

    public static bool TryRankTransfers(ReadOnlySpan<WorldItemAllocationState1458> items,
        ReadOnlySpan<WorldItemAllocationPlayer1458> players, Span<WorldItemStackTransfer1458> transfers,
        int previousPending, out int count)
    {
        count = 0;
        if (items.Length != Capacity || transfers.Length < Capacity || previousPending < 0 || previousPending > Capacity) return false;
        Span<bool> visible = stackalloc bool[Capacity];
        Span<bool> eligible = stackalloc bool[Capacity];
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!item.Active || item.Blocked || item.ReuseTicks != 0 || item.Shimmer != 0f) continue;
            if (!VanillaWorldItemAllocationCatalog1458.TryGet(item.Type, out var facts)) return false;
            eligible[i] = item.Stack > 0 && item.Stack < facts.MaximumStack;
            int x = (int)(item.X + 8f), y = (int)(item.Y + 8f);
            foreach (var player in players)
            {
                int px = (int)(player.X + player.Width * 0.5f), py = (int)(player.Y + player.Height * 0.5f);
                if (x >= px - 1160 && x < px + 1160 && y >= py - 800 && y < py + 800) { visible[i] = true; break; }
            }
        }
        int limit = Math.Min(Capacity, Math.Max(InitialTransfers, previousPending + 1));
        for (short source = 0; source < Capacity; source++)
        {
            if (!eligible[source]) continue;
            var item = items[source]; var best = default(WorldItemStackTransfer1458); bool found = false;
            for (short target = 0; target < Capacity; target++)
            {
                if (source == target || !eligible[target]) continue;
                var other = items[target];
                if (item.Type != other.Type || item.Prefix != other.Prefix || Preferred(source, target, items, visible)) continue;
                float dx = item.X - other.X, dy = item.Y - other.Y;
                if (!float.IsFinite(dx) || !float.IsFinite(dy) || dx <= int.MinValue || dx >= int.MaxValue || dy <= int.MinValue || dy >= int.MaxValue) return false;
                long distance = Math.Abs((long)(int)dx) + Math.Abs((long)(int)dy);
                if (distance > MaximumTransferDistance) continue;
                VanillaWorldItemAllocationCatalog1458.TryGet(item.Type, out var facts);
                (int step, int preservation) = item.Type switch
                {
                    73 or 74 or 3822 => (40, 6),
                    _ when facts.Equipment => (160, 5),
                    72 => (160, 4),
                    71 => (160, 3),
                    75 => (640, 2),
                    _ => (160, 1)
                };
                var candidate = new WorldItemStackTransfer1458(source, target, item.Generation, other.Generation,
                    (int)distance / step + (visible[source] ? 3 : 0), preservation, (int)distance);
                if (!found || Compare(candidate, best) < 0) { best = candidate; found = true; }
            }
            if (!found) continue;
            int at = 0;
            while (at < count && Compare(best, transfers[at]) >= 0) at++;
            if (at >= limit) continue;
            int end = Math.Min(count, limit - 1);
            for (int index = end; index > at; index--) transfers[index] = transfers[index - 1];
            transfers[at] = best; count = Math.Min(count + 1, limit);
        }
        return true;
    }

    private static bool Preferred(int left, int right, ReadOnlySpan<WorldItemAllocationState1458> items, ReadOnlySpan<bool> visible)
    {
        if (visible[left] != visible[right]) return visible[left];
        if (items[left].Age != items[right].Age) return items[left].Age < items[right].Age;
        return left < right;
    }
    private static int Compare(in WorldItemStackTransfer1458 left, in WorldItemStackTransfer1458 right)
    {
        int value = left.DistanceOrder.CompareTo(right.DistanceOrder);
        if (value == 0) value = left.PreservationOrder.CompareTo(right.PreservationOrder);
        return value == 0 ? left.Distance.CompareTo(right.Distance) : value;
    }
}
