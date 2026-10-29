using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Tests;

public sealed class GroundCombatProjectileExecutor1458Tests
{
    [Fact]
    public void Lifetime_tail_is_one_movement_with_one_original_kill_boundary()
    {
        // Independently observed 1458 Update ordering: TTL1/2/3 move2/3/4
        // times, TTL4 expires on the last extraUpdate and moves4 times.
        foreach (int type in new[] { 438, 592 })
        foreach (int lifetime in new[] { 1, 2, 3, 4 })
        {
            var store = new RuntimeProjectileStore();
            var initial = State(type);
            Assert.True(store.TrySpawnVanilla(in initial, lifetime, out var source));
            var sink = new Sink();
            var stepper = new Motion();
            var result = new RuntimeProjectileStateExecutor(store, sink, sink).Tick(stepper);
            Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), result);
            Assert.False(store.TryGet(source.Handle, out _));
            Assert.Single(sink.Terminations);
            var terminal = sink.Terminations[0];
            Assert.Equal(ProjectileSimulationTerminationReason.LifetimeExpired, terminal.Reason);
            int moves = Math.Min(lifetime + 1, 4);
            Assert.Equal(100f + moves * 10f, terminal.FinalProjectile.PositionX);
            Assert.Equal(moves, sink.Steps.Length);
            Assert.Equal(lifetime < 4 ? 1 : 0, stepper.Tails);
            if (lifetime < 4)
                Assert.Equal(new ProjectileKillOrigin(100f + lifetime * 10f, 100f), terminal.KillOrigin);
            else Assert.Null(terminal.KillOrigin);
            Assert.All(sink.Steps, step => Assert.Equal(source.Type, step.State.Type));
        }
    }

    [Fact]
    public void Refused_or_malformed_tail_and_unowned_collision_offer_leave_the_original_actor_untouched()
    {
        foreach (string mode in new[] { "refuse", "ai-change", "clock-change", "offer-without-owner",
            "shimmer-without-owner", "wet-count-out-of-range" })
        {
            var store = new RuntimeProjectileStore();
            var state = State(438);
            Assert.True(store.TrySpawnVanilla(in state, 2, out var source));
            Assert.True(store.TryGetLifecycle(source.Handle, out var life));
            var sink = new Sink();
            var result = new RuntimeProjectileStateExecutor(store, sink, sink).Tick(new Motion(mode));
            Assert.Equal(new ProjectileStateTickSummary(1, 1, 0, 1), result);
            Assert.True(store.TryGet(source.Handle, out var after));
            Assert.Equal(source, after);
            Assert.True(store.TryGetLifecycle(source.Handle, out var afterLife));
            Assert.Equal(life, afterLife);
            Assert.Empty(sink.Terminations);
            Assert.Empty(sink.Steps);
        }
    }

    private static ProjectileStateUpdate State(int type) =>
        new(new(type), byte.MaxValue, 100f, 100f, 10f, 0f, default, 0, 20, 0f, 0);

    private sealed class Motion(string mode = "normal") : IProjectileStateStepper, IProjectileInactiveLifetimeMotionStepper
    {
        internal int Tails;
        public bool SupportsInactiveLifetimeMotion(ProjectileTypeId type) => type.Value is 438 or 592;
        public bool TryStepState(in ProjectileSimulationStepContext context, out ProjectileSimulationStepResult next)
        {
            var state = Move(context.Projectile);
            int time = context.Lifecycle.TimeLeft - 1;
            next = new(state, time, TerminationReason: time == 0
                ? ProjectileSimulationTerminationReason.LifetimeExpired : ProjectileSimulationTerminationReason.None);
            return true;
        }
        public bool TryStepInactiveLifetimeMotion(in ProjectileSimulationStepContext context,
            out ProjectileSimulationStepResult next)
        {
            Tails++;
            Assert.Equal(0, context.Lifecycle.TimeLeft);
            Assert.Equal(ProjectileSimulationTerminationReason.LifetimeExpired, context.TerminationReason);
            var state = Move(context.Projectile);
            if (mode == "ai-change") state = state with { Ai = new ProjectileAiState(1f, 0f, 0f) };
            next = new(state, mode == "clock-change" ? -1 : 0,
                TerminationReason: ProjectileSimulationTerminationReason.LifetimeExpired,
                CollisionTileCutOffer: mode == "offer-without-owner" ? new ProjectileCollisionTileCutOffer() : null)
            {
                ShimmerVisualOffers = mode == "shimmer-without-owner" ? (byte)10 : (byte)0,
                SourceWetCountOverride = mode == "wet-count-out-of-range" ? (byte)11 : null
            };
            return mode != "refuse";
        }
        private static ProjectileStateUpdate Move(ProjectileSnapshot source) => new(source.Type, source.Spawner,
            source.PositionX + source.VelocityX, source.PositionY + source.VelocityY, source.VelocityX, source.VelocityY,
            source.Ai, source.BannerIdToRespondTo, source.Damage, source.KnockBack, source.OriginalDamage);
    }

    private sealed class Sink : IProjectileSimulationCommitSink, IProjectileTerminationCommitSink
    {
        internal readonly List<ProjectileTerminationCommit> Terminations = [];
        internal ProjectileSimulationStepResult[] Steps = [];
        public void ProjectileTerminated(in ProjectileTerminationCommit termination) => Terminations.Add(termination);
        public void ProjectileSimulationCommitted(in ProjectileSnapshot initial, in ProjectileLifecycleState lifecycle,
            ReadOnlySpan<ProjectileSimulationStepResult> steps, in ProjectileSnapshot final, bool expired) => Steps = steps.ToArray();
    }
}
