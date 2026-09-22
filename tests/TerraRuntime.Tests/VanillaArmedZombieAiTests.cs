using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaArmedZombieAiTests
{
    [Fact]
    public void Armed_attack_starts_after_the_common_motion_tick_and_brakes_for_twenty_ticks()
    {
        var entry = Input(ai2: 0f, velocityX: 1f) with { ArmedAttackCanStart = true };
        Assert.True(VanillaZombieMotion.TryStep(in entry, out VanillaZombieMotionResult started));
        Assert.Equal(.7f, started.VelocityX, 5);
        Assert.Equal(1f, started.Ai.Ai2, 5);

        var active = Input(ai2: 1f, velocityX: 1f);
        Assert.True(VanillaZombieMotion.TryStep(in active, out VanillaZombieMotionResult held));
        Assert.Equal(.9f, held.VelocityX, 5);
        Assert.Equal(2f, held.Ai.Ai2, 5);
        Assert.Equal(1f, held.Ai.Ai3, 5);

        var final = Input(ai2: 19f, velocityX: 1f);
        Assert.True(VanillaZombieMotion.TryStep(in final, out VanillaZombieMotionResult finished));
        Assert.Equal(0f, finished.Ai.Ai2, 5);
        Assert.Equal(.9f, finished.VelocityX, 5);
    }

    [Fact]
    public void Armed_melee_expands_only_after_five_attack_ticks_and_faces_sprite_direction()
    {
        var beforeReach = new NpcAiState(0f, 0f, 5f, 0f);
        var activeReach = new NpcAiState(0f, 0f, 6f, 0f);

        Assert.False(VanillaArmedZombieCombatFacts1458.HasExtendedMeleeReach(VanillaNpcIds.ArmedZombie, beforeReach));
        Assert.True(VanillaArmedZombieCombatFacts1458.HasExtendedMeleeReach(VanillaNpcIds.ArmedZombie, activeReach));
        Assert.True(VanillaArmedZombieCombatFacts1458.HasExtendedMeleeReach(VanillaNpcIds.ArmedTorchZombie, activeReach));
        Assert.Equal(21, VanillaArmedZombieCombatFacts1458.ResolveMeleeDamage(17, VanillaNpcIds.ArmedZombie, activeReach));

        float left = 100f;
        float right = 118f;
        VanillaArmedZombieCombatFacts1458.ExpandMeleeHitbox(VanillaNpcIds.ArmedZombie, activeReach, -1, ref left, ref right);
        Assert.Equal(66f, left, 5);
        Assert.Equal(118f, right, 5);

        left = 100f;
        right = 118f;
        VanillaArmedZombieCombatFacts1458.ExpandMeleeHitbox(VanillaNpcIds.ArmedZombie, activeReach, 1, ref left, ref right);
        Assert.Equal(100f, left, 5);
        Assert.Equal(152f, right, 5);
    }

    private static VanillaZombieMotionInput Input(float ai2, float velocityX) => new(
        PositionX: 100f,
        OldPositionX: 99f,
        VelocityX: velocityX,
        VelocityY: 0f,
        DirectionX: 1,
        DirectionY: 1,
        Target: VanillaNpcDefinitionCatalog.DefaultTarget,
        Ai: new NpcAiState(0f, 0f, ai2, 0f),
        Scale: 1f,
        TargetOverlaps: false,
        ClosestTarget: new VanillaZombieTargetRefresh(false, VanillaNpcDefinitionCatalog.DefaultTarget, 0, 0))
    {
        MotionProfile = VanillaGroundFighterMotionProfile.ArmedZombie,
        TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
        Life = 45,
        LifeMax = 45
    };
}
