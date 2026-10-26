namespace TerraRuntime.Application;

/// <summary>
/// Immutable post-loot effects of one accepted Moon participant death. The retained death journal owns
/// its single publication attempt; this value never carries an observer or a mutable planning envelope.
/// </summary>
internal readonly record struct MoonEventDeathEffects1458(
    sbyte MoonIcon,
    long ProgressRevision,
    int Wave,
    float WaveKills,
    int WaveRequirement,
    bool WaveAdvanced,
    bool ShouldReportProgress)
{
    internal short EarnedProgressionEvent => WaveAdvanced && Wave == 15
        ? MoonIcon == 1 ? (short)14 : (short)15
        : (short)0;

    internal bool IsCurrent(RuntimeWorldClock clock) => MoonIcon != 0 && clock.MoonEventActive &&
        clock.MoonEventProgressRevision == ProgressRevision &&
        (MoonIcon == 1 ? clock.SnowMoonActive : clock.PumpkinMoonActive);
}
