using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application.Operations;

/// <summary>
/// Bounded TUI projection over the authoritative world-item snapshot boundary. The source store owns
/// synchronization; this reader keeps one fixed scratch buffer and emits only immutable grouped snapshots.
/// </summary>
internal sealed class LocalRuntimeWorldItemOperations : IWorldItemOperations
{
    private readonly IWorldItemSnapshotReader items;
    private readonly WorldItemSnapshot[] itemBuffer;
    private readonly GroupAccumulator[] groupBuffer;
    private readonly object gate = new();

    public LocalRuntimeWorldItemOperations(IWorldItemSnapshotReader items)
    {
        this.items = items ?? throw new ArgumentNullException(nameof(items));
        if (items.Capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(items));

        itemBuffer = new WorldItemSnapshot[items.Capacity];
        groupBuffer = new GroupAccumulator[items.Capacity];
    }

    public RuntimeWorldItemsSnapshot CaptureSnapshot()
    {
        lock (gate)
        {
            int count = items.CopyActive(itemBuffer);
            if ((uint)count > (uint)itemBuffer.Length)
                throw new InvalidOperationException("World-item reader returned more entries than its advertised capacity.");

            int groupCount = 0;
            for (int i = 0; i < count; i++)
            {
                WorldItemSnapshot item = itemBuffer[i];
                int groupIndex = FindGroup(groupCount, item.ItemNetId);
                if (groupIndex < 0)
                {
                    groupIndex = groupCount++;
                    groupBuffer[groupIndex] = new GroupAccumulator(item.ItemNetId);
                }

                groupBuffer[groupIndex].Add(in item);
            }

            var groups = new RuntimeWorldItemGroupSnapshot[groupCount];
            for (int i = 0; i < groupCount; i++)
            {
                groups[i] = groupBuffer[i].Capture();
                groupBuffer[i] = default;
            }

            Array.Sort(groups, static (left, right) =>
            {
                int byCount = right.DropCount.CompareTo(left.DropCount);
                return byCount != 0 ? byCount : left.ItemNetId.CompareTo(right.ItemNetId);
            });

            return new RuntimeWorldItemsSnapshot(
                ActiveItems: count,
                Groups: groups,
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    private int FindGroup(int groupCount, short itemNetId)
    {
        for (int i = 0; i < groupCount; i++)
        {
            if (groupBuffer[i].ItemNetId == itemNetId)
                return i;
        }

        return -1;
    }

    private struct GroupAccumulator(short itemNetId)
    {
        public short ItemNetId = itemNetId;
        public int DropCount;
        public long TotalStack;
        public int ReservedDrops;
        public int ShimmeredDrops;
        public short MaxStack;
        public double PositionXTotal;
        public double PositionYTotal;

        public void Add(in WorldItemSnapshot item)
        {
            DropCount++;
            TotalStack += item.Stack;
            if (item.OwnerPlayerId != byte.MaxValue || item.TimeToKeepReservation > 0)
                ReservedDrops++;
            if (item.Shimmered || item.ShimmerTime > 0f)
                ShimmeredDrops++;
            if (item.Stack > MaxStack)
                MaxStack = item.Stack;
            PositionXTotal += item.PositionX;
            PositionYTotal += item.PositionY;
        }

        public readonly RuntimeWorldItemGroupSnapshot Capture() =>
            new(
                ItemNetId,
                DropCount,
                TotalStack,
                ReservedDrops,
                ShimmeredDrops,
                MaxStack,
                DropCount == 0 ? 0f : (float)(PositionXTotal / DropCount),
                DropCount == 0 ? 0f : (float)(PositionYTotal / DropCount));
    }
}
