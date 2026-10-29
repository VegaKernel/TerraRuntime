using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed partial class CaptainGhostLifecycleSource1458Tests
{
    // Connection identity and source player slot are independent. Slot zero is a
    // real fixture player; zero is not a valid connection source identity.
    private static ConnectionHandle RangedConnection() => new(GameCommandSourceId.FromConnection(9001),
        new(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
    private static TerrariaConnectionOutboundQueue JoinRangedPlayer(Fixture fixture)
    {
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(2048, 1048576, 16384));
        var connection = RangedConnection();
        Assert.True(fixture.Registry.TryRegister(connection.Source, queue));
        fixture.Registry.PlayerSpawned(connection, new(connection.Player.Slot, 1, 1, 0, 0, 0, 0, 0));
        return queue;
    }

    [Theory]
    [InlineData("client")] [InlineData("projectile")] [InlineData("town")]
    public void Unowned_ritual_controller_refuses_before_credit_hp_random_and_npc_publication(string route)
    {
        var f = new Fixture(false, false, replicate: true, attachInvasion: false, targetType: 379,
            clientPlayer: route == "client");
        var update = new NpcStateUpdate(379, 379, f.Captain.PositionX, f.Captain.PositionY, 0, 0, 255,
            new(0, 0, 0, -2f), f.Captain.Simulation);
        Assert.True(f.Npcs.TryUpdate(f.Captain.Handle, in update, out var before));
        var queue = JoinRangedPlayer(f); QueueCapture.Drain(queue);
        f.Commits.Clear(); var random = f.Random.Clone();
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var ledger = (RuntimeNpcPlayerInteractionLedger)typeof(RuntimeNpcNetworkCombatPipeline)
            .GetField("interactions", fields)!.GetValue(f.Pipeline)!;
        Assert.False(ledger.HasInteraction(before.Handle, new(0)));
        if (route == "client")
            Assert.Equal(RuntimeNpcNetworkDamageResult.Rejected, f.Pipeline.TryApply(RangedConnection(),
                new(before.Handle.Slot, RuntimeNpcPacketProjection.ToProtocolGeneration(before.Handle.Generation), 10, 0, 1, 0)));
        else if (route == "projectile")
        {
            var shot = new ProjectileSnapshot(new(0, new(1)), new(1), new(14), 0, 800, 440, 1, 0, default, 0, 10, 0, 10);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Pipeline.TryStrikePreparedProjectile(
                in shot, in before, 1, 10, 0, false, random, random.Clone(), () => true, _ => Assert.Fail("Refused ritual adopted projectile randomness.")));
        }
        else Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(before.Handle, 10));
        Assert.True(f.Npcs.TryGet(before.Handle, out var after)); Assert.Equal(before, after);
        Assert.True(f.Random.HasSameState(random)); Assert.Empty(f.Commits); Assert.Empty(f.Drops);
        Assert.False(ledger.HasInteraction(before.Handle, new(0)));
        var frames = QueueCapture.Drain(queue).Select(Convert.FromHexString).ToArray();
        // The source protocol's162 acknowledgement precedes generation/capability
        // admission; it is not a gameplay hit or an NPC-state publication.
        Assert.All(frames, frame => Assert.Equal((byte)162, frame[2]));
    }

    [Theory]
    [InlineData("client")] [InlineData("projectile")] [InlineData("town")]
    public void Nonlethal_maggot_choice_is_adopted_once_before_npc_observers(string route)
    {
        var f = new Fixture(false, false, attachInvasion: false, targetType: 632, tiles: new(new(192, 200)), clientPlayer: route == "client");
        var before = f.Random.Clone(); var expected = before.Clone(); _ = expected.Next(5);
        int observed = 0;
        f.OnNpcCommit = (kind, npc) =>
        {
            if (npc.Handle != f.Captain.Handle || kind == NpcStateCommitKind.Spawn) return;
            observed++; Assert.True(f.Random.HasSameState(expected));
        };
        if (route == "client")
            Assert.Equal(RuntimeNpcNetworkDamageResult.Committed, f.Pipeline.TryApply(RangedConnection(),
                new(f.Captain.Handle.Slot, RuntimeNpcPacketProjection.ToProtocolGeneration(f.Captain.Handle.Generation), 10, 0, 1, 0)));
        else if (route == "projectile")
        {
            var shot = new ProjectileSnapshot(new(0, new(1)), new(1), new(14), 0, 800, 440, 1, 0, default, 0, 10, 0, 10);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Committed, f.Pipeline.TryStrikePreparedProjectile(
                in shot, in f.Captain, 1, 10, 0, false, before, before.Clone(), () => true, _ => Assert.True(f.Random.HasSameState(expected))));
        }
        else Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 10));
        Assert.True(observed > 0); Assert.True(f.Random.HasSameState(expected));
    }

    [Theory]
    [InlineData(379, 0)] [InlineData(380, 0)] [InlineData(383, 1)] [InlineData(384, 0)]
    [InlineData(386, 1)] [InlineData(387, 0)] [InlineData(389, 1)] [InlineData(390, 1)]
    [InlineData(391, 1)] [InlineData(606, 0)] [InlineData(631, 0)]
    public void Qualified_ordinary_ranged_deaths_complete_projectile_loot_and_exact_invasion_credit(int type, int credit)
    {
        var f = new Fixture(false, false, replicate: true, invasionType: 4, targetType: type);
        var queue = JoinRangedPlayer(f); QueueCapture.Drain(queue);
        var before = f.Random.Clone();
        var shot = new ProjectileSnapshot(new(0, new(1)), new(1), new(14), 0, 800, 440, 1, 0, default, 0, 32767, 0, 32767);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Pipeline.TryStrikePreparedProjectile(
            in shot, in f.Captain, 1, 32767, 0, false, before, before.Clone(), () => true, _ => { }));
        Assert.False(f.Npcs.TryGet(f.Captain.Handle, out _));
        Assert.Equal(6 - credit, f.CaptureInvasion().Size);
        var retained = f.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 32767));
        Assert.True(f.Random.HasSameState(retained));
    }

    [Theory]
    [InlineData("client", 1458)] [InlineData("client", 42)] [InlineData("client", 0)]
    [InlineData("projectile", 1458)] [InlineData("projectile", 42)] [InlineData("projectile", 0)]
    [InlineData("town", 1458)] [InlineData("town", 42)] [InlineData("town", 0)]
    public void Maggot_death_adopts_all_siblings_and_known_status_before_first_observer_on_every_strike_lane(string route, int seed)
    {
        var tiles = new WorldTileStore(new(192, 200));
        var f = new Fixture(false, false, replicate: true, attachInvasion: false, targetType: 632, tiles: tiles, seed: seed, clientPlayer: route == "client");
        var queue = JoinRangedPlayer(f); QueueCapture.Drain(queue);
        var status = new RuntimeNpcBuffStatus1458(f.Npcs);
        int expected = RangedCohortLoot1458Tests.Rows("parentHitEffects", "cases")
            .Single(r => r.GetProperty("type").GetInt32() == 632 && r.GetProperty("seed").GetInt32() == seed && r.GetProperty("lethal").GetBoolean())
            .GetProperty("after").GetProperty("activeNpcPeers").GetArrayLength() - 1;
        int observers = 0;
        VanillaUnifiedRandom1458? accepted = null;
        f.OnRetainedBirth = child =>
        {
            if (child.Type != 606) return;
            observers++;
            Assert.False(f.Npcs.TryGet(f.Captain.Handle, out _));
            Span<NpcSnapshot> siblings = new NpcSnapshot[200];
            int count = f.Npcs.CopyActive(siblings);
            Assert.Equal(expected, siblings[..count].ToArray().Count(n => n.Type == 606));
            Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[20];
            foreach (var sibling in siblings[..count])
            {
                if (sibling.Type != 606) continue;
                Assert.True(status.TryCopyWireBuffs(sibling.Handle, buffs, out int buffCount));
                Assert.Equal(0, buffCount);
            }
            if (accepted is null) accepted = f.Random.Clone();
            else Assert.True(f.Random.HasSameState(accepted));
        };
        if (route == "client")
            Assert.Equal(RuntimeNpcNetworkDamageResult.Killed, f.Pipeline.TryApply(RangedConnection(),
                new(f.Captain.Handle.Slot, RuntimeNpcPacketProjection.ToProtocolGeneration(f.Captain.Handle.Generation), 32767, 0, 1, 0)));
        else if (route == "projectile")
        {
            var before = f.Random.Clone();
            var shot = new ProjectileSnapshot(new(0, new(1)), new(1), new(14), 0, 800, 440, 1, 0, default, 0, 32767, 0, 32767);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Pipeline.TryStrikePreparedProjectile(
                in shot, in f.Captain, 1, 32767, 0, false, before, before.Clone(), () => true, _ => { }));
        }
        else
        {
            var attacker = new NpcStateUpdate(3, 3, 800, 440, 0, 0, 255, default, NpcSimulationState.Initial);
            Assert.True(f.Npcs.TrySpawnVanilla(in attacker, out var npc, startSlot: 190));
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrike(npc.Handle, f.Captain.Handle, 32767, 0, 1));
        }
        Assert.Equal(expected, observers);
        Assert.DoesNotContain(f.Commits, c => c.Kind == NpcStateCommitKind.Spawn && c.Npc.Type == 606);
        Assert.DoesNotContain(QueueCapture.Drain(queue), frame =>
        {
            var bytes = Convert.FromHexString(frame);
            return bytes.Length >= 4 && bytes[2] == 23 && bytes[3] is >= 1 and <= 3;
        });
        var born = new NpcSnapshot[200]; int bornCount = f.Npcs.CopyActive(born);
        foreach (var child in born.Take(bornCount).Where(n => n.Type == 606))
        {
            Assert.True(f.Npcs.HasPendingBirth(child.Handle));
            Assert.True(f.Npcs.TryPublishPendingBirth(child.Handle));
            Assert.True(f.Npcs.TryPublishPendingBirth(child.Handle));
        }
        Assert.Equal(expected, f.Commits.Count(c => c.Kind == NpcStateCommitKind.Spawn && c.Npc.Type == 606));
        Assert.NotNull(accepted); Assert.True(f.Random.HasSameState(accepted!));
        var terminal = f.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 32767));
        Assert.True(f.Random.HasSameState(terminal));
    }

    [Theory]
    [InlineData("client")] [InlineData("projectile")] [InlineData("town")]
    public void Maggot_terminal_wire_follows_source_caller_order_without_announcing_children(string route)
    {
        var f = new Fixture(false, false, replicate: true, attachInvasion: false, targetType: 632,
            tiles: new(new(192, 200)), clientPlayer: route == "client");
        var sender = JoinRangedPlayer(f); var observer = f.Join(1);
        QueueCapture.Drain(sender); QueueCapture.Drain(observer);
        var priorFrames = new List<byte[]>(); bool sawDrop = false;
        f.OnDrop = _ =>
        {
            if (sawDrop) return;
            sawDrop = true;
            priorFrames.AddRange(QueueCapture.Drain(observer).Select(Convert.FromHexString));
            Assert.Equal(route != "client", priorFrames.Any(b => b[2] == 28));
        };
        if (route == "client")
            Assert.Equal(RuntimeNpcNetworkDamageResult.Killed, f.Pipeline.TryApply(RangedConnection(),
                new(f.Captain.Handle.Slot, RuntimeNpcPacketProjection.ToProtocolGeneration(f.Captain.Handle.Generation), 32767, 0, 1, 0)));
        else if (route == "projectile")
        {
            var before = f.Random.Clone();
            var shot = new ProjectileSnapshot(new(0, new(1)), new(1), new(14), 0, 800, 440, 1, 0, default, 0, 32767, 0, 32767);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Pipeline.TryStrikePreparedProjectile(
                in shot, in f.Captain, 1, 32767, 0, false, before, before.Clone(), () => true, _ => { }));
        }
        else Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 32767));
        var messages = priorFrames.Concat(QueueCapture.Drain(observer).Select(Convert.FromHexString)).ToArray();
        int hit = Array.FindIndex(messages, b => b[2] == 28);
        Assert.True(sawDrop); Assert.True(hit >= 0);
        Assert.DoesNotContain(messages, b => b[2] == 23 && b[3] is >= 1 and <= 3);
        var terminal = messages.Where(b => b[2] == 23 && b[3] == f.Captain.Handle.Slot).ToArray();
        if (route == "client")
        {
            Assert.Single(terminal); Assert.True(Array.IndexOf(messages, terminal[0]) > hit);
            Assert.DoesNotContain(QueueCapture.Drain(sender).Select(Convert.FromHexString), b => b[2] == 28);
        }
        else Assert.Empty(terminal);
        Assert.Single(f.Commits, c => c.Kind == NpcStateCommitKind.Despawn && c.Npc.Handle == f.Captain.Handle);
    }

    [Fact]
    public void Maggot_unknown_birth_terrain_and_changed_queried_section_refuse_before_hp_random_or_births()
    {
        foreach (bool missingWorld in new[] { true, false })
        {
            var tiles = missingWorld ? null : new WorldTileStore(new(192, 200));
            var f = new Fixture(false, false, attachInvasion: false, targetType: 632, tiles: tiles);
            bool changed = false;
            if (!missingWorld)
                f.OnPlayerRead = () =>
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    object? terrain = typeof(RuntimeNpcNetworkCombatPipeline).GetField("plannedMaggotTerrain", flags)!.GetValue(f.Pipeline);
                    if (changed || terrain is null) return;
                    var sections = (System.Collections.IDictionary)terrain.GetType().GetField("sections", flags)!.GetValue(terrain)!;
                    if (sections.Count == 0) return;
                    changed = true;
                    tiles!.Set(50, 28, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                };
            var before = f.Random.Clone();
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 32767));
            Assert.True(f.Npcs.TryGet(f.Captain.Handle, out var after)); Assert.Equal(f.Captain, after);
            Assert.True(f.Random.HasSameState(before)); Assert.Equal(1, f.Npcs.ActiveCount);
            if (!missingWorld) Assert.True(changed);
        }
    }

    [Fact]
    public void Good_world_rock_golem_refuses_unowned_projectile99_death_before_any_adoption()
    {
        var f = new Fixture(true, false, attachInvasion: false, targetType: 631);
        var before = f.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 32767));
        Assert.True(f.Npcs.TryGet(f.Captain.Handle, out var after)); Assert.Equal(f.Captain, after);
        Assert.True(f.Random.HasSameState(before)); Assert.Empty(f.Drops);
    }

    [Fact]
    public void Saturated_source_pool_still_completes_maggot_death_without_inventing_a_child_slot()
    {
        var f = new Fixture(false, true, attachInvasion: false, targetType: 632, tiles: new(new(192, 200)));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 32767));
        Assert.Equal(199, f.Npcs.ActiveCount);
        Assert.DoesNotContain(f.Commits, c => c.Npc.Type == 606);
    }

    [Fact]
    public void Wet_births_query_each_actual_child_body_while_retaining_only_the_pending_generation()
    {
        var tiles = new WorldTileStore(new(192, 200));
        for (int x = 48; x <= 53; x++) for (int y = 26; y <= 31; y++)
            tiles.Set(x, y, new WorldTile { LiquidAmount = 255 });
        var f = new Fixture(false, false, attachInvasion: false, targetType: 632, tiles: tiles, seed: 42);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 32767));
        var peers = new NpcSnapshot[200]; int count = f.Npcs.CopyActive(peers);
        Assert.Equal(3, count);
        foreach (var child in peers.Take(count))
        {
            Assert.Equal(606, child.Type); Assert.True(child.Simulation.Wet);
            Assert.True(f.Npcs.HasPendingBirth(child.Handle));
        }
    }

    [Fact]
    public void First_child_reentry_cannot_publish_a_replaced_sibling_or_lose_other_accepted_births()
    {
        var f = new Fixture(false, false, replicate: true, attachInvasion: false, targetType: 632,
            tiles: new(new(192, 200)), seed: 42);
        var queue = JoinRangedPlayer(f); QueueCapture.Drain(queue);
        NpcHandle replaced = default;
        int announcedByReentrantRemoval = 0;
        int seen = 0;
        f.OnRetainedBirth = child =>
        {
            if (child.Type != 606 || ++seen != 1) return;
            Assert.True(f.Npcs.TryGetActive(2, out var sibling)); replaced = sibling.Handle;
            Assert.True(f.Npcs.TryDespawn(sibling.Handle));
            // The trusted explicit removal announces its own pending generation.
            // The outer retained journal must not announce that stale child again.
            announcedByReentrantRemoval = f.Commits.Count(c => c.Kind == NpcStateCommitKind.Spawn && c.Npc.Handle == replaced);
            Assert.Equal(1, announcedByReentrantRemoval);
            var replacement = new NpcStateUpdate(3, 3, 800, 440, 0, 0, 255, default, NpcSimulationState.Initial);
            Assert.True(f.Npcs.TrySpawn(2, in replacement, out _));
        };
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Captain.Handle, 32767));
        Assert.Equal(2, seen);
        Assert.Equal(announcedByReentrantRemoval,
            f.Commits.Count(c => c.Kind == NpcStateCommitKind.Spawn && c.Npc.Handle == replaced));
        Assert.True(f.Npcs.TryGetActive(2, out var actual)); Assert.Equal(3, actual.Type);
        Assert.True(f.Npcs.TryGetActive(3, out var last)); Assert.Equal(606, last.Type);
    }
}
