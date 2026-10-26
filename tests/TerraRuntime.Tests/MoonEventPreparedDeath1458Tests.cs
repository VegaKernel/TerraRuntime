using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonEventPreparedDeath1458Tests
{
    [Fact]
    public void Raw_client_event_damage_never_uses_ranged_compatibility_fallback_for_progress_or_rewards()
    {
        Span<PlayerSlotId> participants = stackalloc PlayerSlotId[255];
        foreach (int type in new[] { 27, 212, 305, 338, 325, 327, 344, 345, 346 })
        {
            var players = new PlayerAuthority(null, null);
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1458), session.Handle);
            Assert.True(players.TryApply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 400, 400))));
            Assert.True(players.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
                new(session.Slot, 40, 30, 0, 0, 0, 0, 0))));
            // Bow39 deliberately reaches the old UnsupportedWeapon compatibility path.
            Assert.True(players.TryApply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 1, 0, 39, 0))));
            Assert.True(players.TryApply(new PlayerItemAnimationRuntimeCommand(connection, 0, 20)));
            var npcs = new RuntimeNpcStore(10);
            var items = new RuntimeWorldItemStore();
            var random = new VanillaUnifiedRandom1458(1458);
            var clock = new RuntimeWorldClock(0, false, 0, 0, 0);
            if (type is 305 or 325 or 327) Assert.True(clock.TryStartPumpkinMoon());
            if (type is 338 or 344 or 345 or 346) Assert.True(clock.TryStartSnowMoon());
            long beforeRevision = clock.MoonEventProgressRevision;
            int group = VanillaInvasionLifecycle1458.GetGroup(type);
            var invasion = new RuntimeWorldInvasion1458(new(group, 12, 120, 0, 50, 0,
                0, 120, group + 3, 0, false, false, false, false));
            var update = new NpcStateUpdate(type, (short)type, 600, 400, 0, 0, 0,
                default, NpcSimulationState.Initial);
            Assert.True(npcs.TrySpawnVanilla(in update, out var target));
            var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items,
                new RuntimePlayerSnapshotLookup(players, null), players, () => 0, null, new(items), null,
                clock, new(), false, false, lootRandom: random, invasion: invasion,
                invasionProgressPublisher: _ => Assert.Fail("Untrusted death published invasion progress."));
            var beforeRandom = random.Clone();
            Assert.True(invasion.TryCapture(out var beforeInvasion));
            var wire = new TerrariaNpcDamageState(target.Handle.Slot,
                RuntimeNpcPacketProjection.ToProtocolGeneration(target.Handle.Generation), short.MaxValue, 0, 1, 0);
            for (int attempt = 0; attempt < 3; attempt++)
                Assert.Equal(RuntimeNpcNetworkDamageResult.Rejected, pipeline.TryApply(connection, in wire));
            Assert.True(npcs.TryGet(target.Handle, out var current));
            Assert.Equal(target, current);
            Assert.True(random.HasSameState(beforeRandom));
            Assert.True(invasion.IsCurrent(in beforeInvasion));
            Assert.Equal(0, items.ActiveCount);
            Assert.Equal(0f, clock.MoonEventWaveKills);
            Assert.Equal(beforeRevision, clock.MoonEventProgressRevision);
            Assert.True(pipeline.Interactions.TryCopyInteractingSlots(target.Handle, participants, out int count));
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public void Prepared_projectile_deaths_adopt_source_moon_wave_before_observers_once_per_generation()
    {
        // TerrariaServer 1.4.5.8 NPC.checkDead -> CheckProgressFrostMoon/PumpkinMoon:
        // NPC338 and NPC305 each contribute one base point, scaled by 1/2/2.5.
        // Wave one needs 25; surplus is discarded on crossing the threshold.
        foreach (int type in new[] { 305, 338 })
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var fixture = new Fixture(type, mode);
            for (int count = 0; count < 24; count++)
                Assert.True(fixture.Clock.TryAdvanceMoonEventDeath(new(type), false, false));
            float scalar = mode == 2 ? 2.5f : mode == 1 ? 2f : 1f;
            int callbacks = 0;
            var target = fixture.Spawn(type);
            var before = fixture.Random.Clone();
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, fixture.Strike(in target, () =>
            {
                callbacks++;
                Assert.Equal(2, fixture.Clock.MoonEventWaveNumber);
                Assert.Equal(0f, fixture.Clock.MoonEventWaveKills);
                Assert.Equal(24f + scalar, fixture.Clock.MoonEventTotalInvasionPoints);
                Assert.Equal(fixture.StartRevision + 25, fixture.Clock.MoonEventProgressRevision);
            }));
            Assert.Equal(1, callbacks);
            Assert.False(fixture.Npcs.TryGet(target.Handle, out _));
            var after = fixture.Random.Clone();
            Assert.False(before.HasSameState(after));
            Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, fixture.Strike(in target, () => callbacks++));
            Assert.Equal(1, callbacks);
            Assert.True(fixture.Random.HasSameState(after));
            Assert.Equal(fixture.StartRevision + 25, fixture.Clock.MoonEventProgressRevision);
        }
    }

    [Fact]
    public void Prepared_death_publication_preserves_newer_moon_progress_and_replacement_generation()
    {
        foreach (int type in new[] { 305, 338 })
        {
            var fixture = new Fixture(type, 0);
            var target = fixture.Spawn(type);
            NpcSnapshot replacement = default;
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, fixture.Strike(in target, () =>
            {
                Assert.Equal(1f, fixture.Clock.MoonEventWaveKills);
                Assert.True(fixture.Clock.TryAdvanceMoonEventDeath(new(type), false, false));
                Assert.False(fixture.Npcs.TryDespawn(target.Handle)); // Accepted Moon terminal precedes observers.
                // Exact-slot replacement retains the already verified original spawn facts;
                // the ordinary allocator may deliberately skip its protected corpse slot.
                var next = new NpcStateUpdate(target.Type, target.NetId, target.PositionX, target.PositionY,
                    target.VelocityX, target.VelocityY, target.Target, target.Ai, target.Simulation);
                Assert.True(fixture.Npcs.TrySpawn(target.Handle.Slot, in next, out replacement));
            }));
            Assert.Equal(2f, fixture.Clock.MoonEventWaveKills);
            Assert.Equal(fixture.StartRevision + 2, fixture.Clock.MoonEventProgressRevision);
            Assert.True(fixture.Npcs.TryGet(replacement.Handle, out var current));
            Assert.Equal(replacement, current);
            Assert.Equal(target.Handle.Slot, replacement.Handle.Slot);
            Assert.NotEqual(target.Handle.Generation, replacement.Handle.Generation);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, fixture.Strike(in target, static () => { }));
            Assert.Equal(2f, fixture.Clock.MoonEventWaveKills);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, fixture.Strike(in replacement, static () => { }));
            Assert.Equal(3f, fixture.Clock.MoonEventWaveKills);
            Assert.Equal(fixture.StartRevision + 3, fixture.Clock.MoonEventProgressRevision);
        }
    }

    [Fact]
    public void Stale_prepared_owner_rejects_without_health_random_or_moon_credit()
    {
        var fixture = new Fixture(338, 0);
        var target = fixture.Spawn(338);
        var before = fixture.Random.Clone();
        var shot = fixture.Projectile;
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected,
            fixture.Pipeline.TryStrikePreparedProjectile(in shot, in target, 0, 100000, 0,
                false, before, before.Clone(), static () => false, _ => Assert.Fail("Rejected death published.")));
        Assert.True(fixture.Npcs.TryGet(target.Handle, out var current));
        Assert.Equal(target, current);
        Assert.True(fixture.Random.HasSameState(before));
        Assert.Equal(fixture.StartRevision, fixture.Clock.MoonEventProgressRevision);
        Assert.Equal(0f, fixture.Clock.MoonEventWaveKills);
    }

    [Fact]
    public void Environment_and_server_melee_deaths_commit_moon_credit_before_owned_money_publication()
    {
        foreach (int type in new[] { 305, 338 })
        foreach (bool environment in new[] { false, true })
        {
            var fixture = new Fixture(type, 0);
            var target = fixture.Spawn(type);
            // Source NPC.extraValue is retained money picked up by an actor, independent
            // of its base NPC.value. It forces the existing source money phase to publish.
            var rich = new NpcStateUpdate(target.Type, target.NetId, target.PositionX, target.PositionY,
                target.VelocityX, target.VelocityY, target.Target, target.Ai,
                target.Simulation with { ExtraMoneyValue = 10000 });
            Assert.True(fixture.Npcs.TryUpdate(target.Handle, in rich, out target));
            int notifications = 0;
            fixture.OnDrop = () =>
            {
                notifications++;
                Assert.Equal(fixture.StartRevision + 1, fixture.Clock.MoonEventProgressRevision);
                Assert.Equal(1f, fixture.Clock.MoonEventWaveKills);
            };
            if (environment)
                Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                    fixture.Pipeline.TryStrikeEnvironment(target.Handle, 100000));
            else
                Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,
                    fixture.Pipeline.TryStrikeServerPlayerMelee(fixture.Player, target.Handle, 100000, 0, false, 0f, 1));
            Assert.True(notifications > 0);
            Assert.False(fixture.Npcs.TryGet(target.Handle, out _));
            Assert.Equal(1f, fixture.Clock.MoonEventWaveKills);
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                fixture.Pipeline.TryStrikeEnvironment(target.Handle, 100000));
            Assert.Equal(fixture.StartRevision + 1, fixture.Clock.MoonEventProgressRevision);
        }
    }

    [Fact]
    public void Nested_ghost_fade_completes_without_canceling_the_retained_moon_publication()
    {
        var fixture = new Fixture(338, 0);
        var target = fixture.Spawn(338);
        // The admitted Captain producer owns Ghost's zero value/extra/midas fields;
        // a generic direct type662 spawn deliberately has no money provenance.
        var captain = fixture.Spawn(216);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
            fixture.Pipeline.TryStrikeEnvironment(captain.Handle, 100000));
        var active = new NpcSnapshot[fixture.Npcs.Capacity];
        int count = fixture.Npcs.CopyActive(active);
        var ghost = Assert.Single(active.AsSpan(0, count).ToArray(), npc => npc.Type == 662);
        Assert.Equal(0f, ghost.Simulation.MoneyValue);
        Assert.Equal(0, ghost.Simulation.ExtraMoneyValue);
        Assert.Equal(false, ghost.Simulation.Midas);
        var motion = new NpcStateUpdate(ghost.Type, ghost.NetId, ghost.PositionX, ghost.PositionY,
            ghost.VelocityX, ghost.VelocityY, ghost.Target, ghost.Ai,
            ghost.Simulation with { Alpha = 255 });
        bool callback = false;
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, fixture.Strike(in target, () =>
        {
            callback = true;
            Assert.Equal(1f, fixture.Clock.MoonEventWaveKills);
            Assert.True(fixture.Pipeline.TryCommitPirateGhostPhase(in ghost, in motion, true,
                [], static () => true, static () => true));
            Assert.False(fixture.Npcs.TryGet(ghost.Handle, out _));
            Assert.Equal(1f, fixture.Clock.MoonEventWaveKills);
        }));
        Assert.True(callback);
        Assert.False(fixture.Npcs.TryGet(target.Handle, out _));
        Assert.Equal(1f, fixture.Clock.MoonEventWaveKills);
        Assert.Equal(fixture.StartRevision + 1, fixture.Clock.MoonEventProgressRevision);
    }

    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup, IWorldItemStateCommitSink
    {
        internal readonly RuntimeNpcStore Npcs = new(200);
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly RuntimeWorldClock Clock;
        internal readonly long StartRevision;
        internal Action? OnDrop;
        internal PlayerHandle Player => player.Player;
        internal ProjectileSnapshot Projectile => new(new(0, new ProjectileGeneration(1)), new(1), new(14), 0,
            800, 440, 1, 0, default, 0, 32767, 0, 32767);
        private readonly PlayerStateSnapshot player = default(PlayerStateSnapshot) with
        {
            Player = new(new PlayerSlotId(0), new PlayerSessionGeneration(1)), Revision = new(1),
            Zones = new(0, 0, 0, 0, 0, 0),
            HasHealth = true, Life = 400, MaxLife = 400, DerivedLifeMax = 400,
            HasMana = true, Mana = 200, MaxMana = 200, NpcLifeCurrent = true
        };

        internal Fixture(int type, int mode)
        {
            Clock = new(0, false, 0, 0, 0);
            Assert.True(type == 338 ? Clock.TryStartSnowMoon() : Clock.TryStartPumpkinMoon());
            StartRevision = Clock.MoonEventProgressRevision;
            var items = new RuntimeWorldItemStore(this);
            // Ghost fade's existing death-birth owner requires the concrete spawn
            // context even when no child is produced. Early Moon counters alone did not.
            Npcs.SetVanillaSpawnContextSource(() => new(mode + 1f, 1, false));
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(Random));
            Pipeline = new(Npcs, items, this, new PlayerAuthority(null, null), () => 0,
                null, new(items), null, Clock, new(), mode > 0, mode == 2,
                lootRandom: Random, lootRemixWorld: false);
        }

        internal NpcSnapshot Spawn(int type)
        {
            var update = new NpcStateUpdate(type, (short)type, 800, 440, 0, 0, 0,
                default, NpcSimulationState.Initial);
            Assert.True(Npcs.TrySpawnVanilla(in update, out var result));
            return result;
        }

        internal RuntimeProjectileNpcDamageResult Strike(in NpcSnapshot target, Action accepted)
        {
            var before = Random.Clone();
            var after = before.Clone();
            after.Next();
            var shot = Projectile;
            return Pipeline.TryStrikePreparedProjectile(in shot, in target, 0, 100000, 0, false,
                before, after, static () => true, _ => accepted());
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = player;
            return slot.Value == 0;
        }

        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item, int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        {
            if (kind == WorldItemStateCommitKind.Drop) OnDrop?.Invoke();
        }
    }
}
