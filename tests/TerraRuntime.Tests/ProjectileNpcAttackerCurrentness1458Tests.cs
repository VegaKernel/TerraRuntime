using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class ProjectileNpcAttackerCurrentness1458Tests
{
    [Fact]
    public void Ordinary_hit_rechecks_attacker_after_external_crit_and_variation_draws() => Check(false);

    [Fact]
    public void Explosion_hit_rechecks_attacker_after_external_crit_and_variation_draws() => Check(true);

    [Fact]
    public void Bot_unknown_active_accessory_cannot_fall_back_to_neutral_outgoing_combat()
    {
        foreach (bool explosion in new[] { false, true })
        foreach (bool unknownGear in new[] { false, true })
        {
            using var f = new Fixture(explosion, bot: true, unknownGear: unknownGear);
            f.Hit();
            Assert.Equal(unknownGear ? 0 : 2, f.Random.Calls);
            Assert.Equal(unknownGear ? 0 : 1, f.Pass.CommittedHits);
            Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var actor));
            if (!unknownGear) Assert.True(actor.Simulation.Life < f.Actor.Simulation.Life);
            else
            {
                Assert.Equal(f.Actor, actor);
                Assert.False(f.Combat.Interactions.HasAnyInteraction(f.Actor.Handle));
                Assert.True(f.Shots.TryGet(f.Shot.Handle, out var shot));
                Assert.Equal(f.Shot, shot);
                Assert.Equal(0, f.Items.ActiveCount);
            }
        }
    }

    private static void Check(bool explosion)
    {
        foreach (string dependency in new[] { "appearance", "equipment", "buffs" })
        foreach (int changedDraw in new[] { 1, 2 })
        {
            using var f = new Fixture(explosion);
            PlayerStateSnapshot? externalPlayer = null;
            f.Random.Callback = () =>
            {
                if (dependency == "appearance")
                {
                    var appearance = new PlayerAppearanceCommitRequest(f.Connection.Player.Slot,
                        0, 0, 0f, 0, "Changed attacker", 0, 0, 0, default, default, default, default,
                        default, default, default, 4, 0, 0);
                    Assert.True(f.Players.TryApply(new PlayerAppearanceRuntimeCommand(f.Connection, appearance)));
                }
                else if (dependency == "equipment")
                    Assert.True(f.Players.TryApply(new PlayerEquipmentRuntimeCommand(f.Connection,
                        new(f.Connection.Player.Slot, (short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3), 1, 0, 491, 0))));
                else
                    Assert.True(f.Players.TryApply(new PlayerBuffTypesRuntimeCommand(f.Connection,
                        new(f.Connection.Player.Slot, new[] { new BuffTypeId(93) }))));
                Assert.True(f.Players.TryCapture(f.Connection.Player, out var changed));
                externalPlayer = changed;
            };
            f.Random.ChangedDraw = changedDraw;
            f.Hit();
            Assert.Equal(2, f.Random.Calls);
            Assert.Equal(0, f.Pass.CommittedHits);
            Assert.Equal(0, f.Pass.ConsumedProjectiles);
            Assert.True(f.Npcs.TryGet(f.Actor.Handle, out var actor));
            Assert.Equal(f.Actor, actor);
            Assert.True(f.Shots.TryGet(f.Shot.Handle, out var shot));
            Assert.Equal(f.Shot, shot);
            Assert.False(f.Combat.Interactions.HasAnyInteraction(f.Actor.Handle));
            Assert.True(f.Players.TryCapture(f.Connection.Player, out var finalPlayer));
            Assert.Equal(externalPlayer, finalPlayer);
            Assert.Equal(0, f.Items.ActiveCount);
            // Custom Random is an external owner: its two completed calls are preserved, not rewound.
        }
        using var unchanged = new Fixture(explosion);
        unchanged.Hit();
        Assert.Equal(2, unchanged.Random.Calls);
        Assert.Equal(1, unchanged.Pass.CommittedHits);
        Assert.True(unchanged.Npcs.TryGet(unchanged.Actor.Handle, out var accepted));
        Assert.True(accepted.Simulation.Life < unchanged.Actor.Simulation.Life);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        private readonly bool explosion;
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeNpcStore Npcs = new(capacity: 8);
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly RuntimeProjectileStore Shots = new(capacity: 4);
        internal readonly CallbackRandom Random = new();
        internal readonly RuntimeNpcNetworkCombatPipeline Combat;
        internal readonly RuntimeProjectileNpcCombatPass Pass;
        internal readonly ConnectionHandle Connection;
        private readonly PlayerHandle owner;
        internal readonly NpcSnapshot Actor;
        internal readonly ProjectileSnapshot Shot;

        internal Fixture(bool explosion, bool bot = false, bool unknownGear = false)
        {
            this.explosion = explosion;
            var slots = new PlayerSlotPool(2);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(9902), session.Handle);
            ServerPlayerAuthority? bots = null;
            owner = Connection.Player;
            if (bot)
            {
                var identities = new ServerPlayerSlotRegistry(slots);
                bots = new(new ServerPlayerStateStore(identities, 2), identities);
                var id = new ServerPlayerId("test:npc-attacker-currentness");
                var created = bots.Create(id, 800, 440);
                Assert.True(created.IsCreated);
                owner = created.Player;
                Assert.NotEqual(Connection.Player.Slot, owner.Slot);
                if (unknownGear)
                    Assert.True(bots.SetItem(id, new((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3),
                        new(999), 1, default, 0)));
            }
            Players = new(null, null, serverPlayers: bots);
            Assert.True(Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session,
                new(session.Slot, 50, 27, 0, 0, 0, 0, 0))));
            var lookup = new RuntimePlayerSnapshotLookup(Players, bots);
            Combat = new(Npcs, Items, lookup, Players, () => 100, new RuntimeNpcReplicationRegistry(),
                new(Items), null, new(1000, false, default, 0, 0), new(), false, false,
                lootRandom: new VanillaUnifiedRandom1458(1458));
            var actor = new NpcStateUpdate(3, 3, 800, 440, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = 200, LifeMax = 200, MoneyValue = 0,
                    ExtraMoneyValue = 0, Immortal = false, ShimmerTransparency = 0 });
            Assert.True(Npcs.TrySpawn(0, actor, out Actor));
            var shot = new ProjectileStateUpdate(new(explosion ? 133 : 1), owner.Slot.Value,
                800, 440, 1, 0, default, 0, 10, 1, 10);
            Assert.True(Shots.TrySpawn(0, shot, out Shot));
            Assert.True(Shots.TryMarkCombatTrusted(Shot.Handle, owner));
            Pass = new(Shots, Npcs, Combat, Players, () => 100, random: Random, serverPlayers: bots);
        }

        internal void Hit()
        {
            if (!explosion) Pass.Tick();
            else Pass.TickExplosions(new[] { new RuntimeProjectileExplosionEvent(
                Shot, owner, default, 790, 430, 100, 100) });
        }

        public void Dispose() => session.Dispose();
    }

    private sealed class CallbackRandom : Random
    {
        internal int Calls;
        internal int ChangedDraw;
        internal Action? Callback;

        public override int Next(int minValue, int maxValue)
        {
            Calls++;
            if (Calls == ChangedDraw) Callback?.Invoke();
            return minValue == 1 ? 100 : 0;
        }
    }
}
