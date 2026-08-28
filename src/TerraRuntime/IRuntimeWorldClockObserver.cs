namespace TerraRuntime;

/// <summary>
/// Internal observer for committed authoritative world-clock state. Implementations must remain
/// non-blocking because notifications are emitted on the authoritative game-loop thread.
/// </summary>
internal interface IRuntimeWorldClockObserver
{
    void WorldClockCommitted(
        double time,
        bool dayTime,
        byte moonPhase,
        double slimeRainTime,
        int dayRate);
}
