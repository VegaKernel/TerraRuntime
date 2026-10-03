using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class EclipseProjectileTermination1458Tests
{
    [Fact]
    public void Source_Kill_origin_survives_a_post_decorator_that_only_projects_state()
    {
        var registry=new RuntimeGameplayBehaviorRegistry<ProjectileTypeId,IProjectileStateStepper>();
        Assert.Equal(GameplayBehaviorRegistrationResult.Registered,registry.TryRegister(new GameplayExtensionId("test:eclipse-post"),
            VanillaProjectileIds.DrManFlyFlask,GameplayBehaviorStage.Post,0,new PostStepper(),out _));
        registry.CommitPending();
        var shot=EclipseProjectile1458Tests.Shot(501);
        var context=new ProjectileSimulationStepContext(shot,new ProjectileLifecycleState(3600,false),0,1);
        var pipeline=new RuntimeProjectileBehaviorStateStepper(new VanillaProjectileWorldStateStepper(EclipseProjectile1458Tests.Terrain("wall")),registry);
        Assert.True(pipeline.TryStepState(in context,out var result));
        Assert.NotNull(result.KillOrigin);
        EclipseProjectile1458Tests.Close(1009,result.KillOrigin.Value.X);
        EclipseProjectile1458Tests.Close(1116.15f,result.KillOrigin.Value.Y);
        Assert.Equal(ProjectileSimulationTerminationReason.TileCollision,result.TerminationReason);
    }

    [Fact]
    public void Accepted_lifetime_expiry_uses_final_center_without_inventing_collision_origin()
    {
        var random=new VanillaUnifiedRandom1458(1458);var queue=new RuntimeProjectileExplosionQueue(2,random);
        var store=new RuntimeProjectileStore(2);var state=EclipseProjectile1458Tests.State(EclipseProjectile1458Tests.Shot(501));
        Assert.True(store.TrySpawn(0,in state,out var shot));Assert.True(store.TrySetServerNpcSource(shot.Handle,new NpcHandle(3,new NpcGeneration(2))));
        var executor=new RuntimeProjectileStateExecutor(store,terminationSink:queue);
        executor.Tick(new LifetimeStepper());
        var effect=Assert.Single(queue.Events.ToArray());Assert.Equal(952,effect.Left);Assert.Equal(1064,effect.Top);
        Assert.Equal(1249478542,random.Next());
    }

    [Fact]
    public void Nail_termination_has_no_flask_death_effect_or_random_arguments()
    {
        var random=new VanillaUnifiedRandom1458(1458);var queue=new RuntimeProjectileExplosionQueue(2,random);
        var store=new RuntimeProjectileStore(2);var state=EclipseProjectile1458Tests.State(EclipseProjectile1458Tests.Shot(498));
        Assert.True(store.TrySpawn(0,in state,out var shot));Assert.True(store.TrySetServerNpcSource(shot.Handle,new NpcHandle(3,new NpcGeneration(2))));
        new RuntimeProjectileStateExecutor(store,terminationSink:queue).Tick(new VanillaProjectileWorldStateStepper(EclipseProjectile1458Tests.Terrain("wall")));
        Assert.Empty(queue.Events.ToArray());Assert.Equal(906992634,random.Next());
    }
    [Fact]
    public void Original_Kill_preserves_damage_and_knockback_and_consumes_fifteen_argument_draws()
    {
        var random=new VanillaUnifiedRandom1458(1458);var queue=new RuntimeProjectileExplosionQueue(2,random);
        var shot=EclipseProjectile1458Tests.Shot(501);
        var commit=new ProjectileTerminationCommit(shot,shot,ProjectileSimulationTerminationReason.LifetimeExpired,false,default,new NpcHandle(3,new NpcGeneration(2)));
        queue.ProjectileTerminated(in commit);
        var effect=Assert.Single(queue.Events.ToArray());
        Assert.Equal(135,effect.Width);Assert.Equal(135,effect.Height);
        Assert.Equal(940,effect.Left);Assert.Equal(1040,effect.Top);
        Assert.Equal(37,effect.Projectile.Damage);Assert.Equal(2.5f,effect.Projectile.KnockBack);
        Assert.Equal(1249478542,random.Next()); // independently captured full original Projectile.Kill()
    }

    [Theory]
    [InlineData("wall",1009f,1116.15f,949f,1056.15f)]
    [InlineData("floor",1023.88f,1105f,963.88f,1045f)]
    public void Accepted_collision_uses_source_Kill_position_before_common_movement_tail(string terrain,float x,float y,float left,float top)
    {
        var random=new VanillaUnifiedRandom1458(1458);var queue=new RuntimeProjectileExplosionQueue(2,random);
        var store=new RuntimeProjectileStore(2);var state=EclipseProjectile1458Tests.State(EclipseProjectile1458Tests.Shot(501));
        Assert.True(store.TrySpawn(0,in state,out var shot));Assert.True(store.TrySetServerNpcSource(shot.Handle,new NpcHandle(3,new NpcGeneration(2))));
        var executor=new RuntimeProjectileStateExecutor(store,terminationSink:queue);
        var result=executor.Tick(new VanillaProjectileWorldStateStepper(EclipseProjectile1458Tests.Terrain(terrain)));
        Assert.Equal(1,result.Applied);Assert.False(store.TryGet(shot.Handle,out _));
        var effect=Assert.Single(queue.Events.ToArray());
        EclipseProjectile1458Tests.Close(left,effect.Left);EclipseProjectile1458Tests.Close(top,effect.Top);
        EclipseProjectile1458Tests.Close(x+7.5f,effect.CenterX);EclipseProjectile1458Tests.Close(y+7.5f,effect.CenterY);
        executor.Tick(new VanillaProjectileWorldStateStepper(EclipseProjectile1458Tests.Terrain(terrain)));
        Assert.Single(queue.Events.ToArray());Assert.Equal(1249478542,random.Next());
    }

    [Theory]
    [InlineData(0)] // no provenance
    [InlineData(1)] // world bounds is removal, not Kill
    [InlineData(2)] // invalid proposed Kill origin
    [InlineData(3)] // retained/live state cannot carry a Kill origin
    [InlineData(4)] // reentrant slot reuse before commit
    [InlineData(5)] // non-default ai2 unsupported
    [InlineData(6)] // infinite proposed origin
    [InlineData(7)] // negative-infinite proposed origin
    public void Rejected_or_non_Kill_paths_do_not_emit_or_advance_termination_random(int kind)
    {
        var random=new VanillaUnifiedRandom1458(1458);var queue=new RuntimeProjectileExplosionQueue(2,random);
        var store=new RuntimeProjectileStore(2);var shot=EclipseProjectile1458Tests.Shot(501);
        if(kind==1)shot=shot with{PositionX=0};
        if(kind==5)shot=shot with{Ai=new ProjectileAiState(17,0,1)};
        var state=EclipseProjectile1458Tests.State(shot);Assert.True(store.TrySpawn(0,in state,out shot));
        if(kind!=0)Assert.True(store.TrySetServerNpcSource(shot.Handle,new NpcHandle(3,new NpcGeneration(2))));
        var executor=new RuntimeProjectileStateExecutor(store,terminationSink:queue);
        IProjectileStateStepper stepper=kind is 2 or 3 or 4 or 6 or 7?new InvalidOrStaleStepper(store,kind):new VanillaProjectileWorldStateStepper(EclipseProjectile1458Tests.Terrain("wall"));
        executor.Tick(stepper);
        Assert.Empty(queue.Events.ToArray());Assert.Equal(906992634,random.Next());
        if(kind is 2 or 3 or 6 or 7)Assert.True(store.TryGet(shot.Handle,out _));
        if(kind==4){var active=new ProjectileSnapshot[2];Assert.Equal(1,store.CopyActive(active));Assert.NotEqual(shot.Handle,active[0].Handle);}
    }

    [Theory]
    [InlineData(929f,1000)]
    [InlineData(930f,926)]
    [InlineData(938f,926)]
    [InlineData(1070f,926)]
    [InlineData(1083f,926)]
    [InlineData(1084f,1000)]
    [InlineData(1090f,1000)]
    public void Real_hostile_damage_pass_matches_original_client_burst_edges(float targetX,int expectedLife)
    {
        var random=new VanillaUnifiedRandom1458(1458);var queue=new RuntimeProjectileExplosionQueue(2,random);
        var store=new RuntimeProjectileStore(2);var state=EclipseProjectile1458Tests.State(EclipseProjectile1458Tests.Shot(501));
        var npc=new NpcHandle(3,new NpcGeneration(2));
        Assert.True(store.TrySpawn(0,in state,out var shot));Assert.True(store.TrySetServerNpcSource(shot.Handle,npc));
        var slots=new PlayerSlotPool(1);Assert.True(slots.TryAcquireConnection(out var lease));using var session=new PlayerJoinSession(lease!);
        session.ObserveWorldRequest();session.ObserveSectionRequest();
        var connection=new ConnectionHandle(GameCommandSourceId.FromConnection(23),session.Handle);
        var players=new PlayerAuthority(null,null);
        players.TryApply(new PlayerSpawnRuntimeCommand(connection,session,new PlayerSpawnCommitRequest(session.Slot,10,10,0,0,0,0,0)));
        players.TryApply(new PlayerHealthRuntimeCommand(connection,new PlayerHealthCommitRequest(session.Slot,1000,1000)));
        players.TryApply(new PlayerEquipmentRuntimeCommand(connection,new PlayerEquipmentCommitRequest(session.Slot,VanillaPlayerItemSlotCatalog.ArmorStart,0,0,0,0)));
        players.TryApply(new PlayerMovementRuntimeCommand(connection,new PlayerMovementCommitRequest(session.Slot,0,0,0,0,0,targetX,1090,false,0,0,false,0,false,0,0,0,0,false,0,0)));
        new RuntimeProjectileStateExecutor(store,terminationSink:queue).Tick(new VanillaProjectileWorldStateStepper(EclipseProjectile1458Tests.Terrain("wall")));
        var pass=new RuntimeProjectilePlayerCombatPass(store,new RuntimeNpcStore(),players,()=>1,new ZeroVariationRandom());
        pass.Tick(queue.Events);
        Assert.True(players.TryCapture(connection.Player,out var target));Assert.Equal(expectedLife,target.Life);
        Assert.Equal(expectedLife==926?1:0,pass.HostileCommittedHits);
    }

    private sealed class ZeroVariationRandom:Random{public override int Next(int minValue,int maxValue)=>0;}
    private sealed class PostStepper:IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext current,out ProjectileSimulationStepResult next)
        {next=new(EclipseProjectile1458Tests.State(current.Projectile),current.Lifecycle.TimeLeft);return true;}
    }
    private sealed class LifetimeStepper:IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext current,out ProjectileSimulationStepResult next)
        {next=new(EclipseProjectile1458Tests.State(current.Projectile with{PositionX=1012,PositionY=1124}),0);return true;}
    }
    private sealed class InvalidOrStaleStepper(RuntimeProjectileStore store,int kind):IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext current,out ProjectileSimulationStepResult next)
        {
            if(kind==4){store.TryDespawn(current.Projectile.Handle,out _);var replacement=EclipseProjectile1458Tests.State(current.Projectile);store.TrySpawn(0,in replacement,out _);}
            next=new(EclipseProjectile1458Tests.State(current.Projectile),kind==3?100:0,TerminationReason:kind==3?ProjectileSimulationTerminationReason.None:ProjectileSimulationTerminationReason.TileCollision,
                KillOrigin:new ProjectileKillOrigin(kind==2?float.NaN:kind==6?float.PositiveInfinity:kind==7?float.NegativeInfinity:1009,1116.15f));return true;
        }
    }
}
