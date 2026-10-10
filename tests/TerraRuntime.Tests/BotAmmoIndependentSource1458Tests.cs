using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Network;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

// Explicit BOT predictive, deterministic ammo policy; original Shoot rows only prove
// retained ammo chronology. No original Minishark conservation/RNG or whole Main claim.
public sealed class BotAmmoIndependentSource1458Tests
{
    [Fact]
    public void Ammo_trust_and_use_owners_are_adopted_before_any_attack_observer()
    {
        AssertOriginalBirthChronology();
        foreach (var profile in new[] { (39, 40), (39, 47), (98, 97), (98, 278), (219, 97) })
        foreach (short stack in new short[] { 1, 20 })
        foreach (string callback in new[] { "plain", "ammo", "actor", "config", "target", "expiry", "projectile", "reentry", "throw", "throw-ammo", "expiry-old", "spawn-ammo", "presentation-actor", "spawn-throw" })
        {
            var f = new Fixture(profile.Item1, stack, profile.Item2);
            bool first = true;
            bool mutated = false;
            f.Events.Observe = kind =>
            {
                if (first)
                {
                    first = false;
                    f.AssertAccepted(stack);
                }
                if (mutated || callback == "spawn-ammo" && kind != "spawn" ||
                    callback == "presentation-actor" && kind != "presentation" ||
                    callback == "spawn-throw" && kind != "spawn") return;
                mutated = true;
                switch (callback)
                {
                    case "spawn-ammo":
                    case "ammo":
                        Assert.True(f.Players.SetItem(f.Id, new(54, new ItemTypeId(f.Ammo), 77, default, 0)));
                        break;
                    case "presentation-actor":
                    case "actor":
                        Assert.True(f.Players.Despawn(f.Id));
                        Assert.True(f.Players.Create(f.Id, 160, 160).IsCreated);
                        Assert.True(f.Players.SetItem(f.Id, new(54, new ItemTypeId(f.Ammo), 77, default, 0)));
                        break;
                    case "config":
                        f.Bot.Configuration = f.Bot.Configuration with { WeaponPolicy = RuntimeBotWeaponPolicy.Melee };
                        break;
                    case "target":
                        Assert.True(f.Players.Despawn(f.TargetId));
                        Assert.True(f.Players.Create(f.TargetId, 900, 160).IsCreated);
                        break;
                    case "expiry":
                        f.Bot.NextAttackTick = 777;
                        f.Bot.UseItemUntilTick = 888;
                        break;
                    case "expiry-old":
                        f.Bot.NextAttackTick = 0;
                        f.Bot.UseItemUntilTick = 999;
                        Assert.Equal(RuntimeBotActionResult.Pending().Status, f.Attack().Status);
                        break;
                    case "throw-ammo":
                        Assert.True(f.Players.SetItem(f.Id, new(54, new ItemTypeId(f.Ammo), 77, default, 0)));
                        throw new InvalidOperationException("ammo callback");
                    case "projectile":
                        Assert.True(f.Shots.TryGetActive(0, out var old));
                        Assert.True(f.Shots.TryDespawn(old.Handle, out _));
                        Assert.True(f.Shots.TrySpawn(0, new ProjectileStateUpdate(old.Type, old.Spawner, old.PositionX, old.PositionY, old.VelocityX, old.VelocityY, old.Ai, old.BannerIdToRespondTo, 77, old.KnockBack, old.OriginalDamage), out var replacement));
                        Assert.NotEqual(old.Handle, replacement.Handle);
                        break;
                    case "reentry":
                        _ = f.Attack();
                        break;
                    case "spawn-throw":
                    case "throw":
                        throw new InvalidOperationException("ammo callback");
                }
            };
            if (callback is "throw" or "throw-ammo" or "spawn-throw")
                Assert.Equal("ammo callback", Assert.Throws<InvalidOperationException>(() => f.Attack()).Message);
            else
                Assert.Equal(RuntimeBotActionResult.Success.Status, f.Attack().Status);
            Assert.False(first);
            Assert.True(mutated);
            Assert.Equal(1, f.Projectiles.AppliedSpawns);
            Assert.True(f.Players.TryGetItem(f.Id, 54, out var final));
            Assert.Equal(callback is "ammo" or "actor" or "throw-ammo" or "spawn-ammo" or "presentation-actor" ? 77 : stack - 1, final.Stack);
            if (callback is "actor" or "presentation-actor")
            {
                Assert.True(f.Players.TryGetPlayer(f.Id, out var replacement));
                Assert.NotEqual(f.Bot.Player, replacement);
            }
            if (callback == "expiry-old")
            {
                Assert.Equal(0, f.Bot.NextAttackTick);
                Assert.Equal(999, f.Bot.UseItemUntilTick);
            }
            else if (callback == "expiry")
            {
                Assert.Equal(777, f.Bot.NextAttackTick);
                Assert.Equal(888, f.Bot.UseItemUntilTick);
            }
            else
            {
                Assert.Equal(f.UseTime, f.Bot.NextAttackTick);
                Assert.Equal(f.Animation, f.Bot.UseItemUntilTick);
            }
            if (callback == "projectile")
            {
                Assert.True(f.Shots.TryGetActive(0, out var replacement));
                Assert.Equal(77, replacement.Damage);
                Assert.All(f.Events.Spawns, shot => Assert.Equal(77, shot.Damage));
                Assert.False(f.Shots.IsCombatTrusted(replacement.Handle));
            }
            if (callback is "actor" or "presentation-actor")
                Assert.DoesNotContain("spawn", f.Events.Seen);
            if (callback is "ammo" or "spawn-ammo" or "throw-ammo" or "actor" or "presentation-actor")
                {
                Assert.Single(f.Events.Items);
                Assert.All(f.Events.Items, item => Assert.Equal(77, item.Stack));
            }
            if (callback is "throw" or "spawn-throw")
            {
                Assert.Contains("spawn", f.Events.Seen);
                Assert.Contains("item", f.Events.Seen);
                Assert.Contains("presentation", f.Events.Seen);
            }
            if (callback == "plain")
            {
                Assert.Contains("move", f.Events.Seen);
                Assert.Contains("presentation", f.Events.Seen);
                Assert.Contains("spawn", f.Events.Seen);
                Assert.Contains("item", f.Events.Seen);
                Assert.Equal(1, f.Events.Seen.Count(x => x == "spawn"));
                Assert.Equal(1, f.Events.Seen.Count(x => x == "item"));
            }
        }
        // A coherent observation still owns its actor/pose after earlier same-tick
        // item or consumable producers legitimately advance the actor revision.
        foreach (string earlierProducer in new[] { "inventory", "buff", "vitals", "move", "hostile" })
        {
            var f = new Fixture(39, 20);
            Assert.True(f.Players.SetItem(f.Id, new(1, VanillaItemIds.ArcheryPotion, 1, default, 0)));
            var observation = f.Observation();
            if (earlierProducer == "inventory")
                Assert.True(f.Players.SetItem(f.Id, new(2, new(3), 1, default, 0)));
            else if (earlierProducer == "buff")
            {
                f.Inventory.UseCombatBuffs(observation);
                Assert.True(f.Players.TryGetItem(f.Id, 1, out var potion));
                Assert.True(potion.IsEmpty);
                Assert.True(f.Bot.ActiveBuffs.ContainsKey(VanillaBuffIds.Archery));
            }
            else if (earlierProducer == "vitals")
                Assert.True(f.Players.SetVitals(f.Id, new(123, 400, 17, 200)));
            else if (earlierProducer == "move")
                Assert.True(f.Players.TryTeleport(f.Id, 190, 170));
            else
                Assert.True(f.Players.SetHostile(f.Id, true));
            f.Events.Seen.Clear();
            Assert.Equal(RuntimeBotActionResult.Success.Status, f.Combat.Attack(observation).Status);
            Assert.True(f.Players.TryGet(f.Bot.Player, out var accepted));
            Assert.Equal(earlierProducer == "vitals" ? 123 : 400, accepted.Life);
            Assert.Equal(earlierProducer == "vitals" ? 17 : 200, accepted.Mana);
            Assert.Equal(1, f.Shots.ActiveCount);
            var birth = Assert.Single(f.Events.Spawns);
            Assert.Equal(accepted.PositionX + 10f, birth.PositionX);
            Assert.Equal(accepted.PositionY + 21f, birth.PositionY);
            if (earlierProducer == "move") Assert.Equal(190f, accepted.PositionX);
            if (earlierProducer == "hostile") Assert.True(accepted.Hostile);
            Assert.True(f.Players.TryGetItem(f.Id, 54, out var consumedAmmo));
            Assert.Equal(19, consumedAmmo.Stack);
        }
        // Real encoded birth journal/baseline ownership, distinct from original Shoot wire.
        var joined = new Fixture(98, 20, replication: true);
        TerrariaConnectionOutboundQueue? newcomer = null;
        joined.Events.Observe = _ =>
        {
            if (newcomer is not null) return;
            joined.AssertAccepted(20);
            newcomer = joined.RegisterPeer(3, 7713);
        };
        Assert.Equal(RuntimeBotActionResult.Success.Status, joined.Attack().Status);
        Assert.NotNull(newcomer);
        Assert.Equal(1, joined.Outbound!.QueuedFrames);
        Assert.Equal(1, newcomer!.QueuedFrames);
        Assert.Equal(1, joined.Replication.BaselineFrames);
        Assert.Single(joined.Events.Spawns);
        var afterReturn = joined.RegisterPeer(4, 7714);
        Assert.Equal(1, afterReturn.QueuedFrames);
        Assert.Equal(2, joined.Replication.BaselineFrames);

        foreach (string joinTiming in new[] { "before-provider-tail", "endpoint-replaced", "occupation-replaced" })
        {
            var f = new Fixture(98, 20, replication: true);
            TerrariaConnectionOutboundQueue? replacementQueue = null;
            if (joinTiming == "before-provider-tail")
            {
                replacementQueue = f.RegisterPeer(3, 7713, playing: false);
                f.OnTick = () => f.MarkPeerPlaying(3, 7713);
            }
            else
            {
                bool called = false;
                f.Events.Observe = _ =>
                {
                    if (called) return;
                    called = true;
                    f.AssertAccepted(20);
                    if (joinTiming == "endpoint-replaced")
                    {
                        Assert.True(f.Replication.TryUnregister(GameCommandSourceId.FromConnection(7712)));
                        replacementQueue = f.RegisterPeer(2, 7712);
                    }
                    else
                    {
                        var connection = f.Peer(2, 7712);
                        f.Replication.PlayerDisconnected(connection);
                        f.MarkPeerPlaying(2, 7712);
                        replacementQueue = f.Outbound;
                    }
                };
            }
            Assert.Equal(RuntimeBotActionResult.Success.Status, f.Attack().Status);
            Assert.NotNull(replacementQueue);
            Assert.Equal(1, replacementQueue!.QueuedFrames);
            Assert.Equal(joinTiming == "endpoint-replaced" ? 0 : 1, f.Outbound!.QueuedFrames);
            Assert.Equal(joinTiming == "before-provider-tail" ? 0 : 1, f.Replication.BaselineFrames);
            Assert.Single(f.Events.Spawns);
        }
    }

    [Fact]
    public void Rejected_allocation_and_stale_commands_never_publish_or_debit()
    {
        foreach (string earlierMutation in new[] { "dead", "actor", "unknown" })
        {
            var f = new Fixture(98, 20);
            var observation = f.Observation();
            if (earlierMutation == "dead")
                Assert.True(f.Players.SetVitals(f.Id, new(0, 400, 200, 200)));
            else if (earlierMutation == "actor")
            {
                Assert.True(f.Players.Despawn(f.Id));
                Assert.True(f.Players.Create(f.Id, 160, 160).IsCreated);
            }
            else
            {
                Assert.True(f.Players.Despawn(f.Id));
                var created = f.Players.Create(f.Id, 160, 160);
                Assert.True(created.IsCreated);
                f.Bot.Player = created.Player;
                observation = f.Observation();
                Assert.False(observation.Self.HasHealth);
            }
            f.Events.Seen.Clear();
            _ = f.Combat.Attack(observation);
            Assert.Equal(0, f.Shots.ActiveCount);
            Assert.Equal(0, f.Projectiles.AppliedSpawns);
            Assert.Empty(f.Events.Seen);
        }

        foreach (int weapon in new[] { 39, 98, 219 })
        foreach (string refusal in new[] { "pool", "ammo", "actor", "config", "observation", "tick", "overflow" })
        {
            var f = new Fixture(weapon, 20, capacity: refusal == "pool" ? 1 : 3);
            var observation = f.Observation();
            if (refusal == "pool")
                Assert.True(f.Shots.TrySpawn(0, new(new ProjectileTypeId(14), f.Bot.Player.Slot.Value,
                    170, 181, 1, 0, default, 0, 1, 0, 0), out _));
            if (refusal == "ammo")
                Assert.True(f.Players.SetItem(f.Id, new(54, VanillaItemIds.None, 0, default, 0)));
            if (refusal == "actor")
            {
                Assert.True(f.Players.Despawn(f.Id));
                Assert.True(f.Players.Create(f.Id, 160, 160).IsCreated);
                Assert.True(f.Players.SetItem(f.Id, new(54, new ItemTypeId(f.Ammo), 77, default, 0)));
            }
            if (refusal == "config")
                f.Bot.Configuration = f.Bot.Configuration with { WeaponPolicy = RuntimeBotWeaponPolicy.Melee };
            if (refusal == "observation")
                f.Bot.ObservationRevision++;
            if (refusal == "tick")
                f.Bot.CurrentTick++;
            if (refusal == "overflow")
            {
                f.Bot.CurrentTick = long.MaxValue;
                observation = f.Observation(long.MaxValue);
            }
            Assert.True(f.Players.TryGetPlayer(f.Id, out var currentHandle));
            Assert.True(f.Players.TryGet(currentHandle, out var before));
            Assert.True(f.Players.TryGetItem(f.Id, 54, out var beforeAmmo));
            f.Events.Seen.Clear();
            _ = f.Combat.Attack(observation);
            Assert.True(f.Players.TryGet(currentHandle, out var after));
            Assert.Equal(before, after);
            Assert.True(f.Players.TryGetItem(f.Id, 54, out var afterAmmo));
            Assert.Equal(beforeAmmo, afterAmmo);
            Assert.Empty(f.Events.Seen);
            Assert.Equal(0, f.Projectiles.AppliedSpawns);
            Assert.Equal(0, f.Bot.NextAttackTick);
            Assert.Equal(0, f.Bot.UseItemUntilTick);
        }
        foreach (string mutation in new[] { "ammo-aba", "actor", "config", "goal", "observation", "tick", "target", "pool", "terrain" })
        {
            var f = new Fixture(98, 20);
            bool called = false;
            PlayerStateSnapshot retained = default;
            ServerPlayerItemState retainedAmmo = default;
            PlayerHandle current = default;
            f.OnTick = () =>
            {
                if (called) return;
                called = true;
                switch (mutation)
                {
                    case "ammo-aba":
                        Assert.True(f.Players.SetItem(f.Id, new(54, new(97), 21, default, 0)));
                        Assert.True(f.Players.SetItem(f.Id, new(54, new(97), 20, default, 0)));
                        break;
                    case "actor":
                        Assert.True(f.Players.Despawn(f.Id));
                        Assert.True(f.Players.Create(f.Id, 160, 160).IsCreated);
                        Assert.True(f.Players.SetItem(f.Id, new(54, new(97), 77, default, 0)));
                        break;
                    case "config":
                        f.Bot.Configuration = f.Bot.Configuration with { WeaponPolicy = RuntimeBotWeaponPolicy.Bow };
                        break;
                    case "goal": f.Bot.GoalGeneration++; break;
                    case "observation": f.Bot.ObservationRevision++; break;
                    case "tick": f.Bot.CurrentTick++; break;
                    case "target":
                        Assert.True(f.Players.Despawn(f.TargetId));
                        Assert.True(f.Players.Create(f.TargetId, 900, 160).IsCreated);
                        break;
                    case "terrain":
                        f.Tiles.Set(14, 11, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                        break;
                    case "pool":
                        Assert.True(f.Shots.TrySpawn(0, new(new ProjectileTypeId(14), f.Bot.Player.Slot.Value,
                            170, 181, 1, 0, default, 0, 77, 0, 0), out _));
                        break;
                }
                Assert.True(f.Players.TryGetPlayer(f.Id, out current));
                Assert.True(f.Players.TryGet(current, out retained));
                Assert.True(f.Players.TryGetItem(f.Id, 54, out retainedAmmo));
                f.Events.Seen.Clear();
            };
            _ = f.Attack();
            Assert.True(called);
            Assert.True(f.Players.TryGet(current, out var after));
            Assert.Equal(retained, after);
            Assert.True(f.Players.TryGetItem(f.Id, 54, out var afterAmmo));
            Assert.Equal(retainedAmmo, afterAmmo);
            Assert.Empty(f.Events.Seen);
            Assert.Equal(0, f.Projectiles.AppliedSpawns);
            Assert.Equal(0, f.Bot.NextAttackTick);
            Assert.Equal(0, f.Bot.UseItemUntilTick);
        }
    }

    private static void AssertOriginalBirthChronology()
    {
        using Stream stream = typeof(BotAmmoIndependentSource1458Tests).Assembly.GetManifestResourceStream("BotAmmoBirthOrderSource1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var memory = new MemoryStream();
        gzip.CopyTo(memory);
        Assert.Equal("6b4d503d26aabe18cfaa7f89ad56a98fa1bdbaa456893fe328bfc350ca6efa42", Convert.ToHexString(SHA256.HashData(memory.ToArray())).ToLowerInvariant());
        using var document = JsonDocument.Parse(memory.ToArray());
        var rows = document.RootElement.GetProperty("rows");
        Assert.Equal(6, rows.GetArrayLength());
        int conserved = 0;
        foreach (var row in rows.EnumerateArray())
        {
            Assert.Equal(1, row.GetProperty("netMode").GetInt32());
            Assert.Equal(0, row.GetProperty("myPlayer").GetInt32());
            var after = row.GetProperty("ammoAfter");
            var publications = row.GetProperty("publications").EnumerateArray().ToArray();
            var births = publications.Where(x => x.GetProperty("packet").GetInt32() == 27).ToArray();
            Assert.Single(births);
            var birth = births[0];
            Assert.True(JsonElement.DeepEquals(after, birth.GetProperty("actualAmmo")));
            Assert.True(birth.GetProperty("sameAmmoReference").GetBoolean());
            Assert.True(JsonElement.DeepEquals(row.GetProperty("afterShoot"), row.GetProperty("after")));
            Assert.Equal(row.GetProperty("nextAfter").GetInt32(), birth.GetProperty("next").GetInt32());
            foreach (var publication in publications)
            {
                Assert.Equal(1, publication.GetProperty("globals").GetProperty("netMode").GetInt32());
                Assert.Equal(0, publication.GetProperty("globals").GetProperty("myPlayer").GetInt32());
                Assert.True(publication.GetProperty("globals").GetProperty("dedServ").GetBoolean());
                Assert.Equal(56, publication.GetProperty("random").GetProperty("state").GetArrayLength());
            }
            if (after.GetProperty("stack").GetInt32() == row.GetProperty("initialAmmoStack").GetInt32())
                conserved++;
        }
        Assert.Equal(2, conserved); // Source Minishark seed1 and Endless reference only.
    }

    internal sealed class Fixture
    {
        internal readonly ServerPlayerId Id = new("ammo-bot");
        internal readonly ServerPlayerId TargetId = new("ammo-target");
        internal readonly Events Events = new();
        internal readonly ServerPlayerAuthority Players;
        internal readonly RuntimeProjectileStore Shots;
        internal readonly WorldTileStore Tiles;
        internal readonly RuntimeProjectileReplicationRegistry Replication = new();
        internal readonly TerrariaConnectionOutboundQueue? Outbound;
        internal readonly ProjectileAuthority Projectiles;
        internal readonly RuntimeBotCombat Combat;
        internal readonly RuntimeBotInventory Inventory;
        internal readonly BotState Bot;
        internal readonly PlayerHandle Target;
        internal readonly WorldRuntimeIdentity World = new(new(Guid.Parse("11111111-1111-1111-1111-111111111111")), new(Guid.Parse("22222222-2222-2222-2222-222222222222")));
        internal readonly int Ammo;
        internal readonly int UseTime;
        internal readonly int Animation;
        internal Action? OnTick;
        private int ownerReads;
        internal Fixture(int weapon, short stack, int? ammo = null, int capacity = 3, bool replication = false, VanillaBulletSourceArithmetic1458 itemPrefixArithmetic = VanillaBulletSourceArithmetic1458.CoreClrSingle)
        {
            Ammo = ammo ?? (weapon == 39 ? 40 : 97);
            UseTime = weapon == 39 ? 30 : weapon == 98 ? 8 : 14;
            Animation = weapon == 98 ? 8 : UseTime;
            var slots = new PlayerSlotPool(8);
            var ids = new ServerPlayerSlotRegistry(slots);
            Players = new(new ServerPlayerStateStore(ids, 8), ids, events: Events);
            var created = Players.Create(Id, 160, 160);
            Assert.True(created.IsCreated);
            Assert.True(Players.SetVitals(Id, new(400, 400, 200, 200)));
            var configuration = new RuntimeBotConfiguration(RuntimeBotMode.Guard, default) with { WeaponPolicy = weapon == 39 ? RuntimeBotWeaponPolicy.Bow : RuntimeBotWeaponPolicy.Gun };
            var loadout = RuntimePlayerBotLoadoutCatalog1458.Pick(new Random(42)) with { BowWeapon = new(39), ArrowAmmo = new(weapon == 39 ? Ammo : 40), GunWeapon = new(weapon == 39 ? 98 : weapon), BulletAmmo = new(weapon == 39 ? 97 : Ammo) };
            Bot = new(1, Id, "probe", configuration, loadout, default, default, 0) { Player = created.Player, ObservationRevision = 1 };
            Assert.True(Players.SetItem(Id, new(0, new(weapon), 1, default, 0)));
            Assert.True(Players.SetItem(Id, new(54, new(Ammo), stack, default, 0)));
            var target = Players.Create(TargetId, 300, 160);
            Assert.True(target.IsCreated);
            Target = target.Player;
            var tiles = Tiles = new WorldTileStore(new WorldDimensions(300, 120));
            var human = new PlayerAuthority(null, tiles);
            var worldItems = new WorldItemAuthority(human, new(), new SystemWorldItemSpawnRandom(0), null);
            var inventory = Inventory = new RuntimeBotInventory(Bot, Players, worldItems, new(), World);
            Shots = new(capacity, replication ? new ProjectileEvents(Replication, Events) : Events);
            if (replication) Outbound = RegisterPeer(2, 7712);
            Projectiles = new(Shots, human, new RuntimeNpcStore(), new Lookup(Players, created.Player, Target, () => { if (++ownerReads == 2) OnTick?.Invoke(); }), null, replication ? Replication : null, () => { OnTick?.Invoke(); return 0; });
            Combat = new(Bot, Players, null!, Projectiles, tiles, inventory, [Bot], World, itemPrefixArithmetic);
            Events.Seen.Clear();
            Events.Items.Clear();
        }
        internal TerrariaConnectionOutboundQueue RegisterPeer(byte slot, long source, bool playing = true)
        {
            var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 16384, 1024));
            var connection = Peer(slot, source);
            Assert.True(Replication.TryRegister(connection.Source, queue));
            if (playing) MarkPeerPlaying(slot, source);
            return queue;
        }
        internal ConnectionHandle Peer(byte slot, long source) => new(GameCommandSourceId.FromConnection(source),
            new(new PlayerSlotId(slot), new PlayerSessionGeneration(1)));
        internal void MarkPeerPlaying(byte slot, long source)
        {
            var connection = Peer(slot, source);
            var request = new PlayerSpawnCommitRequest(connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0);
            Replication.PlayerSpawned(connection, request);
        }
        internal RuntimeBotObservationSnapshot Observation(long tick = 0)
        {
            Assert.True(Players.TryGet(Bot.Player, out var self));
            return new(Bot.Id, World, 1, Bot.GoalGeneration, tick, self, Bot.Configuration, null,
                new BotGuardTarget(default, Target, 310, 181, 0, 0, 20, 42), null, false, null, null);
        }
        internal RuntimeBotActionResult Attack() => Combat.Attack(Observation());
        internal void AssertAccepted(short stack)
        {
            Assert.True(Players.TryGet(Bot.Player, out var player));
            Assert.Equal(0, player.SelectedItem);
            Assert.True((player.ControlFlags & (1 << 5)) != 0);
            Assert.Equal(Animation, player.ItemAnimation);
            Assert.NotNull(player.ItemRotation);
            Assert.Equal(UseTime, Bot.NextAttackTick);
            Assert.Equal(Animation, Bot.UseItemUntilTick);
            Assert.True(Players.TryGetItem(Id, 54, out var ammo));
            Assert.Equal(stack - 1, ammo.Stack);
            Assert.Equal(stack == 1 ? VanillaItemIds.None : new ItemTypeId(Ammo), ammo.ItemType);
            Assert.Equal(default, ammo.Prefix);
            Assert.Equal(0, ammo.ItemFlags);
            Assert.True(Shots.TryGetActive(0, out var shot));
            Assert.True(Shots.TryGetCombatTrustedOwner(shot.Handle, out var owner));
            Assert.Equal(Bot.Player, owner);
            Assert.Equal(1, Projectiles.AppliedSpawns);
            Assert.Equal(1, Shots.ActiveCount);
        }
    }
    internal sealed class Events : IRuntimeServerPlayerEventSink, IProjectileStateCommitSink
    {
        internal Action<string>? Observe;
        internal readonly List<string> Seen = [];
        internal readonly List<ServerPlayerItemState> Items = [];
        internal readonly List<ProjectileSnapshot> Spawns = [];
        private void Notify(string kind) { Seen.Add(kind); Observe?.Invoke(kind); }
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot shot) { if (kind == ProjectileStateCommitKind.Spawn) { Spawns.Add(shot); Notify("spawn"); } }
        public void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item) { Items.Add(item); Notify("item"); }
        public void ServerPlayerMoved(in PlayerStateSnapshot player) => Notify("move");
        public void ServerPlayerItemUsePresented(PlayerHandle player, float rotation, short animation) => Notify("presentation");
        public void ServerPlayerCreated(in PlayerStateSnapshot player) { }
        public void ServerPlayerAppearanceUpdated(PlayerHandle player, in ServerPlayerAppearanceState value) { }
        public void ServerPlayerVitalsUpdated(PlayerHandle player, in ServerPlayerVitalsState value) { }
        public void ServerPlayerPvpUpdated(PlayerHandle player, bool value) { }
        public void ServerPlayerGodModeUpdated(PlayerHandle player, bool value) { }
        public void ServerPlayerDied(PlayerHandle player, DamageSource source, ProjectileTypeId type, int damage, int direction) { }
        public void ServerPlayerBuffTypesUpdated(PlayerHandle player, ReadOnlySpan<BuffTypeId> value) { }
        public void ServerPlayerRecallPresented(in PlayerStateSnapshot player, short x, short y) { }
        public void ServerPlayerDespawned(PlayerHandle player) { }
    }
    private sealed class ProjectileEvents(RuntimeProjectileReplicationRegistry replication, Events events) : IProjectileStateCommitSink
    {
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            replication.ProjectileStateCommitted(kind, snapshot);
            events.ProjectileStateCommitted(kind, snapshot);
        }
    }
    private sealed class Lookup(ServerPlayerAuthority players, PlayerHandle owner, PlayerHandle target, Action readOwner) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            if (owner.Slot == slot)
            {
                readOwner();
                if (players.TryGet(owner, out snapshot)) return true;
            }
            if (target.Slot == slot && players.TryGet(target, out snapshot))
                return true;
            snapshot = default;
            return false;
        }
    }
}
