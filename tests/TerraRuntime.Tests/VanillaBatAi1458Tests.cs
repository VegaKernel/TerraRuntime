using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaBatAi1458Tests
{
    [Fact]
    public void Vampire_forms_keep_bottom_anchor_life_and_reset_ai_across_source_distance_transforms()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 30f, 100f, 0, true, false, false, false)]);
        NpcSnapshot flying = Snapshot(VanillaNpcIds.Vampire) with
        {
            Target = 7,
            Ai = new NpcAiState(1f, 2f, 3f, 4f),
            Simulation = Snapshot(VanillaNpcIds.Vampire).Simulation with
            { Life = 333, LifeMax = 750, TimeLeft = 9, LocalAi = new NpcAiState(5f, 6f, 7f, 8f) }
        };

        Assert.True(stepper.TryStepState(in flying, out NpcStateUpdate grounded));
        Assert.Equal(VanillaNpcIds.VampireHumanoid.Value, grounded.Type);
        Assert.Equal(2f, grounded.PositionY);
        Assert.Equal(333, grounded.Simulation.Life);
        Assert.Equal(750, grounded.Simulation.LifeMax);
        Assert.Equal(default, grounded.Ai);
        Assert.Equal(default, grounded.Simulation.LocalAi);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, grounded.Simulation.TimeLeft);

        stepper.EnableZombieMotion(double.PositiveInfinity);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 1_000f, 1_000f, 0, true, false, false, false)]);
        NpcSnapshot humanoid = new(flying.Handle, flying.Revision, VanillaNpcIds.VampireHumanoid.Value,
            (short)VanillaNpcIds.VampireHumanoid.Value, 10f, 2f, 0f, 0f, 7, new NpcAiState(1f, 2f, 3f, 4f),
            flying.Simulation with { Life = 333, LifeMax = 750, TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft });

        Assert.True(stepper.TryStepState(in humanoid, out NpcStateUpdate returned));
        Assert.Equal(VanillaNpcIds.Vampire.Value, returned.Type);
        Assert.Equal(20f, returned.PositionY);
        Assert.Equal(333, returned.Simulation.Life);
        Assert.Equal(default, returned.Ai);
        Assert.Equal(default, returned.Simulation.LocalAi);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, returned.Simulation.TimeLeft);
    }

    [Fact]
    public void Vampire_humanoid_uses_its_source_six_pixel_speed_and_reversal_damping()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.EnableZombieMotion(double.PositiveInfinity);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 0f, 10f, 0, true, false, false, false)]);
        NpcSnapshot humanoid = Snapshot(VanillaNpcIds.VampireHumanoid) with
        {
            Target = 7,
            VelocityX = 1f,
            Simulation = Snapshot(VanillaNpcIds.VampireHumanoid).Simulation with
            { DirectionX = -1, Life = 750, LifeMax = 750, TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft }
        };

        Assert.True(stepper.TryStepState(in humanoid, out NpcStateUpdate next));
        Assert.Equal(.88f, next.VelocityX, 5);
    }

    [Fact]
    public void Catalog_admits_source_defaults_for_ordinary_bats_slimer_and_vampire()
    {
        Assert.Equal(16, VanillaBatNpcCatalog1458.DefinitionCount);
        foreach (VanillaNpcDefinition definition in VanillaBatNpcCatalog1458.AllDefinitions)
        {
            Assert.Equal(VanillaNpcAiStyles.Bat, definition.AiStyle);
            Assert.Equal(VanillaNpcBehaviorFamily.Bat, definition.BehaviorFamily);
            Assert.Equal(VanillaNpcPhysicsFamily.BatFlight, definition.PhysicsFamily);
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(definition.Type, out VanillaNpcDefinition resolved));
            Assert.Equal(definition, resolved);
            Assert.True(VanillaNpcAiCoverageCatalog.TryGet(definition.Type, out VanillaNpcAiCoverage coverage));
            Assert.True(coverage.Has(VanillaNpcAiCapability.BatMotionSlice));
            Assert.False(coverage.FullVanillaAiParity);
        }

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.LavaBat, out VanillaNpcDefinition lavaBat));
        Assert.Equal(22, lavaBat.BaseWidth);
        Assert.Equal(22, lavaBat.BaseHeight);
        Assert.Equal(50, lavaBat.Damage);
        Assert.Equal(16, lavaBat.Defense);
        Assert.Equal(160, lavaBat.LifeMax);
        Assert.Equal(0.6f, lavaBat.KnockBackResist);
        Assert.Equal(1.15f, lavaBat.Scale);

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Harpy, out VanillaNpcDefinition harpy));
        Assert.Equal((24, 34, 25, 8, 100), (harpy.BaseWidth, harpy.BaseHeight, harpy.Damage, harpy.Defense, harpy.LifeMax));
        Assert.Equal(.6f, harpy.KnockBackResist);
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.HarpyFeather, out VanillaProjectileDefinition feather));
        Assert.Equal((14, 14, VanillaProjectileAiStyles.Arrow), (feather.Width, feather.Height, feather.AiStyle));
        Assert.True(feather.TileCollide);
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.HarpyFeather));

        foreach (NpcTypeId type in new[] { VanillaNpcIds.Demon, VanillaNpcIds.VoodooDemon })
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition demon));
            Assert.Equal((28, 48, 32, 8), (demon.BaseWidth, demon.BaseHeight, demon.Damage, demon.Defense));
            Assert.Equal(type == VanillaNpcIds.Demon ? 120 : 140, demon.LifeMax);
            Assert.Equal(.8f, demon.KnockBackResist);
        }

        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.DemonScythe, out VanillaProjectileDefinition scythe));
        Assert.Equal((48, 48, VanillaProjectileAiStyles.DemonScythe), (scythe.Width, scythe.Height, scythe.AiStyle));
        Assert.Equal((12, 12), (scythe.CollisionWidth, scythe.CollisionHeight));
        Assert.True(scythe.TileCollide);
        Assert.True(scythe.CanCutTiles);
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.DemonScythe));

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.RedDevil, out VanillaNpcDefinition redDevil));
        Assert.Equal((28, 48, 50, 40, 600),
            (redDevil.BaseWidth, redDevil.BaseHeight, redDevil.Damage, redDevil.Defense, redDevil.LifeMax));
        Assert.Equal(.5f, redDevil.KnockBackResist);
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.RedDevilSickle, out VanillaProjectileDefinition sickle));
        Assert.Equal((16, 16, VanillaProjectileAiStyles.RedDevilSickle), (sickle.Width, sickle.Height, sickle.AiStyle));
        Assert.Equal((16, 16), (sickle.CollisionWidth, sickle.CollisionHeight));
        Assert.True(sickle.TileCollide);
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.RedDevilSickle));

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.FlyingSnake, out VanillaNpcDefinition flyingSnake));
        Assert.Equal((34, 50, 85, 28, 260),
            (flyingSnake.BaseWidth, flyingSnake.BaseHeight, flyingSnake.Damage, flyingSnake.Defense, flyingSnake.LifeMax));
        Assert.Equal(.65f, flyingSnake.KnockBackResist);
    }

    [Fact]
    public void Slimer_uses_one_generic_ai14_acceleration_pass()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.Slimer,
            directionX: 1,
            directionY: -1);

        Assert.Equal(0.1f, result.VelocityX, 5);
        Assert.Equal(-0.04f, result.VelocityY, 5);
    }

    [Fact]
    public void Flying_snake_uses_its_source_ai14_acceleration_profile()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.FlyingSnake,
            directionX: 1,
            directionY: -1);

        Assert.Equal(.2f, result.VelocityX, 5);
        Assert.Equal(-.1f, result.VelocityY, 5);
    }

    [Fact]
    public void Flying_vampire_uses_its_source_speed_and_daytime_surface_escape()
    {
        VanillaBatMotionResult1458 profile = Step(VanillaNpcIds.Vampire, directionX: 1, directionY: -1);
        Assert.Equal(.2f, profile.VelocityX, 5);
        Assert.Equal(-.2f, profile.VelocityY, 5);
        Assert.Equal(2f, profile.Ai.Ai1);

        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetWorldBounds(4200, 100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 300f, 100f, 0, true, false, false, false)]);
        NpcSnapshot vampire = Snapshot(VanillaNpcIds.Vampire);

        Assert.True(stepper.TryStepState(in vampire, out NpcStateUpdate next));

        Assert.Equal(-.2f, next.VelocityX, 5);
        Assert.Equal(-.2f, next.VelocityY, 5);
        Assert.Equal(2f, next.Ai.Ai1);
    }

    [Fact]
    public void Flying_vampire_does_not_retreat_during_an_eclipse()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetWorldBounds(4200, 100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, eclipseActive: true);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 300f, 100f, 0, true, false, false, false)]);
        NpcSnapshot vampire = Snapshot(VanillaNpcIds.Vampire);

        Assert.True(stepper.TryStepState(in vampire, out NpcStateUpdate next));

        Assert.Equal(.2f, next.VelocityX, 5);
        Assert.Equal(.2f, next.VelocityY, 5);
    }

    [Fact]
    public void Flying_snake_restores_velocity_sign_directions_when_sight_is_blocked()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetProjectileEnvironment(new BlockedEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 200f, 40f, 0, true, false, false, false)]);
        NpcSnapshot snake = Snapshot(VanillaNpcIds.FlyingSnake) with { VelocityX = -1f, VelocityY = 1f };

        Assert.True(stepper.TryStepState(in snake, out NpcStateUpdate next));

        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(-1, next.Simulation.DirectionX);
        Assert.Equal(1, next.Simulation.DirectionY);
        Assert.Equal(-1.2f, next.VelocityX, 5);
        Assert.Equal(1.1f, next.VelocityY, 5);
    }

    [Fact]
    public void Purple_queen_slime_minion_uses_its_source_acceleration_profile()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.QueenSlimeMinionPurple,
            directionX: 1,
            directionY: -1);

        Assert.Equal(0.35f, result.VelocityX, 5);
        Assert.Equal(-0.3f, result.VelocityY, 5);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(
            VanillaNpcIds.QueenSlimeMinionPurple,
            out VanillaNpcDefinition definition));
        Assert.Equal(VanillaNpcBehaviorFamily.Bat, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.BatFlight, definition.PhysicsFamily);
    }

    [Fact]
    public void Collision_rebound_precedes_closest_target_acceleration()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.CaveBat,
            velocityX: 0.25f,
            velocityY: 0.25f,
            oldVelocityX: 3f,
            oldVelocityY: -4f,
            directionX: -1,
            directionY: -1,
            collideX: true,
            collideY: true,
            closest: new VanillaBlueSlimeTargetRefresh(true, 7, 1, -1));

        Assert.Equal(-1.4f, result.VelocityX, 5);
        Assert.Equal(1.82f, result.VelocityY, 5);
        Assert.Equal(1, result.DirectionX);
        Assert.Equal(-1, result.DirectionY);
        Assert.Equal((ushort)7, result.Target);
    }

    [Fact]
    public void Hellbat_uses_its_distinct_opposing_velocity_corrections()
    {
        VanillaBatMotionResult1458 cave = Step(
            VanillaNpcIds.CaveBat,
            velocityX: 1f,
            directionX: -1,
            directionY: 0);
        VanillaBatMotionResult1458 hell = Step(
            VanillaNpcIds.Hellbat,
            velocityX: 1f,
            directionX: -1,
            directionY: 0);

        Assert.Equal(0.9f, cave.VelocityX, 5);
        Assert.Equal(0.88f, hell.VelocityX, 5);
    }

    [Fact]
    public void Wet_escape_and_wander_clock_follow_source_order()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.GiantBat,
            velocityY: 3f,
            directionX: 1,
            directionY: 1,
            ai: new NpcAiState(0f, 200f, 150f, 0f),
            wet: true);

        Assert.Equal(201f, result.Ai.Ai1);
        Assert.Equal(151f, result.Ai.Ai2);
        Assert.Equal(0.4f, result.VelocityX, 5);
        Assert.Equal(2.35f, result.VelocityY, 5);
    }

    [Fact]
    public void Harpy_uses_its_source_wet_escape_and_low_speed_wander_profile()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.Harpy,
            velocityY: 3f,
            directionX: 1,
            directionY: 1,
            ai: new NpcAiState(0f, 200f, 150f, 0f),
            wet: true);

        Assert.Equal(201f, result.Ai.Ai1);
        Assert.Equal(151f, result.Ai.Ai2);
        Assert.Equal(.22f, result.VelocityX, 5);
        Assert.Equal(2.35f, result.VelocityY, 5);
    }

    [Theory]
    [InlineData(62)]
    [InlineData(66)]
    public void Demon_shooters_use_double_acceleration_wet_escape_and_low_speed_wander(int rawType)
    {
        var type = new NpcTypeId(rawType);
        VanillaBatMotionResult1458 result = Step(
            type,
            velocityY: 3f,
            directionX: 1,
            directionY: 1,
            ai: new NpcAiState(0f, 200f, 150f, 0f),
            wet: true);

        Assert.Equal(.22f, result.VelocityX, 5);
        Assert.Equal(2.35f, result.VelocityY, 5);
        Assert.Equal(201f, result.Ai.Ai1);
        Assert.Equal(151f, result.Ai.Ai2);
    }

    [Fact]
    public void Red_devil_keeps_the_source_ordinary_bat_motion_profile()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.RedDevil,
            velocityY: 3f,
            directionX: 1,
            directionY: 1,
            wet: true);

        Assert.Equal(.1f, result.VelocityX, 5);
        Assert.Equal(3.04f, result.VelocityY, 5);
    }

    [Fact]
    public void Harpy_feather_and_timer_reset_run_only_after_the_source_state_commits()
    {
        var shotNpcs = new RuntimeNpcStore(2);
        Assert.True(shotNpcs.TrySpawn(1, HarpyUpdate(ai0: 29f), out NpcSnapshot shotSource));
        var shotProjectiles = new RuntimeProjectileStore(2);
        var shotRandom = new SequenceRandom();
        VanillaNpcTargetingAiStepper shotStepper = CreateHarpyStepper(shotRandom);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(shotNpcs, shotProjectiles).Tick(new HarpyOnly(shotStepper)).Applied);
        Assert.True(shotNpcs.TryGet(shotSource.Handle, out NpcSnapshot shotCommitted));
        Assert.Equal(30f, shotCommitted.Ai.Ai0);
        Assert.True(shotProjectiles.TryGetActive(0, out ProjectileSnapshot feather));
        Assert.Equal(VanillaProjectileIds.HarpyFeather, feather.Type);
        Assert.Equal((short)15, feather.Damage);
        Assert.Equal(105f, feather.PositionX, 5);
        Assert.Equal(110f, feather.PositionY, 5);
        Assert.Equal(2, shotRandom.Draws);
        Assert.True(shotProjectiles.TryGetServerNpcSource(feather.Handle, out NpcHandle provenance));
        Assert.Equal(shotSource.Handle, provenance);

        var resetNpcs = new RuntimeNpcStore(2);
        Assert.True(resetNpcs.TrySpawn(1, HarpyUpdate(ai0: 399f), out NpcSnapshot resetSource));
        var resetRandom = new SequenceRandom();
        VanillaNpcTargetingAiStepper resetStepper = CreateHarpyStepper(resetRandom);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(resetNpcs).Tick(new HarpyOnly(resetStepper)).Applied);
        Assert.True(resetNpcs.TryGet(resetSource.Handle, out NpcSnapshot resetCommitted));
        Assert.Equal(0f, resetCommitted.Ai.Ai0);
        Assert.Equal(1, resetRandom.Draws);

        var rejectedNpcs = new RuntimeNpcStore(2);
        Assert.True(rejectedNpcs.TrySpawn(1, HarpyUpdate(ai0: 399f), out _));
        var rejectedRandom = new SequenceRandom();
        VanillaNpcTargetingAiStepper rejectedStepper = CreateHarpyStepper(rejectedRandom);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(rejectedNpcs).Tick(new StaleHarpyOnly(rejectedStepper, rejectedNpcs)).Rejected);
        Assert.Equal(0, rejectedRandom.Draws);
    }

    [Theory]
    [InlineData(62, 19f, 120)]
    [InlineData(66, 79f, 140)]
    public void Demon_shooters_emit_source_scythes_only_after_committing_their_timer(int rawType, float ai0, int life)
    {
        var type = new NpcTypeId(rawType);
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, BatShooterUpdate(type, ai0, life), out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore(2);
        var random = new SequenceRandom();
        VanillaNpcTargetingAiStepper stepper = CreateHarpyStepper(random);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HarpyOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(ai0 + 1f, committed.Ai.Ai0);
        Assert.True(projectiles.TryGetActive(0, out ProjectileSnapshot scythe));
        Assert.Equal(VanillaProjectileIds.DemonScythe, scythe.Type);
        Assert.Equal((short)21, scythe.Damage);
        Assert.Equal(90f, scythe.PositionX, 5);
        Assert.Equal(100f, scythe.PositionY, 5);
        Assert.Equal(.2f, MathF.Sqrt(scythe.VelocityX * scythe.VelocityX + scythe.VelocityY * scythe.VelocityY), 5);
        Assert.Equal(2, random.Draws);
        Assert.True(projectiles.TryGetServerNpcSource(scythe.Handle, out NpcHandle provenance));
        Assert.Equal(source.Handle, provenance);
    }

    [Fact]
    public void Demon_timer_reset_draws_only_after_the_proposed_state_commits()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, BatShooterUpdate(VanillaNpcIds.Demon, 299f, 120), out NpcSnapshot source));
        var random = new SequenceRandom();
        VanillaNpcTargetingAiStepper stepper = CreateHarpyStepper(random);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new HarpyOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(0f, committed.Ai.Ai0);
        Assert.Equal(1, random.Draws);

        var staleNpcs = new RuntimeNpcStore(2);
        Assert.True(staleNpcs.TrySpawn(1, BatShooterUpdate(VanillaNpcIds.Demon, 299f, 120), out _));
        var staleRandom = new SequenceRandom();
        VanillaNpcTargetingAiStepper staleStepper = CreateHarpyStepper(staleRandom);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(staleNpcs).Tick(new StaleHarpyOnly(staleStepper, staleNpcs)).Rejected);
        Assert.Equal(0, staleRandom.Draws);
    }

    [Fact]
    public void Red_devil_sickle_uses_its_leading_center_offset_and_committed_timer()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, BatShooterUpdate(VanillaNpcIds.RedDevil, 99f, 600), out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore(2);
        var random = new SequenceRandom();
        VanillaNpcTargetingAiStepper stepper = CreateHarpyStepper(random);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HarpyOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(100f, committed.Ai.Ai0);
        Assert.Equal(.1f, committed.VelocityX, 5);
        Assert.Equal(-.04f, committed.VelocityY, 5);
        Assert.True(projectiles.TryGetActive(0, out ProjectileSnapshot sickle));
        Assert.Equal(VanillaProjectileIds.RedDevilSickle, sickle.Type);
        Assert.Equal((short)80, sickle.Damage);
        Assert.Equal(committed.PositionX + 14f + committed.VelocityX * 5f + sickle.VelocityX * 100f, sickle.PositionX, 5);
        Assert.Equal(committed.PositionY + 24f + committed.VelocityY * 5f + sickle.VelocityY * 100f, sickle.PositionY, 5);
        Assert.Equal(.2f, MathF.Sqrt(sickle.VelocityX * sickle.VelocityX + sickle.VelocityY * sickle.VelocityY), 5);
        Assert.Equal(2, random.Draws);
        Assert.True(projectiles.TryGetServerNpcSource(sickle.Handle, out NpcHandle provenance));
        Assert.Equal(source.Handle, provenance);
    }

    [Fact]
    public void Red_devil_timer_reset_uses_the_source_250_tick_random_threshold_after_commit()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, BatShooterUpdate(VanillaNpcIds.RedDevil, 249f, 600), out NpcSnapshot source));
        var random = new SequenceRandom();
        VanillaNpcTargetingAiStepper stepper = CreateHarpyStepper(random);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new HarpyOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(0f, committed.Ai.Ai0);
        Assert.Equal(1, random.Draws);
    }

    [Fact]
    public void Dry_visible_target_resets_pursuit_clock_but_still_advances_wander_phase()
    {
        VanillaBatMotionResult1458 result = Step(
            VanillaNpcIds.IceBat,
            directionX: 1,
            directionY: -1,
            ai: new NpcAiState(0f, 200f, 0f, 0f),
            targetDryAndVisible: true);

        Assert.Equal(0f, result.Ai.Ai1);
        Assert.Equal(1f, result.Ai.Ai2);
    }

    [Fact]
    public void Controlled_pursuit_slice_reuses_pre_wander_source_motion_and_excludes_boss_child_special_case()
    {
        var input = new VanillaBatPursuitInput1458(
            VelocityX: 0f,
            VelocityY: 0f,
            OldVelocityX: 0f,
            OldVelocityY: 0f,
            DirectionX: 1,
            DirectionY: -1,
            Target: 7,
            Wet: false,
            CollideX: false,
            CollideY: false);

        Assert.True(VanillaBatMotion1458.TryStepPursuit(
            VanillaNpcIds.CaveBat,
            in input,
            out VanillaBatPursuitResult1458 result));
        Assert.Equal(0.2f, result.VelocityX, 5); // ordinary pass + source-backed bat double-acceleration pass
        Assert.Equal(-0.08f, result.VelocityY, 5);
        Assert.Equal(1, result.DirectionX);
        Assert.Equal(-1, result.DirectionY);
        Assert.Equal((ushort)7, result.Target);

        Assert.False(VanillaBatMotion1458.TryStepPursuit(
            VanillaNpcIds.QueenSlimeMinionPurple,
            in input,
            out _));
        Assert.False(VanillaBatMotion1458.TryStepPursuit(
            VanillaNpcIds.Harpy,
            in input,
            out _));
        Assert.False(VanillaBatMotion1458.TryStepPursuit(
            VanillaNpcIds.Demon,
            in input,
            out _));
        Assert.False(VanillaBatMotion1458.TryStepPursuit(
            VanillaNpcIds.VoodooDemon,
            in input,
            out _));
        Assert.False(VanillaBatMotion1458.TryStepPursuit(
            VanillaNpcIds.RedDevil,
            in input,
            out _));
    }

    [Fact]
    public void Dispatcher_routes_bat_family_and_commits_no_gravity_state()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(7, 200f, 40f, 0, true, false, false, false)
        ]);
        NpcSnapshot bat = Snapshot(VanillaNpcIds.CaveBat) with
        {
            Ai = new NpcAiState(0f, 200f, 0f, 0f)
        };

        Assert.True(stepper.TryStepState(in bat, out NpcStateUpdate next));

        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(0f, next.Ai.Ai1);
        Assert.True(next.Simulation.NoGravity);
        Assert.False(next.Simulation.NoTileCollide);
    }

    private static VanillaBatMotionResult1458 Step(
        NpcTypeId type,
        float velocityX = 0f,
        float velocityY = 0f,
        float oldVelocityX = 0f,
        float oldVelocityY = 0f,
        int directionX = 0,
        int directionY = 0,
        bool collideX = false,
        bool collideY = false,
        bool wet = false,
        NpcAiState ai = default,
        VanillaBlueSlimeTargetRefresh closest = default,
        bool targetDryAndVisible = false)
    {
        var input = new VanillaBatMotionInput1458(
            velocityX,
            velocityY,
            oldVelocityX,
            oldVelocityY,
            directionX,
            directionY,
            byte.MaxValue,
            ai,
            wet,
            collideX,
            collideY,
            closest,
            targetDryAndVisible);
        Assert.True(VanillaBatMotion1458.TryStep(type, in input, out VanillaBatMotionResult1458 result));
        return result;
    }

    private static NpcSnapshot Snapshot(NpcTypeId type) =>
        new(
            new NpcHandle(1, new NpcGeneration(1)),
            new NpcRevision(1),
            type.Value,
            checked((short)type.Value),
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: byte.MaxValue,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                Life = 16,
                LifeMax = 16,
                DirectionX = 1,
                DirectionY = 1,
                Scale = 1f
            });

    private static VanillaNpcTargetingAiStepper CreateHarpyStepper(IVanillaNpcRandom random)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 200f, 100f, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcStateUpdate HarpyUpdate(float ai0) => new(
        VanillaNpcIds.Harpy.Value,
        (short)VanillaNpcIds.Harpy.Value,
        PositionX: 100f,
        PositionY: 100f,
        VelocityX: 0f,
        VelocityY: 0f,
        Target: 7,
        Ai: new NpcAiState(ai0, 0f, 0f, 0f),
        Simulation: NpcSimulationState.Initial with { Life = 100, LifeMax = 100, DirectionX = 1, DirectionY = 1, TimeLeft = 750 });

    private static NpcStateUpdate BatShooterUpdate(NpcTypeId type, float ai0, int life) => new(
        type.Value,
        checked((short)type.Value),
        PositionX: 100f,
        PositionY: 100f,
        VelocityX: 0f,
        VelocityY: 0f,
        Target: 7,
        Ai: new NpcAiState(ai0, 0f, 0f, 0f),
        Simulation: NpcSimulationState.Initial with { Life = life, LifeMax = life, DirectionX = 1, DirectionY = 1, TimeLeft = 750 });

    private sealed class VisibleEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(
            float sourcePositionX,
            float sourcePositionY,
            int sourceWidth,
            int sourceHeight,
            float targetPositionX,
            float targetPositionY,
            int targetWidth,
            int targetHeight) => true;
    }

    private sealed class BlockedEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(
            float sourcePositionX,
            float sourcePositionY,
            int sourceWidth,
            int sourceHeight,
            float targetPositionX,
            float targetPositionY,
            int targetWidth,
            int targetHeight) => false;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class HarpyOnly(VanillaNpcTargetingAiStepper stepper) : INpcAiStateStepper, INpcAiStatePostCommitEffect
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) => stepper.TryStepState(in npc, out next);
        public bool DefersStatePublication(in NpcSnapshot before, in NpcStateUpdate proposed) =>
            stepper.DefersStatePublication(in before, in proposed);
        public NpcSnapshot CompleteCommittedState(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.CompleteCommittedState(in before, in committed, mutations);
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.ApplyCommittedEffect(in before, in committed, mutations);
    }

    private sealed class StaleHarpyOnly(VanillaNpcTargetingAiStepper stepper, RuntimeNpcStore store)
        : INpcAiStateStepper, INpcAiStatePostCommitEffect
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (!stepper.TryStepState(in npc, out next))
                return false;
            Assert.True(store.TryUpdate(npc.Handle, in next, out _));
            return true;
        }

        public bool DefersStatePublication(in NpcSnapshot before, in NpcStateUpdate proposed) =>
            stepper.DefersStatePublication(in before, in proposed);
        public NpcSnapshot CompleteCommittedState(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.CompleteCommittedState(in before, in committed, mutations);
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.ApplyCommittedEffect(in before, in committed, mutations);
    }

    private sealed class SequenceRandom : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax) { Draws++; return inclusiveMin; }
    }
}
