using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class BigMimicRetainedEffects1458Tests
{
    public static IEnumerable<object[]> CannonCases() => BigMimicAi1458Tests.Read("BigMimicCannon1458");

    [Theory]
    [MemberData(nameof(CannonCases))]
    public void World_execution_materializes_original_cannon_offers_from_body_before_movement(JsonElement row)
    {
        var fixture = new Fixture(476, 8f, row.GetProperty("clock").GetSingle(), row.GetProperty("seed").GetInt32(),
            tenth: row.GetProperty("tenth").GetBoolean(), width: row.GetProperty("width").GetInt32(),
            height: row.GetProperty("height").GetInt32());
        Assert.True(fixture.World.TryStepState(in fixture.Before, out var proposal));
        Assert.Empty(fixture.Items());
        Assert.True(fixture.Store.TryUpdateUnpublished(fixture.Before.Handle, in proposal, out var accepted));
        var after = fixture.World.CompleteCommittedState(in fixture.Before, in accepted, new BigMimicWorldExecution1458Tests.Mutations(fixture.Store));
        Assert.True(after.IsActive);
        var expected = row.GetProperty("drops");
        var actual = fixture.Items();
        Assert.Equal(expected.GetArrayLength(), actual.Length);
        for (int index = 0; index < actual.Length; index++)
        {
            var drop = expected[index];
            Assert.Equal(drop.GetProperty("slot").GetInt32(), actual[index].Handle.Slot);
            Assert.Equal(drop.GetProperty("id").GetInt32(), actual[index].ItemNetId);
            Assert.Equal(drop.GetProperty("x").GetSingle(), actual[index].PositionX);
            Assert.Equal(drop.GetProperty("y").GetSingle(), actual[index].PositionY);
            Assert.Equal(drop.GetProperty("vx").GetSingle(), actual[index].VelocityX);
            Assert.Equal(drop.GetProperty("vy").GetSingle(), actual[index].VelocityY);
            Assert.Equal(WorldItemOwnershipMode.GrabDelayForAllPlayers, actual[index].Ownership);
            Assert.Equal(100, actual[index].GrabDelayTime);
            Assert.Equal(0, actual[index].Prefix);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.SourceRandom.Next());
    }

    [Theory]
    [InlineData("random")]
    [InlineData("target")]
    [InlineData("player")]
    [InlineData("npc")]
    public void Changed_accepted_dependency_cannot_publish_cannon_or_replace_new_random_draws(string dependency)
    {
        var fixture = new Fixture(476, 8f, 19f, 1458, tenth: true);
        Assert.True(fixture.World.TryStepState(in fixture.Before, out var proposal));
        Assert.True(fixture.Store.TryUpdateUnpublished(fixture.Before.Handle, in proposal, out var accepted));
        if (dependency == "random")
            fixture.Random.SourceRandom.Next();
        else if (dependency == "target")
            fixture.Targeting.SetCandidates([new(0, 1820f, 1021f, 0, true, false, false, false)]);
        else if (dependency == "player")
            fixture.Player.Snapshot = fixture.Player.Snapshot with { Revision = new(2) };
        else
            Assert.True(fixture.Store.TryUpdateUnpublished(accepted.Handle, in proposal, out _));
        var checkpoint = fixture.Random.SourceRandom.Clone();
        Assert.False(fixture.World.CompleteCommittedState(in fixture.Before, in accepted,
            new BigMimicWorldExecution1458Tests.Mutations(fixture.Store)).IsActive);
        Assert.Empty(fixture.Items());
        Assert.True(fixture.Random.SourceRandom.HasSameState(checkpoint));
    }

    [Fact]
    public void Accepted_strike_forces_idle_wake_once_and_following_strike_uses_ordinary_update()
    {
        var fixture = new Fixture(473, 0f, 23f, 1458);
        var damage = new RuntimeNpcDamageExecutor(fixture.Store);
        var request = new NpcDamageRequest(fixture.Before.Handle, DamageSource.Server, 30);
        Assert.True(damage.TryApply(in request, out var firstDamage));
        Assert.Equal(13, firstDamage.ResolvedDamage);
        var first = Assert.Single(fixture.Commits.Events);
        Assert.Equal(NpcStateCommitKind.ForcedUpdate, first.Kind);
        Assert.Equal(1f, first.Snapshot.Ai.Ai0);
        Assert.Equal(0f, first.Snapshot.Ai.Ai1);
        Assert.Equal(3487, first.Snapshot.Simulation.Life);
        fixture.Commits.Events.Clear();

        Assert.True(damage.TryApply(in request, out var nextDamage));
        Assert.Equal(13, nextDamage.ResolvedDamage);
        var next = Assert.Single(fixture.Commits.Events);
        Assert.Equal(NpcStateCommitKind.Update, next.Kind);
        Assert.Equal(first.Snapshot.Handle, next.Snapshot.Handle);
        Assert.True(next.Snapshot.Revision.Value > first.Snapshot.Revision.Value);
        Assert.Equal(1f, next.Snapshot.Ai.Ai0);
        Assert.Equal(3474, next.Snapshot.Simulation.Life);
    }

    [Fact]
    public void Authoritative_world_tick_publishes_only_final_forced_shell_transition()
    {
        var fixture = new Fixture(473, 3f, 179f, 1458, expert: true);
        var summary = new RuntimeNpcAiStateExecutor(fixture.Store, fixture.Projectiles).Tick(fixture.World);
        Assert.Equal(1, summary.Applied);
        Assert.Equal(0, summary.Rejected);
        var commit = Assert.Single(fixture.Commits.Events);
        Assert.Equal(NpcStateCommitKind.ForcedUpdate, commit.Kind);
        Assert.Equal(2f, commit.Snapshot.Ai.Ai0);
        Assert.Equal(1001.175f, commit.Snapshot.PositionX);
        Assert.True(commit.Snapshot.Simulation.ReflectsProjectiles);
    }

    [Fact]
    public void Shield_departure_reflects_current_peers_unpublished_before_their_next_simulation()
    {
        var fixture = new Fixture(473, 3f, 179f, 1458, expert: true);
        var update = new ProjectileStateUpdate(new(1), 0, 1011.75f, 1019.25f, 3f, 4f, default, 0, 101, 0f, 101);
        Assert.True(fixture.Projectiles.TrySpawn(0, in update, out var projectile));
        update = update with { VelocityX = 1f, VelocityY = 2f };
        Assert.True(fixture.Projectiles.TryCommitSimulationStep(projectile.Handle, in update, 100,
            out projectile, out _));
        Assert.True(fixture.World.TryStepState(in fixture.Before, out var proposal));
        Assert.True(fixture.Projectiles.TryGetLifecycle(projectile.Handle, out var beforeLifecycle));
        Assert.False(beforeLifecycle.Reflected);
        Assert.True(fixture.Store.TryUpdateUnpublished(fixture.Before.Handle, in proposal, out var accepted));
        var after = fixture.World.CompleteCommittedState(in fixture.Before, in accepted,
            new BigMimicWorldExecution1458Tests.Mutations(fixture.Store));
        Assert.True(after.IsActive);
        Assert.Equal(2f, after.Ai.Ai0);
        Assert.True(after.Simulation.ReflectsProjectiles);
        Assert.True(fixture.Projectiles.TryGet(projectile.Handle, out var reflected));
        Assert.Equal(25, reflected.Damage);
        Assert.True(fixture.Projectiles.TryGetLifecycle(projectile.Handle, out var lifecycle));
        Assert.True(lifecycle.Reflected);
        Assert.Equal(1, lifecycle.PenetrateOverride);
        Assert.NotEqual(projectile.VelocityX, reflected.VelocityX);
        Assert.NotEqual(projectile.VelocityY, reflected.VelocityY);
    }

    [Fact]
    public void New_projectile_peer_invalidates_shield_plan_without_mutation_or_random_draws()
    {
        var fixture = new Fixture(473, 3f, 179f, 1458, expert: true);
        Assert.True(fixture.World.TryStepState(in fixture.Before, out var proposal));
        Assert.True(fixture.Store.TryUpdateUnpublished(fixture.Before.Handle, in proposal, out var accepted));
        var update = new ProjectileStateUpdate(new(1), 0, 1011.75f, 1019.25f, 3f, 4f, default, 0, 101, 0f, 101);
        Assert.True(fixture.Projectiles.TrySpawn(999, in update, out var projectile));
        var checkpoint = fixture.Random.SourceRandom.Clone();
        Assert.False(fixture.World.CompleteCommittedState(in fixture.Before, in accepted,
            new BigMimicWorldExecution1458Tests.Mutations(fixture.Store)).IsActive);
        Assert.True(fixture.Random.SourceRandom.HasSameState(checkpoint));
        Assert.True(fixture.Projectiles.TryGet(projectile.Handle, out var current));
        Assert.Equal(projectile, current);
    }

    [Fact]
    public void Source_nonfinite_zero_old_velocity_is_rejected_before_live_shield_mutation()
    {
        var fixture = new Fixture(473, 3f, 0f, 1458, expert: true);
        var update = new ProjectileStateUpdate(new(1), 0, 1011.75f, 1019.25f, 1f, 2f, default, 0, 101, 0f, 101);
        Assert.True(fixture.Projectiles.TrySpawn(0, in update, out var projectile));
        var checkpoint = fixture.Random.SourceRandom.Clone();
        Assert.False(fixture.World.TryStepState(in fixture.Before, out _));
        Assert.True(fixture.Random.SourceRandom.HasSameState(checkpoint));
        Assert.True(fixture.Projectiles.TryGet(projectile.Handle, out var current));
        Assert.Equal(projectile, current);
    }

    private sealed class Fixture
    {
        internal readonly Recording Commits = new();
        internal readonly RuntimeNpcStore Store;
        internal readonly RuntimeProjectileStore Projectiles = new();
        internal readonly RuntimeWorldItemStore WorldItems = new();
        internal readonly Players Player = new();
        internal readonly SystemVanillaNpcRandom Random;
        internal readonly VanillaNpcTargetingAiStepper Targeting;
        internal readonly VanillaNpcWorldMotionAiStepper World;
        internal readonly NpcSnapshot Before;

        internal Fixture(int type, float phase, float clock, int seed, bool expert = false,
            bool tenth = false, int width = 28, int height = 44)
        {
            Store = new(200, Commits);
            Random = new(seed);
            Targeting = new(new Rejecting(), random: Random);
            World = new(Targeting, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
            Targeting.SetWorldConditions(true, false, expertMode: expert);
            Targeting.SetCandidates([new(0, 1810f, 1021f, 0, true, false, false, false)]);
            Targeting.SetBigMimicEffects(new RuntimeBigMimicEffects1458(Projectiles, WorldItems, Player), tenth);
            var initial = new NpcStateUpdate(type, (short)type, 1000.75f, 1000.25f, .5f, 0f, 0,
                new(phase, clock, 0f, 0f), NpcSimulationState.Initial with
                {
                    DirectionX = 1, DirectionY = 1, SpriteDirection = 1, Life = 3500, LifeMax = 3500,
                    HitboxOverride = new(width, height), SpawnDifficulty = expert ? 2f : 1f
                });
            Assert.True(Store.TrySpawn(0, in initial, out Before));
            Commits.Events.Clear();
        }

        internal WorldItemSnapshot[] Items()
        {
            var buffer = new WorldItemSnapshot[400];
            return buffer[..WorldItems.CopyActive(buffer)];
        }
    }

    private sealed class Recording : INpcStateCommitSink
    {
        internal readonly List<(NpcStateCommitKind Kind, NpcSnapshot Snapshot)> Events = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Events.Add((kind, snapshot));
    }

    private sealed class Players : IRuntimePlayerSlotSnapshotLookup
    {
        internal PlayerStateSnapshot Snapshot = default(PlayerStateSnapshot) with
        { Player = new(new(0), new(1)), Revision = new(1), PositionX = 1800f, PositionY = 1000f };
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = Snapshot;
            return slot.Value == 0;
        }
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
