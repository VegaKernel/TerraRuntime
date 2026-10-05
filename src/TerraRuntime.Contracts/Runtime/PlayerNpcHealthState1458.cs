namespace TerraRuntime.Contracts.Runtime;

/// <summary>NPC-facing remote life and persistent source regeneration clocks; missing clocks are unknown imports.</summary>
public readonly record struct PlayerNpcHealthState1458(int Life, int? RegenCount, float? RegenTime,
    bool SourceProfileKnown = false)
{
    public static PlayerNpcHealthState1458 Constructor => new(100, 0, 0f, true);
}
