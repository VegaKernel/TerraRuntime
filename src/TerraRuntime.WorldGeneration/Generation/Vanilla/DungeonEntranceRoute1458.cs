using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal readonly record struct DungeonPosition1458(double X, double Y)
{
    public DungeonPoint1458 Tile => new((int)X, (int)Y);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

internal readonly record struct DungeonEntranceSegment1458(
    DungeonPosition1458 Start, DungeonPosition1458 Target, int OverrideSteps, int Seed);

internal readonly record struct DungeonHallPlatform1458(DungeonPoint1458 Position, bool InAHallway, double PlacePotsChance);

/// <summary>Retains the double-precision interpolation and countdown of DungeonCrawler's precalculated connection.</summary>
internal sealed class DungeonEntranceRoute1458
{
    private readonly DungeonPosition1458 target;
    private readonly double distance;
    private bool started;
    public DungeonPosition1458 Current { get; private set; }
    public int Remaining { get; private set; }
    public bool Complete => started && Remaining <= 0;

    public DungeonEntranceRoute1458(DungeonPosition1458 start, DungeonPosition1458 target)
    {
        double dx = target.X - start.X, dy = target.Y - start.Y;
        distance = Math.Sqrt(dx * dx + dy * dy);
        if (!start.IsFinite || !target.IsFinite || start == default || target == default ||
            !double.IsFinite(distance) || distance <= 0 || distance > int.MaxValue)
            throw new InvalidOperationException("Invalid precalculated dungeon entrance route.");
        Current = start; this.target = target; Remaining = (int)distance;
    }

    public DungeonEntranceSegment1458 TakeNext(IWorldGenerationVanillaRandom random)
    {
        if (Complete) throw new InvalidOperationException("Dungeon entrance route is already complete.");
        int steps = random.Next(10, 30);
        if (steps > distance - Remaining) steps = Math.Max(1, (int)distance - Remaining);
        double amount = Remaining / distance;
        var end = new DungeonPosition1458(Current.X + (target.X - Current.X) * amount,
            Current.Y + (target.Y - Current.Y) * amount);
        var segment = new DungeonEntranceSegment1458(Current, end, steps, random.Next());
        Remaining -= steps; Current = end; started = true;
        return segment;
    }
}
