using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ProjectilePhysicalSentinel1458Tests
{
    // Official Main.UpdateWorld_Projectiles iterates [0,1000), although NewProjectile can initialize
    // and publish physical slot1000. These are actual owned runtime phases, not a capacity-failure test.
    [Fact]
    public void World_tick_retains_overflow_birth_without_aging_damage_or_damage_rng()
    {
        foreach (bool targetNpc in new[] { false, true })
        foreach (ushort slot in new ushort[] { 999, 1000 })
        {
            var npcs = new RuntimeNpcStore(2);
            var shots = new RuntimeProjectileStore();
            var stepper = new Stepper();
            var random = new CountingRandom();
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            Assert.True(tiles.TryAttachWorldSurface(80));
            // The real movement ingress rejects the old (100,100) pose with world borders.
            // Keep all collision participants in the admitted interior and own a separate
            // empty-selected player phase, which derives neutral crit4 without borrowing hit RNG.
            var state = new ServerRuntimeState(npcs: npcs, npcAiStepper: new IdleNpc(), projectiles: shots,
                projectileStepper: stepper, projectilePlayerCombatRandom: random, worldTiles: tiles,
                playerUpdateRandomSeed: new(0),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80, RockLayer = 150 });
            var runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(state)!;
            runtime.Players.SetRemotePlayerEnvironment(new(false, false), shots);
            runtime.Players.SetNpcHealthWorldFacts(() => new(false, false));
            using var fixture = new Players(state.Apply, 1000, 1000, normalGravity: true);
            Assert.True(state.TryCapturePlayerSnapshot(fixture.Target.Player, out var reported));
            Assert.Equal(1000f, reported.PositionX);
            Assert.Equal(1000f, reported.PositionY);
            NpcSnapshot npc = default;
            if (targetNpc) Assert.True(npcs.TrySpawn(0, Actor() with { PositionX = 1000, PositionY = 1000 }, out npc));
            var shotState = Shot(14, fixture.Owner.Player.Slot.Value) with { PositionX = 1000, PositionY = 1000 };
            Assert.True(shots.TrySpawn(slot, shotState, out var shot));
            Assert.True(shots.TryMarkCombatTrusted(shot.Handle, fixture.Owner.Player));
            Assert.True(shots.TryGetLifecycle(shot.Handle, out var before));
            state.Tick();
            Assert.True(runtime.Players.TryGet(fixture.Owner, out var owner));
            Assert.Equal(PlayerDerivedCritState1458.SourceBaseline, owner.ItemPhase!.Value.DerivedCrit);
            if (slot == 1000)
            {
                Assert.Equal(0, stepper.Calls);
                Assert.Equal(0, random.Draws);
                Assert.True(shots.TryGet(shot.Handle, out var after));
                Assert.Equal(shot, after);
                Assert.True(shots.TryGetLifecycle(shot.Handle, out var lifecycle));
                Assert.Equal(before, lifecycle);
                Assert.True(state.TryCapturePlayerSnapshot(fixture.Target.Player, out var target));
                Assert.Equal(1000, target.Life);
                if (targetNpc) { Assert.True(npcs.TryGet(npc.Handle, out var actor)); Assert.Equal(100, actor.Simulation.Life); }
            }
            else
            {
                Assert.True(stepper.Calls > 0, "Physical999 must execute the configured stepper.");
                if (targetNpc)
                {
                    Assert.True(npcs.TryGet(npc.Handle, out var actor));
                    Assert.True(actor.Simulation.Life < 100,
                        $"NPC hit missing at ({actor.PositionX},{actor.PositionY}); projectile retained={shots.TryGet(shot.Handle, out _)}.");
                }
                else
                {
                    Assert.True(state.TryCapturePlayerSnapshot(fixture.Target.Player, out var target));
                    Assert.True(random.Draws > 0,
                        $"Physical999 must reach PvP RNG: ownerHostile={owner.Hostile}, ownerDead={owner.IsDead}, targetHostile={target.Hostile}, targetDead={target.IsDead}, targetHealth={target.HasHealth}, targetLife={target.Life}, targetPosition=({target.PositionX},{target.PositionY}), ownerCombat={runtime.Players.TryCaptureCombatSnapshot(fixture.Owner, out _)}.");
                    Assert.True(target.Life < 1000);
                }
            }
        }
    }

    [Fact]
    public void Explosion_and_hostile_collision_lanes_also_exclude_physical_overflow()
    {
        foreach (int kind in new[] { 0, 1, 2, 3 }) // friendly explosion PvP/NPC; hostile ordinary/explosion
        foreach (ushort slot in new ushort[] { 999, 1000 })
        {
            var players = new PlayerAuthority(null, null);
            using var fixture = new Players(command => players.TryApply(command));
            var npcs = new RuntimeNpcStore(2);
            Assert.True(npcs.TrySpawn(0, Actor(), out var npc));
            var shots = new RuntimeProjectileStore();
            bool hostile = kind >= 2;
            var shotState = Shot(hostile ? VanillaProjectileIds.CultistBossFireBall.Value : 134,
                hostile ? byte.MaxValue : fixture.Owner.Player.Slot.Value);
            Assert.True(shots.TrySpawn(slot, shotState, out var shot));
            if (hostile) Assert.True(shots.TrySetServerNpcSource(shot.Handle, npc.Handle));
            else Assert.True(shots.TryMarkCombatTrusted(shot.Handle, fixture.Owner.Player));
            var random = new CountingRandom();
            var pass = new RuntimeProjectilePlayerCombatPass(shots, npcs, players, () => 1, random);
            var explosion = new RuntimeProjectileExplosionEvent(shot, hostile ? default : fixture.Owner.Player,
                hostile ? npc.Handle : default, 90, 90, 100, 100);
            if (kind != 2) Assert.True(shots.TryDespawn(shot.Handle, out _));
            if (kind == 1)
            {
                var items = new RuntimeWorldItemStore();
                var combat = new RuntimeNpcNetworkCombatPipeline(npcs, items, new RuntimePlayerSnapshotLookup(players, null), players,
                    () => 1, null, new(items), null, null, new(), false, false);
                new RuntimeProjectileNpcCombatPass(shots, npcs, combat, players, () => 1).TickExplosions([explosion]);
                Assert.True(npcs.TryGet(npc.Handle, out var current));
                Assert.Equal(slot == 1000, current.Simulation.Life == 100);
            }
            else
            {
                pass.Tick(kind == 2 ? [] : [explosion]);
                Assert.True(players.TryCapture(fixture.Target.Player, out var target));
                Assert.Equal(slot == 1000, target.Life == 1000);
                Assert.Equal(slot == 1000, random.Draws == 0);
            }
        }
    }

    private static NpcStateUpdate Actor() => new(3, 3, 100, 100, 0, 0, 0, default,
        // Fresh official NPC constructor declares shimmerTransparency and lifeRegenCount
        // without initializers (source zero); a manual Initial import otherwise leaves them unknown.
        NpcSimulationState.Initial with { Life = 100, LifeMax = 100, Immortal = false, MoneyValue = 0,
            DamageOverride = 0, ShimmerTransparency = 0, LifeRegenCounter = 0 });
    private static ProjectileStateUpdate Shot(int type, byte owner) => new(new(type), owner, 100, 100, 0, 0, default, 0, 20, 0, 20);
    private sealed class CountingRandom : Random
    {
        internal int Draws;
        public override int Next(int minimum, int maximum) { Draws++; return Math.Clamp(0, minimum, maximum - 1); }
    }
    private sealed class IdleNpc : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
    private sealed class Stepper : IProjectileStateStepper
    {
        internal int Calls;
        public bool TryStepState(in ProjectileSimulationStepContext context, out ProjectileSimulationStepResult next)
        {
            Calls++;
            var p = context.Projectile;
            next = new(new(p.Type, p.Spawner, p.PositionX, p.PositionY, p.VelocityX, p.VelocityY, p.Ai,
                p.BannerIdToRespondTo, p.Damage, p.KnockBack, p.OriginalDamage), context.Lifecycle.TimeLeft - 1);
            return true;
        }
    }
    private sealed class Players : IDisposable
    {
        private readonly PlayerSlotPool slots = new(3);
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly Action<RuntimeCommand> apply;
        private readonly float positionX, positionY;
        private readonly byte movementFlags;
        internal readonly ConnectionHandle Owner, Target;
        internal Players(Action<RuntimeCommand> apply, float positionX = 100, float positionY = 100, bool normalGravity = false)
        {
            this.apply = apply; this.positionX = positionX; this.positionY = positionY;
            movementFlags = normalGravity ? (byte)16 : (byte)0;
            Owner = Join(1); Target = Join(2);
        }
        private ConnectionHandle Join(long id)
        {
            Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(id), session.Handle);
            apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 10, 10, 0, 0, 0, 0, 0)));
            apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 1000, 1000)));
            apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 59, 0, 0, 0, 0)));
            apply(new PlayerPvpToggleRuntimeCommand(connection, true));
            apply(new PlayerMovementRuntimeCommand(connection, new(session.Slot, 0, movementFlags, 0, 0, 0, positionX, positionY,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            return connection;
        }
        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
}
