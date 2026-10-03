using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class IncomingCombatRandomOrdering1458Tests
{
    [Theory]
    [InlineData(3, 17, 0, 2000)]
    [InlineData(430, 17, 0, 17)]
    [InlineData(430, 34, 0, 34)]
    [InlineData(430, 34, 1, 51)]
    public void Fighter_damage_reset_is_source_scoped_to_armed_branch(int type, int baseDamage, int attackTimer, int expected)
    {
        var store = new RuntimeNpcStore(2);
        var state = new NpcStateUpdate((ushort)type, (short)type, 100, 100, 0, 1,
            VanillaNpcDefinitionCatalog.DefaultTarget, new NpcAiState(0, 0, attackTimer, 0),
            NpcSimulationState.Initial with { DamageOverride = 2000, BaseDamage = baseDamage });
        Assert.True(store.TrySpawn(0, in state, out var npc));
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 100);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1500, 100, 0, true, false, false, false)]);
        Assert.True(stepper.TryStepState(in npc, out var next));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out var definition));
        Assert.Equal(expected, next.Simulation.DamageOverride ?? definition.Damage);
    }

    // Official 1.4.5.8 Player.Update_NPCCollision and Projectile.Damage_EVP reject General immunity
    // before Main.DamageVar. BossNoCheese reaches DamageVar before Hurt rejects its cooldown.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void General_immunity_preserves_damage_rng_until_expiration(bool serverOwned, int attackKind)
    {
        using var fixture = new Fixture(serverOwned, bossChannel: false, attackKind);
        fixture.Run(attackKind);
        Assert.Equal(1, fixture.Random.Draws);
        for (fixture.Tick = 1; fixture.Tick < 40; fixture.Tick++)
            fixture.Run(attackKind);
        Assert.Equal(1, fixture.Random.Draws);
        fixture.Run(attackKind);
        Assert.Equal(2, fixture.Random.Draws);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void Boss_cooldown_keeps_source_damage_draw_order(bool serverOwned, int attackKind)
    {
        using var fixture = new Fixture(serverOwned, bossChannel: true, attackKind);
        fixture.Run(attackKind);
        fixture.Tick = 1;
        fixture.Run(attackKind);
        Assert.Equal(2, fixture.Random.Draws);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession? session;
        private readonly RuntimeNpcPlayerCombatPass contact;
        private readonly RuntimeProjectilePlayerCombatPass projectile;
        private readonly RuntimeProjectileExplosionEvent explosion;
        public readonly CountingRandom Random = new();
        public long Tick;

        public Fixture(bool serverOwned, bool bossChannel, int attackKind)
        {
            var slots = new PlayerSlotPool(2);
            ServerPlayerAuthority? server = null;
            if (serverOwned)
            {
                var identities = new ServerPlayerSlotRegistry(slots);
                server = new ServerPlayerAuthority(new ServerPlayerStateStore(identities, 2), identities);
                var id = new ServerPlayerId("rng-test");
                Assert.Equal(ServerPlayerCreateStatus.Created, server.Create(id, 100, 100).Status);
                Assert.True(server.SetVitals(id, new ServerPlayerVitalsState(500, 500, 0, 0)));
            }
            var players = new PlayerAuthority(null, null, serverPlayers: server);
            if (!serverOwned)
            {
                Assert.True(slots.TryAcquireConnection(out var lease));
                session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
                session.ObserveWorldRequest();
                session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), session.Handle);
                players.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
                    new PlayerSpawnCommitRequest(session.Slot, 10, 10, 0, 0, 0, 0, 0)));
                players.TryApply(new PlayerHealthRuntimeCommand(connection,
                    new PlayerHealthCommitRequest(session.Slot, 500, 500)));
                players.TryApply(new PlayerEquipmentRuntimeCommand(connection,
                    new PlayerEquipmentCommitRequest(session.Slot, 59, 0, 0, 0, 0)));
                players.TryApply(new PlayerMovementRuntimeCommand(connection,
                    new PlayerMovementCommitRequest(session.Slot, 0, 0, 0, 0, 0, 100, 100, false, 0, 0,
                        false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            }
            var npcs = new RuntimeNpcStore(2);
            NpcTypeId npcType = bossChannel ? VanillaNpcIds.MoonLordHand : VanillaNpcIds.Zombie;
            var state = new NpcStateUpdate(npcType.Value, (short)npcType.Value, 100, 100, 0, 0,
                VanillaNpcDefinitionCatalog.DefaultTarget, default,
                NpcSimulationState.Initial with { Friendly = false, DamageOverride = 10 });
            Assert.True(npcs.TrySpawn(0, in state, out var npc));
            contact = new RuntimeNpcPlayerCombatPass(npcs, players, Random, server);
            var projectiles = new RuntimeProjectileStore(2);
            var type = bossChannel ? VanillaProjectileIds.PhantasmalBolt : VanillaProjectileIds.CultistBossFireBall;
            var intent = new NpcAiProjectileIntent(type, 100, 100, 0, 0, 10, 0);
            Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(projectiles, npc.Handle, in intent, out var shot));
            projectile = new RuntimeProjectilePlayerCombatPass(projectiles, npcs, players, () => Tick, Random, serverPlayers: server);
            explosion = new RuntimeProjectileExplosionEvent(shot, default, npc.Handle, 100, 100, 40, 40);
            if (attackKind == 2)
                Assert.True(projectiles.TryDespawn(shot.Handle, out _));
        }

        public void Run(int kind)
        {
            if (kind == 0) contact.Tick(Tick);
            else if (kind == 1) projectile.Tick([]);
            else projectile.Tick([explosion]);
        }

        public void Dispose() => session?.Dispose();
    }

    private sealed class CountingRandom : Random
    {
        public int Draws;
        public override int Next(int minValue, int maxValue)
        {
            Assert.Equal((-15, 16), (minValue, maxValue));
            Draws++;
            return 0;
        }
    }
}
