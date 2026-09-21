using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaMoonEventGhostAiTests
{
    [Fact]
    public void Type_330_defaults_match_source_ai22_entry()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(330), out VanillaNpcDefinition definition));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new NpcTypeId(330), out _));
        Assert.Equal(new NpcAiStyleId(22), definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.MoonEventGhost, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.NoClipFlight, definition.PhysicsFamily);
        Assert.Equal((24, 44, 90, 44, 1250), (definition.BaseWidth, definition.BaseHeight, definition.Damage, definition.Defense, definition.LifeMax));
        Assert.Equal(.4f, definition.KnockBackResist);
        Assert.Equal(100, definition.AlphaAtSpawn);
    }

    [Fact]
    public void Active_event_tracks_target_and_uses_source_flight_acceleration()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(true, 300f, 40f);
        NpcSnapshot npc = CreateNpc();
        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(1, next.Simulation.DirectionX);
        Assert.Equal(-1, next.Simulation.DirectionY);
        Assert.Equal(.1f, next.VelocityX, 5);
        Assert.Equal(-.04f, next.VelocityY, 5);
        Assert.Equal(0, next.Simulation.Alpha);
        Assert.True(next.Simulation.NoGravity);
        Assert.True(next.Simulation.NoTileCollide);
    }

    [Fact]
    public void Ended_event_encourages_despawn_without_substituting_a_different_ai()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(false, 300f, 40f);
        NpcSnapshot npc = CreateNpc();
        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Assert.Equal(10, next.Simulation.TimeLeft);
        Assert.Equal(.1f, next.VelocityX, 5);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(bool moon, float x, float y)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.EnableZombieMotion(100d);
        stepper.SetMoonEventState(moon);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, x, y, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot CreateNpc() => new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1),
        330, 330, 100f, 100f, 0f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget, default,
        NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1, SpriteDirection = 1, Life = 1250, LifeMax = 1250,
            TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft });

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
