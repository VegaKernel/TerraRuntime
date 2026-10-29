using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcPhaseCurrentness1458Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Critter_context_callback_cannot_hide_a_newer_status_revision(bool mutateDanger)
        => CheckContextMutation(mutateDanger, mutateFacts: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Critter_context_callback_cannot_hide_changed_world_facts(bool mutateDanger)
        => CheckContextMutation(mutateDanger, mutateFacts: true);

    private static void CheckContextMutation(bool mutateDanger, bool mutateFacts)
    {
        var tiles = new WorldTileStore(new(192, 200));
        Assert.True(tiles.TryAttachWorldSurface(80d));
        for (int x = 0; x < 192; x++) tiles.Set(x, 100, new() { Type = 1, Flags = WorldTileFlags.Active });
        var npcs = new RuntimeNpcStore(200);
        var creation = new NpcStateUpdate(230, 230, 1023f, 1576f, 0f, 0f, 255, default,
            NpcSimulationState.Initial with { DirectionX = 1, DirectionY = -1, SpriteDirection = 1, TimeLeft = 750 });
        Assert.True(npcs.TrySpawn(0, in creation, out var before));
        var status = new RuntimeNpcBuffStatus1458(npcs);
        var projectiles = new RuntimeProjectileStore();
        var motion = new VanillaNpcWorldMotionAiStepper(new NoAi(), tiles);
        var facts = new NpcPhaseWorldFacts1458(true, true, false, false, false, false, 0)
            { BloodMoon = false, Raining = false, Crimson = false };
        bool armed = false, mutated = false;
        void Mutate()
        {
            if (!armed || mutated) return;
            mutated = true;
            if (mutateFacts) facts = facts with { Crimson = true };
            else Assert.True(status.TryApply(before.Handle, 60));
        }
        var weather = new RuntimeTownNpcScheduleConditions1458(true, false, false, false, false);
        var environment = new VanillaNpcPhaseEnvironment1458(tiles, npcs, projectiles, status, motion,
            () => facts, null,
            _ => { if (!mutateDanger) Mutate(); return weather; },
            () => { if (mutateDanger) Mutate(); return []; });
        Assert.True(environment.TryCapture(in before, new(), out var captured));
        using (captured)
        {
            Assert.True(captured.IsCurrent);
            armed = true;
            Assert.False(captured.IsCurrent);
            Assert.True(mutated);
            Assert.True(npcs.TryGet(before.Handle, out var after));
            Assert.Equal(before, after);
        }
    }

    private sealed class NoAi : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
