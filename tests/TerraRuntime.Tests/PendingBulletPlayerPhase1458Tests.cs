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
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PendingBulletPlayerPhase1458Tests
{
    [Fact]
    public void Source_shotgun_children_complete_after_an_owned_player_tick_with_one_debit()
    {
        using var source = Source();
        JsonElement row = Row(source);
        foreach (int ticks in new[] { 1, 2 })
        {
            using var f = new Fixture(row);
            var before = f.Random.Clone();
            PlayerStateSnapshot captured = f.Snapshot();
            f.Report(0);
            for (int i = 0; i < ticks; i++) f.State.Tick();
            Assert.True(f.Snapshot().Revision.Value > captured.Revision.Value);
            Assert.Equal(0, f.Store.ActiveCount);
            Assert.Equal(20, f.Ammo());
            Assert.True(f.Random.HasSameState(before));
            f.Complete();
            Assert.Equal(5, f.Store.ActiveCount);
            Assert.Equal(row.GetProperty("afterAmmo").GetInt32(), f.Ammo());
            Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Clone().Next());
            Assert.Equal(5, f.Outbound.QueuedFrames);
            foreach (var packet in f.Packets)
            {
                Assert.True(f.Registry.WireIdentities.TryResolve(packet.Key, out var handle));
                Assert.True(f.Store.TryGet(handle, out var projectile));
                Assert.True(f.Store.IsCombatTrusted(handle));
                Assert.Equal(packet.PositionX, projectile.PositionX);
                Assert.Equal(packet.PositionY, projectile.PositionY);
            }
        }
    }

    [Fact]
    public void Input_ABA_profile_inventory_death_reuse_RNG_and_unowned_pose_still_reject_pending_use()
    {
        using var source = Source();
        JsonElement row = Row(source);
        foreach (int mutation in Enumerable.Range(0, 9))
        {
            using var f = new Fixture(row);
            f.Report(0);
            f.State.Tick();
            switch (mutation)
            {
                case 0: f.Move(1601); f.Move(1600); break; // A real packet13 ABA is still a new input epoch.
                case 1:
                    f.State.Apply(new PlayerAppearanceRuntimeCommand(f.Connection,
                        new(f.Connection.Player.Slot, 0, 0, 0, 0, "changed", 0, 0, 0,
                            default, default, default, default, default, default, default, 0, 0, 0)));
                    break;
                case 2: f.SetItem(54, 97, 19); break;
                case 3: f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection,
                    new(f.Connection.Player.Slot, new BuffTypeId[] { new(93) }))); break;
                case 4: f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection,
                    new(f.Connection.Player.Slot, 0, 400))); break;
                case 5: f.ReplacePlayerGeneration(); break;
                case 6: f.Random.Next(); break;
                case 7: f.MutatePoseWithoutCommand(); break;
                case 8: f.SetItem(0, 98, 1); f.SetItem(0, 534, 1); break;
            }
            var before = f.Random.Clone();
            int ammo = mutation == 2 ? 19 : 20;
            f.Complete();
            Assert.Equal(0, f.Store.ActiveCount);
            if (mutation != 5) Assert.Equal(ammo, f.Ammo());
            Assert.True(f.Random.HasSameState(before));
            Assert.Equal(0, f.Outbound.QueuedFrames);
            Assert.True(f.State.RejectedClientProjectileUpdates > 0);
        }
    }

    [Fact]
    public void Unowned_retained_item_clock_changes_reject_immediate_and_refreshed_pending_use()
    {
        using var source = Source();
        foreach (bool tick in new[] { false, true })
        {
            using var f = new Fixture(Row(source));
            f.Report(0);
            if (tick) f.State.Tick();
            var pose = f.Snapshot();
            var member = f.Member();
            var phase = Assert.IsType<RuntimePlayerItemPhase1458>(member.ItemPhase);
            member.ItemPhase = phase with { ToolTime = phase.ToolTime + 1 };
            Assert.Equal(pose, f.Snapshot());
            var before = f.Random.Clone();
            f.Complete();
            Assert.Equal(0, f.Store.ActiveCount);
            Assert.Equal(20, f.Ammo());
            Assert.True(f.Random.HasSameState(before));
            Assert.Equal(0, f.Outbound.QueuedFrames);
            Assert.True(f.State.RejectedClientProjectileUpdates > 0);
        }
    }

    [Fact]
    public void Actual_phase_retirement_clears_pending_proof_with_and_without_fallback_input_writes()
    {
        using var source = Source();
        foreach (bool fullTick in new[] { false, true })
        {
            using var f = new Fixture(Row(source));
            f.Report(0);
            f.State.Tick();
            var member = f.Member();
            Assert.NotNull(member.RemotePhaseSnapshot);
            ulong input = member.ProjectileUseInputRevision;
            member.PhysicsPhase = null; // Missing source state makes the next real player phase retire.
            if (fullTick) f.State.Tick();
            else Assert.False(f.Players().TickRemotePlayerPhase());
            Assert.Null(member.ItemPhase);
            Assert.Null(member.RemotePhaseSnapshot);
            Assert.Null(member.RemotePhaseItem);
            // The full tick's fallback health writer also advances input; isolate retirement
            // through its real owner entry point to exercise the unchanged-epoch boundary.
            if (!fullTick) Assert.Equal(input, member.ProjectileUseInputRevision);
            var before = f.Random.Clone();
            f.Complete();
            Assert.Equal(0, f.Store.ActiveCount);
            Assert.Equal(20, f.Ammo());
            Assert.True(f.Random.HasSameState(before));
            Assert.Equal(0, f.Outbound.QueuedFrames);
            Assert.True(f.State.RejectedClientProjectileUpdates > 0);
        }
    }

    internal static JsonDocument Source()
    {
        using var stream = typeof(PendingBulletPlayerPhase1458Tests).Assembly.GetManifestResourceStream("RangedBulletLaunch1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    internal static JsonElement Row(JsonDocument source) => source.RootElement.GetProperty("launches").EnumerateArray().First(x =>
        x.GetProperty("weapon").GetInt32() == 534 && x.GetProperty("seed").GetInt32() == 0 &&
        x.GetProperty("aimDirection").GetInt32() == 1 && x.GetProperty("ammo").GetInt32() == 97 &&
        !x.GetProperty("ammoBox").GetBoolean() && !x.GetProperty("ammoPotion").GetBoolean() &&
        (!x.TryGetProperty("prefix", out var prefix) || prefix.GetInt32() == 0));

    internal sealed class Fixture : IDisposable
    {
        internal readonly ServerRuntimeState State;
        internal readonly RuntimeProjectileStore Store = new();
        internal readonly RuntimeProjectileReplicationRegistry Registry = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(0);
        internal readonly TerrariaConnectionOutboundQueue Outbound = new(new OutboundQueueOptions(32, 16_384, 1_024));
        internal readonly PlayerJoinSession Session;
        internal readonly ConnectionHandle Connection;
        internal readonly TerrariaProjectileUpdateState[] Packets;
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession? replacement;

        internal Fixture(JsonElement row, VanillaItemPrefixWorld1458? itemPrefixWorld = null)
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(worldTiles: tiles, npcs: new RuntimeNpcStore(capacity: 8), projectiles: Store,
                projectileReplication: Registry, naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
                playerUpdateRandomSeed: new(0), itemPrefixWorld: itemPrefixWorld);
            Assert.True(slots.TryAcquireConnection(out var lease));
            Session = new(lease!); Session.ObserveWorldRequest(); Session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99100), Session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(Session.Slot, 400, 400)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(Session.Slot, 200, 200)));
            SetItem(0, 534, 1); SetItem(54, 97, 20);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, Session, new(Session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Move(1600);
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(99101),
                new PlayerHandle(new(1), new(1)));
            Assert.True(Registry.TryRegister(peer.Source, Outbound));
            var peerSpawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 100, 103, 0, 0, 0, 0, 0);
            Registry.PlayerSpawned(peer, in peerSpawn);
            // The source corpus launches at400/400. An independent untouched interior source call
            // at1600/1606 preserves every velocity/debit/cursor and translates positions by1200/1206.
            Packets = row.GetProperty("shots").EnumerateArray().Select((shot, index) => new TerrariaProjectileUpdateState(
                new(0, checked((ushort)(701 + index)), 1), shot.GetProperty("type").GetInt16(),
                shot.GetProperty("position").GetProperty("X").GetSingle() + 1200,
                shot.GetProperty("position").GetProperty("Y").GetSingle() + 1206,
                shot.GetProperty("velocity").GetProperty("X").GetSingle(), shot.GetProperty("velocity").GetProperty("Y").GetSingle(),
                0, 0, 0, 0, shot.GetProperty("damage").GetInt16(), shot.GetProperty("knockBack").GetSingle(), 0)).ToArray();
            Assert.Equal(5, Packets.Length);
        }

        internal PlayerStateSnapshot Snapshot() { Assert.True(State.TryCapturePlayerSnapshot(Connection.Player, out var p)); return p; }
        internal void Report(int index) => State.Apply(new ClientProjectileUpdateRuntimeCommand(Connection, Packets[index]));
        internal void Complete() { for (int i = 1; i < Packets.Length; i++) Report(i); }
        internal void Move(float x) => State.Apply(new PlayerMovementRuntimeCommand(Connection,
            new(Session.Slot, 64, 16, 0, 0, 0, x, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        internal void SetItem(short slot, short type, short stack) => State.Apply(new PlayerEquipmentRuntimeCommand(Connection,
            new(Session.Slot, slot, stack, 0, type, 0)));
        internal short Ammo() { Assert.True(State.TryCapturePlayerInventoryItem(Connection.Player, 54, out var item)); return item.Stack; }
        internal void ReplacePlayerGeneration()
        {
            State.Apply(new PlayerDisconnectRuntimeCommand(Connection));
            Session.Dispose();
            Assert.True(slots.TryAcquireConnection(out var lease));
            replacement = new(lease!); replacement.ObserveWorldRequest(); replacement.ObserveSectionRequest();
            Assert.Equal(Connection.Player.Slot, replacement.Slot);
            Assert.NotEqual(Connection.Player.Generation, replacement.Handle.Generation);
            var current = new ConnectionHandle(Connection.Source, replacement.Handle);
            State.Apply(new PlayerSpawnRuntimeCommand(current, replacement,
                new(replacement.Slot, 100, 103, 0, 0, 0, 0, 0)));
        }
        internal void MutatePoseWithoutCommand()
        {
            Member().PositionX = 1601f;
        }
        internal RuntimePlayerMember Member()
        {
            var players = Players();
            var membership = typeof(PlayerAuthority).GetField("membership", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
            var members = (System.Collections.IEnumerable)membership.GetType().GetProperty("Members")!.GetValue(membership)!;
            return members.Cast<RuntimePlayerMember>().Single();
        }
        internal PlayerAuthority Players()
        {
            var runtime = typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            return (PlayerAuthority)runtime.GetType().GetProperty("Players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        }
        public void Dispose() { replacement?.Dispose(); Session.Dispose(); }
    }
}
