using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeNurseHealingProjectile1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using var stream = typeof(RuntimeNurseHealingProjectile1458Tests).Assembly.GetManifestResourceStream("TownNurseProjectile1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    public static IEnumerable<object[]> BounceCases()
    {
        using var stream = typeof(RuntimeNurseHealingProjectile1458Tests).Assembly.GetManifestResourceStream("TownNurseBounce1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    [Theory, MemberData(nameof(BounceCases))]
    public void Source_584_tile_bounce_preserves_collision_speed_and_penetration_lifecycle(JsonElement row)
    {
        string terrain = row.GetProperty("terrain").GetString()!;
        float vx = row.GetProperty("vx").GetSingle(), vy = row.GetProperty("vy").GetSingle();
        var tiles = new WorldTileStore(new(100, 80));
        if (terrain is "floor" or "corner")
            for (int x = 0; x < 100; x++) tiles.Set(x, 30, new() { Type = 1, Flags = WorldTileFlags.Active });
        if (terrain is "wall" or "corner")
            for (int y = 0; y < 80; y++) tiles.Set(vx < 0 ? 31 : 33, y, new() { Type = 1, Flags = WorldTileFlags.Active });
        var target = new NpcSnapshot(new(0, new(1)), new(1), 17, 17, 1000, 440, .5f, 0, 255, default,
            NpcSimulationState.Initial with { Life = 200, LifeMax = 250, HitboxOverride = new(18, 40) });
        var projectile = new ProjectileSnapshot(new(0, new(1)), new(1), VanillaProjectileIds.NurseSyringeHeal,
            255, vx < 0 ? 512 : 520, 472, vx, vy, default, 0, 0, 0, 0);
        var context = new VanillaProjectileBehaviorContext(false, 0, 0, NpcTargets: new Resolver(target, true));
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out var definition));
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(in projectile, in definition, in context, out var behavior));
        var lifecycle = new ProjectileLifecycleState(3600, false, default) { PenetrateOverride = row.GetProperty("penetration").GetInt32() };
        var step = new ProjectileSimulationStepContext(projectile, lifecycle, 0, 1);
        Assert.True(new VanillaProjectileWorldMotionResolver(tiles).TryResolve(in step, in definition, in behavior, in context, out var motion));
        Assert.Equal(row.GetProperty("active").GetBoolean(), motion.TimeLeft > 0);
        Assert.Equal(row.GetProperty("x").GetSingle(), motion.State.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), motion.State.PositionY);
        Assert.Equal(row.GetProperty("outVx").GetSingle(), motion.State.VelocityX);
        Assert.Equal(row.GetProperty("outVy").GetSingle(), motion.State.VelocityY);
        Assert.Equal(row.GetProperty("remaining").GetInt32(), motion.PenetrateOverride ?? lifecycle.PenetrateOverride);
        if (motion.TimeLeft > 0) Assert.Equal(row.GetProperty("timeLeft").GetInt32(), motion.TimeLeft);
        Assert.Equal(row.GetProperty("local")[1].GetSingle(), motion.LocalAi!.Value.Ai1);
        var random = new VanillaUnifiedRandom1458(1458);
        Assert.False(new RuntimeProjectileCollisionTileCut1458(random).Evaluate(in motion));
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }

    [Theory, MemberData(nameof(OriginalCases))]
    public void Source_AI110_homing_kill_and_capped_heal_match_original(JsonElement row)
    {
        string scenario = row.GetProperty("scenario").GetString()!;
        bool outer = row.GetProperty("outer").GetBoolean();
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        int life = scenario switch { "missing1" => 249, "missing20" => 230, "missing21" => 229,
            "healthy" => 250, "overfull" => 251, _ => 200 };
        var target = new NpcSnapshot(new(0, new(1)), new(1), scenario == "notTown" ? 3 : 17,
            (short)(scenario == "notTown" ? 3 : 17), 639, 440, .5f, 0, 255, default,
            NpcSimulationState.Initial with { Life = life, LifeMax = 250, HitboxOverride = new(18, 40) });
        float x = scenario switch { "distance7" => 651, "distance8" => 652, "overlap" => 640, "edgeTouch" => 657, _ => 500 };
        var source = new ProjectileSnapshot(new(0, new(1)), new(1), VanillaProjectileIds.NurseSyringeHeal,
            255, x, 456, 8, scenario == "upward" ? -4 : 0, default, 0, 0, 0, 0);
        Assert.True(VanillaDefinitionCatalog.TryGet(source.Type, out var definition));
        Assert.Equal((8, 8, VanillaProjectileAiStyles.NurseHealing), (definition.Width, definition.Height, definition.AiStyle));
        var resolver = new Resolver(target, scenario != "inactive");
        var context = new VanillaProjectileBehaviorContext(false, 0, 0, NpcTargets: resolver);
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(in source, in definition, in context, out var behavior));
        Assert.Equal(!row.GetProperty("active").GetBoolean(), behavior.Kill);
        int expectedHealing = row.GetProperty("target").GetProperty("life").GetInt32() - life;
        Assert.Equal(expectedHealing, behavior.NpcHealing?.Amount ?? 0);
        Assert.Equal(row.GetProperty("local")[1].GetSingle(), behavior.LocalAiOverride!.Value.Ai1);
        if (behavior.NpcHealing is { } heal)
        { Assert.Equal(target.Handle, heal.Target); Assert.Equal(target.Revision, heal.Revision); }
        ProjectileStateUpdate result = new(source.Type, source.Spawner, source.PositionX, source.PositionY,
            behavior.VelocityX, behavior.VelocityY, source.Ai, 0, 0, 0, 0);
        if (outer)
        {
            var tiles = new WorldTileStore(new(100, 80));
            for (int column = 0; column < 100; column++) tiles.Set(column, 30, new() { Type = 1, Flags = WorldTileFlags.Active });
            var step = new ProjectileSimulationStepContext(source, new(3600, false, default), 0, 1);
            Assert.True(new VanillaProjectileWorldMotionResolver(tiles).TryResolve(in step, in definition,
                in behavior, in context, out var motion));
            result = motion.State;
        }
        JsonElement[] shots = row.GetProperty("shots").EnumerateArray().ToArray();
        if (shots.Length > 0)
        {
            Assert.Single(shots);
            Assert.Equal(shots[0].GetProperty("x").GetSingle(), result.PositionX);
            Assert.Equal(shots[0].GetProperty("y").GetSingle(), result.PositionY);
            Assert.Equal(shots[0].GetProperty("vx").GetSingle(), result.VelocityX);
            Assert.Equal(shots[0].GetProperty("vy").GetSingle(), result.VelocityY);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Healing_rejects_missing_owner_stale_revision_reuse_and_invalid_amount_without_projectile_commit(int corruption)
    {
        var npcs = new RuntimeNpcStore();
        var state = new NpcStateUpdate(17, 17, 639, 440, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 200, LifeMax = 250 });
        Assert.True(npcs.TrySpawn(0, in state, out var target));
        var projectiles = new RuntimeProjectileStore(4);
        var shot = new ProjectileStateUpdate(VanillaProjectileIds.NurseSyringeHeal, 255, 640, 456, 8, 0, default, 0, 0, 0, 0);
        Assert.True(projectiles.TrySpawn(0, in shot, out var before));
        var heal = new ProjectileNpcHealingApplication(target.Handle, target.Revision, corruption == 3 ? 51 : corruption == 4 ? 0 : 20);
        if (corruption == 1) Assert.True(npcs.TryUpdate(target.Handle, in state, out _));
        if (corruption == 2) { Assert.True(npcs.TryDespawn(target.Handle)); Assert.True(npcs.TrySpawn(0, in state, out _)); }
        var executor = new RuntimeProjectileStateExecutor(projectiles, npcs: corruption == 0 ? null : npcs);
        var summary = executor.Tick(new HealStepper(heal));
        Assert.Equal(0, summary.Applied);
        Assert.True(projectiles.TryGet(before.Handle, out var retained)); Assert.Equal(before, retained);
        Assert.True(npcs.TryGetActive(0, out var active)); Assert.Equal(200, active.Simulation.Life);
    }

    [Fact]
    public void Accepted_heal_commits_after_projectile_removal_and_only_the_observed_revision()
    {
        var npcs = new RuntimeNpcStore();
        var state = new NpcStateUpdate(17, 17, 639, 440, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 200, LifeMax = 250 });
        Assert.True(npcs.TrySpawn(0, in state, out var target));
        var sink = new RemovalSink(npcs, target.Handle);
        var projectiles = new RuntimeProjectileStore(4, commitSink: sink);
        var shot = new ProjectileStateUpdate(VanillaProjectileIds.NurseSyringeHeal, 255, 640, 456, 8, 0, default, 0, 0, 0, 0);
        Assert.True(projectiles.TrySpawn(0, in shot, out var projectile));
        var healing = new RuntimeProjectileNpcHealing1458(npcs, null);
        var effects = new RuntimeProjectileSimulationCommitSink(new(4), new(4), npcHealing: healing);
        var executor = new RuntimeProjectileStateExecutor(projectiles, effects, npcs: npcs);
        var summary = executor.Tick(new VanillaProjectileWorldStateStepper(new(new(100, 80)), npcs: npcs));
        Assert.Equal(1, summary.Applied); Assert.Equal(0, summary.Rejected);
        Assert.Equal(200, sink.LifeAtRemoval); Assert.False(projectiles.TryGet(projectile.Handle, out _));
        Assert.True(npcs.TryGet(target.Handle, out var healed)); Assert.Equal(220, healed.Simulation.Life);
        Assert.Equal(target.Revision.Value + 1, healed.Revision.Value);
        Assert.False(healing.TryApply(new(target.Handle, target.Revision, 20)));
        Assert.Equal(220, healed.Simulation.Life);
    }

    private sealed class HealStepper(ProjectileNpcHealingApplication heal) : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext context, out ProjectileSimulationStepResult next)
        {
            var p = context.Projectile;
            next = new(new(p.Type, p.Spawner, p.PositionX, p.PositionY, p.VelocityX, p.VelocityY,
                p.Ai, p.BannerIdToRespondTo, p.Damage, p.KnockBack, p.OriginalDamage), 0,
                TerminationReason: ProjectileSimulationTerminationReason.BehaviorKill, NpcHealing: heal);
            return true;
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Collision_offer_runs_only_on_accepted_state_and_before_Kill_publication(bool stale)
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var capture = new RandomAtRemoval(random);
        var npcs = new RuntimeNpcStore();
        var target = new NpcStateUpdate(17, 17, 1000, 440, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 200, LifeMax = 250 });
        Assert.True(npcs.TrySpawn(0, in target, out _));
        var projectiles = new RuntimeProjectileStore(4, commitSink: capture);
        var shot = new ProjectileStateUpdate(VanillaProjectileIds.NurseSyringeHeal, 255, 520, 472, 8, 3, default, 0, 0, 0, 0);
        Assert.True(projectiles.TrySpawn(0, in shot, out var projectile));
        Assert.True(projectiles.TryCommitSimulationStep(projectile.Handle, in shot, 3600, null, null,
            out _, out _, penetrateOverride: 1));
        var tiles = new WorldTileStore(new(100, 80));
        for (int x = 0; x < 100; x++) tiles.Set(x, 30, new() { Type = 1, Flags = WorldTileFlags.Active });
        IProjectileStateStepper motion = new VanillaProjectileWorldStateStepper(tiles, npcs: npcs);
        if (stale) motion = new MutateProjectileDuringPlan(projectiles, motion);
        var effects = new RuntimeProjectileSimulationCommitSink(new(4), new(4),
            collisionTileCuts: new RuntimeProjectileCollisionTileCut1458(random));
        var summary = new RuntimeProjectileStateExecutor(projectiles, effects, npcs: npcs).Tick(motion);
        if (stale)
        {
            Assert.Equal(0, summary.Applied); Assert.Null(capture.NextAtRemoval);
            Assert.Equal(new VanillaUnifiedRandom1458(1458).Next(), random.Next());
            Assert.True(projectiles.TryGet(projectile.Handle, out var retained)); Assert.Equal(99f, retained.VelocityX);
        }
        else
        {
            Assert.Equal(1, summary.Applied); Assert.Equal(1335025742, capture.NextAtRemoval);
            Assert.Equal(1335025742, random.Next()); Assert.False(projectiles.TryGet(projectile.Handle, out _));
        }
    }

    [Fact]
    public void Unowned_collision_offer_or_nonzero_Nurse_damage_does_not_advance_state()
    {
        var npcs = new RuntimeNpcStore();
        var target = new NpcStateUpdate(17, 17, 1000, 440, 0, 0, 255, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawn(0, in target, out _));
        var tiles = new WorldTileStore(new(100, 80));
        for (int x = 0; x < 100; x++) tiles.Set(x, 30, new() { Type = 1, Flags = WorldTileFlags.Active });
        foreach (short damage in new short[] { 0, 1 })
        {
            var table = new RuntimeProjectileStore(4);
            var shot = new ProjectileStateUpdate(VanillaProjectileIds.NurseSyringeHeal, 255, 520, 472, 8, 3, default, 0, damage, 0, damage);
            Assert.True(table.TrySpawn(0, in shot, out var before));
            Assert.Equal(0, new RuntimeProjectileStateExecutor(table, npcs: npcs).Tick(
                new VanillaProjectileWorldStateStepper(tiles, npcs: npcs)).Applied);
            Assert.True(table.TryGet(before.Handle, out var retained)); Assert.Equal(before, retained);
        }
    }

    private sealed class RandomAtRemoval(VanillaUnifiedRandom1458 random) : IProjectileStateCommitSink
    {
        internal int? NextAtRemoval;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot projectile)
        { if (kind == ProjectileStateCommitKind.Despawn) NextAtRemoval = random.Clone().Next(); }
    }
    private sealed class MutateProjectileDuringPlan(RuntimeProjectileStore store, IProjectileStateStepper inner) : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext context, out ProjectileSimulationStepResult next)
        {
            bool planned = inner.TryStepState(in context, out next);
            var p = context.Projectile;
            var changed = new ProjectileStateUpdate(p.Type, p.Spawner, p.PositionX, p.PositionY, 99, p.VelocityY,
                p.Ai, p.BannerIdToRespondTo, p.Damage, p.KnockBack, p.OriginalDamage);
            Assert.True(store.TryUpdate(p.Handle, in changed, out _));
            return planned;
        }
    }
    private sealed class RemovalSink(RuntimeNpcStore npcs, NpcHandle target) : IProjectileStateCommitSink
    {
        internal int LifeAtRemoval;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot projectile)
        { if (kind == ProjectileStateCommitKind.Despawn && npcs.TryGet(target, out var n)) LifeAtRemoval = n.Simulation.Life; }
    }
    private sealed class Resolver(NpcSnapshot target, bool active) : IVanillaProjectileNpcTargetResolver
    {
        public bool IsNpcSlotAddressable(int slot) => slot is >= 0 and < 200;
        public bool TryGetActiveNpc(int slot, out NpcSnapshot n) { n = target; return active && slot == target.Handle.Slot; }
        public bool TryGetChaseableTargetCenter(int slot, out float x, out float y) { x = y = 0; return false; }
        public bool TryFindClosestTargetWithLineOfSight(in ProjectileSnapshot p, in VanillaProjectileDefinition d,
            float range, out int slot, out float x, out float y) { slot = -1; x = y = 0; return false; }
    }
}
