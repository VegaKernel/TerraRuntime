namespace TerraRuntime.Application;

internal sealed partial class RuntimeWorldClock
{
    // Main.ladyBugRainBoost is transient source state, deliberately absent from
    // SaveWorldHeader. NPC.LadyBugKilled adds1800 up to18000, even for gold bait.
    internal int LadyBugRainBoost { get; private set; }
    internal void ConsumeLadyBugBait() => LadyBugRainBoost = Math.Min(18_000, LadyBugRainBoost + 1_800);
}
