namespace TerraRuntime.Gameplay.Tiles;

[Flags]
public enum MinecartTrackNeighbors1458 : byte
{
    None = 0,
    UpperLeft = 1,
    Left = 2,
    LowerLeft = 4,
    UpperRight = 8,
    Right = 16,
    LowerRight = 32,
    All = UpperLeft | Left | LowerLeft | UpperRight | Right | LowerRight
}

public readonly record struct MinecartTrackFrameInput1458(short Front, short Back,
    MinecartTrackNeighbors1458 Neighbors);

public readonly record struct MinecartTrackFrameOutcome1458(short Front, short Back,
    bool SourceResult, bool Changed, bool InvokeFailedKill);

/// <summary>
/// Pure source FrameTrack selection over independently captured Initialize tables.
/// It neither edits terrain nor advances RNG. The caller owns the failed-KillTile
/// dust/framing call, source role, target admission and publication.
/// </summary>
public static partial class VanillaMinecartTrackFrame1458
{
    public const ushort TileType = 314;
    public const int TrackFrameCount = 36;

    public static bool TryPlan(in MinecartTrackFrameInput1458 input, bool pound, bool mute,
        out MinecartTrackFrameOutcome1458 outcome)
    {
        outcome = default;
        if ((input.Neighbors & ~MinecartTrackNeighbors1458.All) != 0) return false;
        var options = SwitchOptions[(byte)input.Neighbors];
        if (options is null)
        {
            outcome = pound ? Unchanged(in input, false) : Finish(in input, 0, -1, false, false);
            return true;
        }
        int kind = (uint)input.Front < TrackFrameCount ? TrackKinds[input.Front] : 0;
        int frontIndex = Array.IndexOf(options, input.Front);
        int backIndex = Array.IndexOf(options, input.Back);
        if (!pound)
        {
            int firstEnd = -1, firstThrough = -1;
            bool retainedEnd = false;
            for (int index = 0; index < options.Length; index++)
            {
                int candidate = options[index];
                if (TrackKinds[candidate] != kind) continue;
                bool end = IsEnd(candidate);
                if (candidate == input.Front) retainedEnd = end;
                if (end && firstEnd < 0) firstEnd = index;
                if (!end && firstThrough < 0) firstThrough = index;
            }
            // A front with a different kind is not a retained source candidate.
            if (frontIndex >= 0 && TrackKinds[options[frontIndex]] != kind) frontIndex = -1;
            if (firstThrough >= 0)
            {
                if (frontIndex < 0 || retainedEnd) frontIndex = firstThrough;
            }
            else
            {
                if (frontIndex < 0)
                {
                    if (kind is 1 or 2) { outcome = Unchanged(in input, false); return true; }
                    frontIndex = firstEnd;
                }
                backIndex = -1;
            }
        }
        else
        {
            int ends = 0, through = 0;
            foreach (int candidate in options)
                if (TrackKinds[candidate] == kind)
                {
                    if (IsEnd(candidate)) ends++; else through++;
                }
            if (through < 2 && ends < 2) { outcome = Unchanged(in input, false); return true; }
            bool selectEnd = through == 0;
            bool selectedBack = false;
            if (!selectEnd)
            {
                // The incoming frame was produced by source framing; a through
                // switch must have its current front in this topology's list.
                if (frontIndex < 0) return false;
                for (int index = backIndex + 1; index < options.Length; index++)
                {
                    int candidate = options[index], front = options[frontIndex];
                    if (TrackKinds[candidate] == kind && !IsEnd(candidate) &&
                        (LeftConnections[candidate] != LeftConnections[front] ||
                         RightConnections[candidate] != RightConnections[front]))
                    { backIndex = index; selectedBack = true; break; }
                }
                if (!selectedBack) backIndex = -1;
            }
            if (!selectedBack)
            {
                int selected = -1;
                for (int offset = 1; offset <= options.Length; offset++)
                {
                    int index = (frontIndex + offset) % options.Length;
                    int candidate = options[index];
                    if (TrackKinds[candidate] == kind && IsEnd(candidate) == selectEnd)
                    { selected = index; break; }
                }
                if (selected < 0) return false;
                frontIndex = selected;
            }
        }
        short finalFront = frontIndex < 0 ? (short)0 : (short)options[frontIndex];
        short finalBack = backIndex < 0 ? (short)-1 : (short)options[backIndex];
        outcome = Finish(in input, finalFront, finalBack, true, pound && !mute);
        return true;
    }

    private static bool IsEnd(int frame) => LeftConnections[frame] < 0 || RightConnections[frame] < 0;
    private static MinecartTrackFrameOutcome1458 Unchanged(in MinecartTrackFrameInput1458 input, bool result) =>
        new(input.Front, input.Back, result, false, false);
    private static MinecartTrackFrameOutcome1458 Finish(in MinecartTrackFrameInput1458 input,
        short front, short back, bool result, bool failedKill)
    {
        bool changed = front != input.Front || back != input.Back;
        return new(front, back, result, changed, failedKill && changed);
    }
}
