namespace TerraRuntime.Tests;

public sealed class GroundCombatRangedDefense1458Tests
{
    [Fact]
    public void Actual_npc_phase_preserves_source_ranged_births_and_granite_defense_transitions()
        => GroundCombatAntlionMotion1458Tests.RunFixture("GroundCombatRangedDefenseLinux1458",21);

    [Fact]
    public void Rare_idle_success_arguments_preserve_actual_source_random_offers()
        => GroundCombatAntlionMotion1458Tests.RunFixture("GroundCombatIdleSuccessLinux1458",4);
}
