namespace TerraRuntime.World;

/// <summary>Bounded single-writer wake queue. Overflow requests a resumable scan instead of losing work.</summary>
public sealed class WorldFallingBlockUpdates
{
    public const int Capacity = 4096;
    private readonly Queue<int> pending = new(Capacity);
    private readonly HashSet<int> queued = new(Capacity);
    private int scan;
    private bool scanning = true, rescan;
    public int Count => pending.Count;
    public void Wake(int index)
    {
        if (queued.Contains(index)) return;
        if (pending.Count == Capacity) { rescan = true; return; }
        pending.Enqueue(index); queued.Add(index);
    }
    public bool TryTake(int tileCount, out int index)
    {
        if (pending.TryDequeue(out index)) { queued.Remove(index); return true; }
        if (!scanning) { if (!rescan) return false; scanning = true; rescan = false; }
        index = scan++;
        if (scan == tileCount) { scan = 0; scanning = false; }
        return true;
    }
}
