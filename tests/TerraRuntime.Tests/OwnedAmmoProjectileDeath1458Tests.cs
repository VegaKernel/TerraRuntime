using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OwnedAmmoProjectileDeath1458Tests
{
    [Fact]
    public void World_bounds_and_missing_entropy_owner_do_not_sample_source_random()
    {
        var store = new RuntimeProjectileStore(2);
        var tiles = new WorldTileStore(new WorldDimensions(200, 150));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        var state = new ProjectileStateUpdate(new(5), 0, 400, 400, 8, 0, default, 0, 10, 0, 10);
        Assert.True(store.TrySpawnVanilla(in state, 1, out var born));
        // A concrete source proposal needs its real world RNG owner. Refusal
        // retains the lifetime rather than silently publishing partial effects.
        var executor = new RuntimeProjectileStateExecutor(store);
        Assert.Equal(0, executor.Tick(stepper).Applied);
        Assert.True(store.TryGet(born.Handle, out _));
        state = state with { PositionX = 0 };
        Assert.True(store.TryCommitSimulationStep(born.Handle, in state, 1, out _, out _));
        Assert.Equal(1, executor.Tick(stepper).Applied);
        Assert.Equal(0, store.ActiveCount);
    }

    [Fact]
    public void State_only_post_decorator_retains_the_concrete_source_update()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 150));
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>();
        Assert.Equal(GameplayBehaviorRegistrationResult.Registered,
            registry.TryRegister(new GameplayExtensionId("test:ammo-source-post"), new(5),
                GameplayBehaviorStage.Post, 0, new StateOnlyPost(), out _));
        registry.CommitPending();
        var state = new ProjectileStateUpdate(new(5), 0, 400, 400, 8, 0, default, 0, 10, 0, 10);
        var store = new RuntimeProjectileStore(2);
        Assert.True(store.TrySpawnVanilla(in state, 1, out var born));
        Assert.True(store.TryGetLifecycle(born.Handle, out var life));
        var context = new ProjectileSimulationStepContext(born, life, 0, 2);
        var pipeline = new RuntimeProjectileBehaviorStateStepper(new VanillaProjectileWorldStateStepper(tiles), registry);
        Assert.True(pipeline.TryStepState(in context, out var result));
        Assert.True(result.OrdinaryAmmoSourceUpdate);
        Assert.Equal(ProjectileSimulationTerminationReason.LifetimeExpired, result.TerminationReason);
    }

    private sealed class StateOnlyPost : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext context, out ProjectileSimulationStepResult next)
        {
            var p = context.Projectile;
            next = new(new(p.Type, p.Spawner, p.PositionX, p.PositionY, p.VelocityX, p.VelocityY,
                p.Ai, p.BannerIdToRespondTo, p.Damage, p.KnockBack, p.OriginalDamage), context.Lifecycle.TimeLeft);
            return true;
        }
    }

    [Fact]
    public void Accepted_entropy_precedes_throwing_publication_and_is_not_repeated()
    {
        var observer = new ThrowingRemoval();
        var store = new RuntimeProjectileStore(2, commitSink: observer);
        var random = new VanillaUnifiedRandom1458(7);
        var effects = new RuntimeProjectileSimulationCommitSink(new RuntimeProjectileLiveChildSpawnQueue(2),
            new RuntimeCultistLightningArcTrailRegistry(2), ordinaryAmmoRandom: random);
        var executor = new RuntimeProjectileStateExecutor(store, effects);
        var tiles = new WorldTileStore(new WorldDimensions(200, 150));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        var state = new ProjectileStateUpdate(new(5), 0, 400, 400, 8, 0, default, 0, 10, 0, 10);
        Assert.True(store.TrySpawnVanilla(in state, 1, out var born));
        var expected = random.Clone(); AdvanceOriginalBranch(expected, 5, false);
        Assert.Throws<InvalidOperationException>(() => executor.Tick(stepper));
        Assert.False(store.TryGet(born.Handle, out _));
        Assert.True(random.HasSameState(expected));
        Assert.Equal(1, observer.Removals);
        executor.Tick(stepper);
        Assert.True(random.HasSameState(expected)); Assert.Equal(1, observer.Removals);
        // The reused physical slot has a distinct lifetime. An old handle
        // cannot spend its source offers or remove the replacement.
        Assert.True(store.TrySpawn(0, in state, out var replacement));
        Assert.NotEqual(born.Handle, replacement.Handle);
        Assert.False(store.TryCommitSimulationStep(born.Handle, in state, 0, out _, out _));
        Assert.True(random.HasSameState(expected));
    }

    private sealed class ThrowingRemoval : IProjectileStateCommitSink
    {
        public int Removals;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            if (kind != ProjectileStateCommitKind.Remove) return;
            Removals++; throw new InvalidOperationException("observer");
        }
    }

    [Fact]
    public void Official_timeout_and_tile_collision_checkpoints_match_owned_production_updates()
    {
        foreach (string edition in new[] { "Linux", "Windows" })
        {
            using var source = Read(edition);
            foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
            {
                int type = row.GetProperty("type").GetInt32();
                bool collision = row.GetProperty("cause").GetString() == "collision";
                int seed = row.GetProperty("seed").GetInt32();
                // noDrop has no producer for these six bodies: both original
                // values must prove the same empty item outcome and entropy.
                Assert.Empty(row.GetProperty("drops").EnumerateArray());
                var original = new VanillaUnifiedRandom1458(seed);
                SameRandom(row.GetProperty("beforeRandom"), original);
                AdvanceOriginalBranch(original, type, collision);
                SameRandom(row.GetProperty("afterRandom"), original);
                using var f = new LocalWeaponClockRuntimeChecks1458.Fixture(false, seed, false);
                if (collision) f.Tiles.Set(26, 25, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                var state = new ProjectileStateUpdate(new((ushort)type), 0, 400, 400, 8, 0, default, 0, 10, 0, 10);
                var store = Store(f.Projectiles);
                Assert.True(store.TrySpawnVanilla(in state, collision ? 120 : 1, out _));
                var expected = f.GameplayRandom.Clone();
                AdvanceOriginalBranch(expected, type, collision);
                Assert.True(f.Projectiles.TryTickState());
                Assert.Equal(0, store.ActiveCount);
                Assert.True(f.GameplayRandom.HasSameState(expected), $"{edition} type {type} {collision} seed {seed}");
                Assert.Equal(0, f.Items.ActiveCount);
                var after = f.GameplayRandom.Clone();
                f.Projectiles.TryTickState();
                Assert.True(f.GameplayRandom.HasSameState(after));
                Assert.Equal(0, f.Items.ActiveCount);
            }
        }
    }

    private static void AdvanceOriginalBranch(VanillaUnifiedRandom1458 random, int type, bool collision)
    {
        if (type == 981)
        {
            random.Next(6);
            // The four-pixel bullet reaches this obstacle on subupdate two.
            if (collision) random.Next(6);
        }
        if (collision) random.Next(3);
        else if (type is 4 or 5) random.Next(type == 4 ? 5 : 3);
        if (type == 5) for (int index = 0; index < 60; index++) random.Next(3);
    }
    [Fact]
    public void Paid_Jester_expiry_keeps_original_terminal_random_and_cannot_invent_recovered_inventory()
    {
        foreach(bool funded in new[]{false,true})
        {
            using var source=Read();
            var row=source.RootElement.GetProperty("rows").EnumerateArray().Single(r=>r.GetProperty("type").GetInt32()==5 &&
                r.GetProperty("cause").GetString()=="timeout" && r.GetProperty("seed").GetInt32()==7 && !r.GetProperty("noDrop").GetBoolean());
            using var f=new LocalWeaponClockRuntimeChecks1458.Fixture(false,7,false);
            f.Equip(7,39,1);f.Equip(54,51,1);if(funded)f.Fund(54,1);
            f.Control(7,false);f.State.Tick();f.Control(7,true);f.State.Tick();
            Assert.True(f.Item(54).IsEmpty);Assert.Equal(0,f.Funded(54));
            var report=new TerrariaProjectileUpdateState(new(f.Player.Slot.Value,951,1),5,
                f.Member.PositionX+5,f.Member.PositionY+16,6.6f,0,0,0,0,0,14,4f,0);
            f.AmmoReport(in report);Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);
            var store=Store(f.Projectiles);var active=new ProjectileSnapshot[4];Assert.Equal(1,store.CopyActive(active));
            var p=active[0];var state=new ProjectileStateUpdate(p.Type,p.Spawner,p.PositionX,p.PositionY,p.VelocityX,p.VelocityY,
                p.Ai,p.BannerIdToRespondTo,p.Damage,p.KnockBack,p.OriginalDamage);
            Assert.True(store.TryCommitSimulationStep(p.Handle,in state,1,out _,out bool expired));Assert.False(expired);
            // The independently captured Update has one post-move offer and
            // sixty Kill arguments. Prove the complete source checkpoint,
            // then project that fixed source branch onto the live preimage;
            // earlier full runtime ticks keep their separate world offers.
            var original=new VanillaUnifiedRandom1458(7);SameRandom(row.GetProperty("beforeRandom"),original);
            for(int offer=0;offer<61;offer++)original.Next(3);
            SameRandom(row.GetProperty("afterRandom"),original);
            var expected=f.GameplayRandom.Clone();for(int offer=0;offer<61;offer++)expected.Next(3);
            Assert.True(f.Projectiles.TryTickState());Assert.Equal(0,store.ActiveCount);
            Assert.True(f.GameplayRandom.HasSameState(expected),"The accepted expiry must adopt the complete original Update/Kill entropy.");
            Assert.Empty(row.GetProperty("drops").EnumerateArray());Assert.Equal(0,f.Items.ActiveCount);
            Assert.True(f.Item(54).IsEmpty);Assert.Equal(0,f.Funded(54));
            var after=f.GameplayRandom.Clone();f.AmmoReport(in report);
            Assert.Equal(1,f.Projectiles.PromotedClientProjectileSpawns);Assert.Equal(0,store.ActiveCount);
            Assert.True(f.GameplayRandom.HasSameState(after));Assert.Equal(0,f.Items.ActiveCount);
        }
    }

    private static JsonDocument Read(string edition = "Linux")
    {
        using var data=typeof(OwnedAmmoProjectileDeath1458Tests).Assembly.GetManifestResourceStream($"AmmoProjectileDeath{edition}1458")!;
        using var zip=new GZipStream(data,CompressionMode.Decompress);return JsonDocument.Parse(zip);
    }
    private static void SameRandom(JsonElement expected,VanillaUnifiedRandom1458 actual)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),Cursor(actual));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(e=>e.GetInt32()),Cells(actual));
        Assert.Equal(expected.GetProperty("next").GetInt32(),actual.Clone().Next());
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="projectiles")]
    private static extern ref RuntimeProjectileStore Store(ProjectileAuthority authority);
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="seedArray")]
    private static extern ref int[] Cells(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
}
