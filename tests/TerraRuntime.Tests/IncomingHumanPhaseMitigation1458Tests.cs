using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class IncomingHumanPhaseMitigation1458Tests
{
    [Fact]
    public void Actual_phase_then_equipment_report_preserves_source_incoming_mitigation_until_next_phase()
    {
        using var source = Read();
        Assert.Equal(13, source.RootElement.GetArrayLength());
        int compared = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            if (row.GetProperty("route").GetString() == "dedicated-hostile-control")
            {
                Assert.Equal(255, row.GetProperty("myPlayer").GetInt32());
                Assert.Equal(row.GetProperty("before").GetRawText(), row.GetProperty("after").GetRawText());
                Assert.Equal(row.GetProperty("beforeRandom").GetRawText(), row.GetProperty("afterRandom").GetRawText());
                Assert.Empty(row.GetProperty("frames").EnumerateArray());
                continue;
            }
            bool remove = row.GetProperty("profile").GetString() == "remove-shield";
            using var f = new Fixture();
            f.Equipment(remove ? (short)156 : (short)0);
            f.State.Tick();
            Assert.Equal(row.GetProperty("derived").GetProperty("defense").GetInt32(), f.Member.ItemPhase!.Value.DerivedCombat!.Value.Defense);
            f.Equipment(remove ? (short)0 : (short)156);
            if (row.GetProperty("advance").GetBoolean()) f.State.Tick();
            string route = row.GetProperty("route").GetString()!;
            var random = new SourceRandom(1458);
            f.Run(route, random);
            Assert.Equal(row.GetProperty("after").GetProperty("life").GetInt32(), f.Member.Life);
            var velocity = row.GetProperty("after").GetProperty("velocity");
            Assert.Equal(velocity.GetProperty("X").GetSingle(), f.Member.VelocityX);
            Assert.Equal(velocity.GetProperty("Y").GetSingle(), f.Member.VelocityY);
            // Independent callers include Hurt's cosmetic draws; this pass owns damage variation,
            // mitigation and impulse, not the source visual stream or complete Main scheduling.
            compared++;
        }
        Assert.Equal(12, compared);
    }

    [Fact]
    public void Unknown_or_saturated_phase_refuses_before_offers_while_explicit_component_policy_and_godmode_remain_separate()
    {
        foreach (string route in Routes)
        {
            using var f = new Fixture();
            f.Member.ItemPhase = null;
            var random = new CallbackRandom(null);
            f.Run(route, random);
            Assert.Equal(0, random.Draws);
            Assert.Equal(400, f.Member.Life);
            Assert.Null(f.Member.ItemPhase);
        }
        foreach (bool inputRevision in new[] { false, true })
        {
            using var f = new Fixture();
            if (inputRevision)
                typeof(RuntimePlayerMember).GetField("<ProjectileUseInputRevision>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(f.Member, ulong.MaxValue);
            else f.Member.Revision = ulong.MaxValue;
            var random = new CallbackRandom(null);
            f.Run("npc-contact", random);
            Assert.Equal(0, random.Draws);
            Assert.Equal(400, f.Member.Life);
        }
        using (var f = new Fixture())
        {
            f.Member.ItemPhase = null;
            f.Member.GodMode = true;
            f.Run("npc-contact", new CallbackRandom(null));
            Assert.Equal(400, f.Member.Life);
            Assert.Null(f.Member.ItemPhase);
        }
        // Standalone component APIs deliberately keep their existing live-equipment contract.
        using (var f = new Fixture(IncomingHumanCombatPolicy1458.EquipmentComponent))
        {
            f.Member.ItemPhase = null;
            f.Run("npc-contact", new CallbackRandom(null));
            Assert.True(f.Member.Life < 400);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerAuthority(null, null,
            incomingHumanCombatPolicy: (IncomingHumanCombatPolicy1458)255));
    }

    [Fact]
    public void Damage_offer_callback_cannot_replace_phase_target_or_source_before_the_commit()
    {
        foreach (string route in Routes)
        foreach (string mutation in new[] { "phase", "equipment", "source" })
        {
            using var f = new Fixture();
            f.State.Tick();
            var random = new CallbackRandom(() =>
            {
                if (mutation == "phase") f.Member.ItemPhase = f.Member.ItemPhase!.Value with { ManaHeat = 0.25f };
                if (mutation == "equipment") f.Equipment(156);
                if (mutation == "source") f.RetireSource();
            });
            f.Run(route, random);
            Assert.True(random.Draws > 0);
            Assert.Equal(400, f.Member.Life);
            Assert.Equal(0, f.Member.VelocityX);
            Assert.Equal(0, f.Member.VelocityY);
        }
    }

    [Fact]
    public void Client_melee_captures_human_target_phase_before_both_draws_and_preserves_retry_cadence()
    {
        using var f = new Fixture();
        f.State.Tick();
        f.MoveOwnerNear();
        Assert.True(f.Players.TryApply(new PlayerEquipmentRuntimeCommand(f.Owner,
            new(f.Owner.Player.Slot, 0, 1, 0, 3508, 0))));
        var random = new CallbackRandom(() => f.Member.ItemPhase = f.Member.ItemPhase!.Value with { ManaHeat = 0.25f });
        var integrity = new RuntimePvpCombatIntegrity(f.Players, random);
        var reason = new TerrariaPlayerDeathReasonState(f.Owner.Player.Slot.Value, -1, -1, -1, 0, 0, 0, null);
        var wire = new TerrariaPlayerHurtState(f.Connection.Player.Slot.Value, reason, 1, 2, 2, -1);
        Assert.Equal(PvpCombatResolveResult.Rejected, integrity.ResolveClientItemHit(1, f.Owner, in wire, out _));
        Assert.Equal(2, random.Draws);
        Assert.Equal(PvpCombatResolveResult.Accepted, integrity.ResolveClientItemHit(1, f.Owner, in wire, out var accepted));
        Assert.Equal(f.Member.ItemPhase, accepted.TargetCapture.ItemPhase);
        f.Member.ItemPhase = null;
        int before = random.Draws;
        Assert.Equal(PvpCombatResolveResult.Rejected, integrity.ResolveClientItemHit(2, f.Owner, in wire, out _));
        Assert.Equal(before, random.Draws);
    }

    [Fact]
    public void Reentrant_join_is_deferred_until_the_next_pass_without_invalidating_the_human_census()
    {
        foreach (string route in new[] { "npc-contact", "hostile-projectile", "hostile-explosion" })
        {
            using var f = new Fixture();
            f.State.Tick();
            RuntimePlayerMember? added = null;
            var random = new CallbackRandom(() => added = f.JoinDuringOffer());
            f.Run(route, random);
            Assert.True(random.Draws > 0);
            Assert.NotNull(added);
            Assert.Equal(400, f.Member.Life);
            Assert.Equal(400, added.Life);
        }
    }

    [Fact]
    public void Raw_godmode_flip_without_a_snapshot_revision_cannot_adopt_damage_or_projectile_immunity()
    {
        foreach (string route in Routes)
        foreach (bool previouslyGodMode in new[] { false, true })
        {
            using var f = new Fixture();
            f.State.Tick();
            f.Member.GodMode = previouslyGodMode;
            if (previouslyGodMode) f.Member.ItemPhase = null;
            var before = f.Member.CaptureSnapshot();
            var item = f.Member.ItemPhase;
            ProjectileSnapshot? expectedShot = null;
            ProjectileLifecycleState expectedLifecycle = default;
            var random = new CallbackRandom(() =>
            {
                if (f.Shots.TryGetActive(0, out var shot))
                {
                    expectedShot = shot;
                    Assert.True(f.Shots.TryGetLifecycle(shot.Handle, out expectedLifecycle));
                }
                f.Member.GodMode = !previouslyGodMode;
            });
            f.Run(route, random);
            Assert.True(random.Draws > 0);
            Assert.Equal(before, f.Member.CaptureSnapshot()); // The omitted snapshot field cannot protect this case.
            Assert.Equal(item, f.Member.ItemPhase);
            Assert.False(f.Players.IsGeneralPveImmune(f.Connection.Player, 1));
            Assert.Equal(0, f.LastContact?.GodModeAvoidances ?? 0);
            Assert.Equal(0, f.LastProjectile?.CommittedHits ?? 0);
            Assert.Equal(0, f.LastProjectile?.HostileCommittedHits ?? 0);
            Assert.Equal(0, f.LastProjectile?.HostileGodModeAvoidances ?? 0);
            Assert.Equal(0, f.LastProjectile?.ConsumedProjectiles ?? 0);
            if (expectedShot is { } expected)
            {
                Assert.True(f.Shots.TryGet(expected.Handle, out var retained));
                Assert.Equal(expected, retained);
                Assert.True(f.Shots.TryGetLifecycle(expected.Handle, out var lifecycle));
                Assert.Equal(expectedLifecycle, lifecycle);
            }
        }
    }

    private static readonly string[] Routes = ["npc-contact", "hostile-projectile", "pvp-projectile", "hostile-explosion", "pvp-explosion"];
    private static JsonDocument Read()
    {
        using var stream = typeof(IncomingHumanPhaseMitigation1458Tests).Assembly.GetManifestResourceStream("IncomingHumanPhaseCallers1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class CallbackRandom(Action? callback) : Random
    {
        internal int Draws;
        public override int Next(int minValue, int maxValue)
        {
            if (Draws++ == 0) callback?.Invoke();
            return minValue < 0 ? 0 : maxValue - 1;
        }
    }

    private sealed class SourceRandom(int seed) : Random
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public override int Next(int minValue, int maxValue) => source.Next(minValue, maxValue);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly PlayerSlotPool slots = new(3);
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly ConnectionHandle Connection;
        internal readonly ConnectionHandle Owner;
        internal readonly RuntimeNpcStore Npcs = new(2);
        internal readonly RuntimeProjectileStore Shots = new(2);
        private NpcHandle sourceNpc;
        private ProjectileHandle sourceShot;
        internal RuntimeNpcPlayerCombatPass? LastContact;
        internal RuntimeProjectilePlayerCombatPass? LastProjectile;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(Connection, out var m)); return m; } }

        internal Fixture(IncomingHumanCombatPolicy1458 policy = IncomingHumanCombatPolicy1458.PhaseOwned)
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(worldTiles: tiles, playerUpdateRandomSeed: new(0), incomingHumanCombatPolicy: policy);
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!).Players;
            Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false));
            Players.SetRemotePlayerEnvironment(new(false, false), new RuntimeProjectileStore());
            Connection = Join(slots, 88310);
            Owner = Join(slots, 88311);
        }

        private ConnectionHandle Join(PlayerSlotPool slots, long id)
        {
            Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var c = new ConnectionHandle(GameCommandSourceId.FromConnection(id), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, 400, 400)));
            State.Apply(new PlayerManaRuntimeCommand(c, new(c.Player.Slot, 200, 200)));
            State.Apply(new PlayerSpawnRuntimeCommand(c, session, new(c.Player.Slot, 100, 103, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 1, 0, 219, 0)));
            State.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 59, 0, 0, 0, 0)));
            State.Apply(new PlayerPvpToggleRuntimeCommand(c, true));
            State.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 64, 16, 0, 0, 0,
                c.Player.Slot.Value == 0 ? 1600 : 1700, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            return c;
        }

        internal void Equipment(short type) => State.Apply(new PlayerEquipmentRuntimeCommand(Connection,
            new(Connection.Player.Slot, 62, (short)(type == 0 ? 0 : 1), 0, type, 0)));

        internal RuntimePlayerMember JoinDuringOffer()
        {
            var c = Join(slots, 88312);
            State.Apply(new PlayerMovementRuntimeCommand(c,
                new(c.Player.Slot, 64, 16, 0, 0, 0, 1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Assert.True(Players.TryGet(c, out var member));
            return member;
        }

        internal void RetireSource()
        {
            if (sourceShot.IsAssigned)
            {
                if (!Shots.TryDespawn(sourceShot, out _))
                {
                    var replacement = new ProjectileStateUpdate(new ProjectileTypeId(14), Owner.Player.Slot.Value,
                        3000, 1606, 1, 0, default, 0, 100, 0, 100);
                    Assert.True(Shots.TrySpawn(0, in replacement, out _));
                }
            }
            else if (sourceNpc.IsAssigned) Npcs.TryDespawn(sourceNpc);
        }

        internal void MoveOwnerNear() => State.Apply(new PlayerMovementRuntimeCommand(Owner,
            new(Owner.Player.Slot, 64, 16, 0, 0, 0, 1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));

        internal void Run(string route, Random random)
        {
            var npcState = new NpcStateUpdate(3, 3, 1600, 1606, 0, 0, 255, default,
                NpcSimulationState.Initial with { Friendly = false, DamageOverride = 100 });
            Assert.True(Npcs.TrySpawn(0, in npcState, out var npc)); sourceNpc = npc.Handle;
            if (route == "npc-contact")
            {
                LastContact = new RuntimeNpcPlayerCombatPass(Npcs, Players, random);
                LastContact.Tick(1);
                return;
            }
            bool pvp = route.StartsWith("pvp", StringComparison.Ordinal);
            var shotState = new ProjectileStateUpdate(new ProjectileTypeId(pvp ? 14 : 100),
                pvp ? Owner.Player.Slot.Value : (byte)255, 1600, 1606, 1, 0, default, 0, 100, 0, 100);
            if (pvp)
            {
                Assert.True(Shots.TrySpawn(0, in shotState, out var shot)); sourceShot = shot.Handle;
                Assert.True(Shots.TryMarkCombatTrusted(shot.Handle, Owner.Player));
            }
            else
            {
                var intent = new NpcAiProjectileIntent(new ProjectileTypeId(100), 1600, 1606, 1, 0, 100, 0);
                Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(Shots, npc.Handle, in intent, out var shot)); sourceShot = shot.Handle;
            }
            Assert.True(Shots.TryGet(sourceShot, out var birth));
            var pass = new RuntimeProjectilePlayerCombatPass(Shots, Npcs, Players, () => 1, random);
            LastProjectile = pass;
            if (route.EndsWith("explosion", StringComparison.Ordinal))
            {
                // OnKill explosions retain an accepted birth after physical removal; reentry must not
                // replace that slot and then reuse the older event against the current target.
                Assert.True(Shots.TryDespawn(sourceShot, out _));
                var bombState = shotState with { Type = new ProjectileTypeId(pvp ? 134 : 467) };
                birth = birth with { Type = bombState.Type };
                pass.Tick([new RuntimeProjectileExplosionEvent(birth, pvp ? Owner.Player : default,
                    pvp ? default : npc.Handle, 1600, 1606, 40, 42)]);
            }
            else pass.Tick([]);
        }

        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
}
