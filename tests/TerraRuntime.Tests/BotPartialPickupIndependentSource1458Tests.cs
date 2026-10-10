using TerraRuntime.Application;
using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;


// Original GetItem capacity/residual references; trusted Bot slot0 protection and
// matching-before-empty order remain explicit runtime policy, not vanilla storage parity.
public sealed class BotPartialPickupIndependentSource1458Tests
{
    [Fact]
    public void Original_capacities_drive_partial_fanout_and_protected_storage_policy()
    {
        using var source = ReadSource();
        var rows = source.RootElement.GetProperty("rows");
        Assert.Equal(168, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            Assert.Equal(row.GetProperty("beforeRng").GetRawText(), row.GetProperty("afterRng").GetRawText());
            Assert.Equal(row.GetProperty("beforeNext").GetInt32(), row.GetProperty("afterNext").GetInt32());
            int accepted = 0;
            var before = row.GetProperty("before");
            var after = row.GetProperty("after");
            for (int slot = 0; slot < 58; slot++)
                if (after[slot].GetProperty("type").GetInt32() == row.GetProperty("id").GetInt32())
                    accepted += after[slot].GetProperty("stack").GetInt32() -
                        (before[slot].GetProperty("type").GetInt32() == row.GetProperty("id").GetInt32() ? before[slot].GetProperty("stack").GetInt32() : 0);
            var residual = row.GetProperty("residual");
            int left = residual.GetProperty("type").GetInt32() == 0 ? 0 : residual.GetProperty("stack").GetInt32();
            Assert.Equal(20, accepted + left);
        }
        foreach (int type in new[] { 40, 47, 97, 278, 188, 499, 3544, 110, 189, 500, 2209 })
        foreach (string profile in new[] { "capacity9", "two_capacity", "capacity9_empty49", "full" })
        {
            var original = rows.EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == type && r.GetProperty("profile").GetString() == profile);
            var f = new Fixture(type);
            f.Fill();
            short first = type is 40 or 47 or 97 or 278 ? (short)54 : (short)1;
            if (profile != "full") f.Set(first, type, 9990, 1);
            if (profile == "two_capacity") f.Set((short)(first + 1), type, 9992);
            if (profile == "capacity9_empty49") f.Set(49, 0, 0);
            // A compatible held slot must never be touched by this host Bot policy.
            f.Set(0, type, 9990);
            f.Seal();
            int notifications = 0;
            f.Events.OnItem = (_, _) => notifications++;
            int left = original.GetProperty("residual").GetProperty("type").GetInt32() == 0 ? 0 : original.GetProperty("residual").GetProperty("stack").GetInt32();
            Assert.Equal(profile == "full" ? RuntimeBotActionStatus.Failure : RuntimeBotActionStatus.Success, f.Pickup().Status);
            Assert.True(f.Players.TryGetItem(f.Id, 0, out var held));
            Assert.Equal(9990, held.Stack);
            Assert.Equal(profile == "full" ? 0 : profile == "capacity9" ? 1 : 2, notifications);
            if (left != 0)
            {
                Assert.True(f.Items.TryGetActive(f.Item.Handle.Slot, out var remaining));
                Assert.Equal(left, remaining.Stack);
                Assert.Equal(f.Item.Handle, remaining.Handle);
                Assert.Equal(f.Item, remaining with { Stack = f.Item.Stack, Revision = f.Item.Revision });
                Assert.Equal(profile == "full", remaining.Revision == f.Item.Revision);
            }
            else Assert.False(f.Items.TryGetActive(f.Item.Handle.Slot, out _));
            if (profile != "full")
            {
                Assert.True(f.Players.TryGetItem(f.Id, first, out var filled));
                Assert.Equal(9999, filled.Stack);
                Assert.Equal((byte)1, filled.ItemFlags);
            }
            if (profile == "two_capacity")
            {
                Assert.True(f.Players.TryGetItem(f.Id, (short)(first + 1), out var second));
                Assert.Equal(9999, second.Stack);
            }
            Assert.Equal(0, f.Leases.Count);
        }
        // Bot matching-before-empty policy deliberately differs from original FillAmmo ordering.
        var matchingFirst = new Fixture(40); matchingFirst.Fill(); matchingFirst.Set(54, 0, 0); matchingFirst.Set(1, 40, 9990); matchingFirst.Seal();
        var matchingOrder = new List<short>(); matchingFirst.Events.OnItem = (_, item) => matchingOrder.Add(item.Slot);
        Assert.Equal(RuntimeBotActionStatus.Success, matchingFirst.Pickup().Status);
        Assert.Equal(new short[] { 1, 54 }, matchingOrder);
        Assert.True(matchingFirst.Players.TryGetItem(matchingFirst.Id, 1, out var ordinaryMatching)); Assert.Equal(9999, ordinaryMatching.Stack);
        Assert.True(matchingFirst.Players.TryGetItem(matchingFirst.Id, 54, out var newAmmo)); Assert.Equal(11, newAmmo.Stack);

        var maximum = new Fixture(40, 53);
        maximum.Fill();
        foreach (short slot in Enumerable.Range(1, 49).Concat(Enumerable.Range(54, 4)).Select(i => (short)i)) maximum.Set(slot, 40, 9998);
        maximum.Set(0, 40, 9998); maximum.Seal();
        var maximumOrder = new List<short>();
        maximum.Events.OnItem = (_, item) => maximumOrder.Add(item.Slot);
        Assert.Equal(RuntimeBotActionStatus.Success, maximum.Pickup().Status);
        Assert.Equal(Enumerable.Range(54, 4).Concat(Enumerable.Range(1, 49)).Select(i => (short)i), maximumOrder);
        Assert.Equal(53, maximumOrder.Count);
        Assert.False(maximum.Items.TryGetActive(maximum.Item.Handle.Slot, out _));
        Assert.True(maximum.Players.TryGetItem(maximum.Id, 0, out var heldMaximum)); Assert.Equal(9998, heldMaximum.Stack);

        var decorated = new Fixture(decorated: true); decorated.Fill(); decorated.Set(1, 3544, 9990); decorated.Seal();
        Assert.Equal(RuntimeBotActionStatus.Success, decorated.Pickup().Status);
        Assert.True(decorated.Items.TryGetActive(decorated.Item.Handle.Slot, out var decoratedResidual));
        Assert.Equal(11, decoratedResidual.Stack);
        Assert.Equal(decorated.Item, decoratedResidual with { Stack = decorated.Item.Stack, Revision = decorated.Item.Revision });

        // A malformed world stack is not broadened into a valid fanout import.
        var malformed = new Fixture(3544, 10000);
        malformed.Seal(); int malformedOffers = 0;
        malformed.Events.OnItem = (_, _) => malformedOffers++;
        Assert.Equal(RuntimeBotActionStatus.Failure, malformed.Pickup().Status);
        Assert.Equal(0, malformedOffers);
        Assert.True(malformed.Items.TryGetActive(malformed.Item.Handle.Slot, out var malformedWorld)); Assert.Equal(malformed.Item, malformedWorld);
        Assert.True(malformed.Players.TryGetItem(malformed.Id, 1, out var malformedSlot)); Assert.True(malformedSlot.IsEmpty);
    }

    [Fact]
    public void Batch_and_world_owners_precede_callbacks_and_suppress_only_stale_components()
    {
        foreach (string mode in new[] { "plain", "later-slot", "world-body", "world-replacement", "throw", "throw-later", "actor" })
        {
            var f = new Fixture();
            f.Fill(); f.Set(1, 3544, 9990); f.Set(2, 3544, 9992); f.Seal();
            int acceptedWorldPublications = 0;
            f.WorldEvents.Callback = (kind, snapshot) =>
            {
                if (kind == WorldItemStateCommitKind.Drop && snapshot.Handle == f.Item.Handle && snapshot.Stack == 4 && snapshot.Revision.Value == f.Item.Revision.Value + 1) acceptedWorldPublications++;
            };
            var seen = new List<short>();
            bool entered = false;
            f.Events.OnItem = (player, item) =>
            {
                if (entered) return;
                seen.Add(item.Slot);
                if (seen.Count != 1) return;
                entered = true;
                Assert.True(f.Players.TryGetItem(f.Id, 1, out var first));
                Assert.True(f.Players.TryGetItem(f.Id, 2, out var second));
                Assert.Equal(9999, first.Stack); Assert.Equal(9999, second.Stack);
                Assert.True(f.Items.TryGetActive(f.Item.Handle.Slot, out var residual));
                Assert.Equal(4, residual.Stack); Assert.Equal(f.Item.Handle, residual.Handle);
                Assert.Equal(f.Item, residual with { Stack = f.Item.Stack, Revision = f.Item.Revision });
                Assert.NotEqual(f.Item.Revision, residual.Revision);
                Assert.Equal(f.Item.Handle, f.Bot.LastPickedItem);
                if (mode is "later-slot" or "throw-later") f.Set(2, 3, 77);
                if (mode == "world-body") Assert.True(f.Items.TryAdvanceMotion(residual.Handle, 161, 160, 0, 0, out _));
                if (mode == "world-replacement")
                {
                    Assert.True(f.Items.TryRemove(residual.Handle.Slot, out _));
                    Assert.True(f.Items.TryAllocate(f.Drop with { Stack = 77 }, out var replacement));
                    Assert.NotEqual(residual.Handle.Generation, replacement.Handle.Generation);
                }
                if (mode == "actor") { Assert.True(f.Players.Despawn(f.Id)); Assert.True(f.Players.Create(f.Id, 160, 160).IsCreated); }
                entered = false;
                if (mode is "throw" or "throw-later") throw new InvalidOperationException("batch observer");
            };
            if (mode is "throw" or "throw-later") Assert.Equal("batch observer", Assert.Throws<InvalidOperationException>(() => f.Pickup()).Message);
            else Assert.Equal(RuntimeBotActionStatus.Success, f.Pickup().Status);
            Assert.Equal(mode is "later-slot" or "actor" or "throw-later" ? new short[] { 1 } : new short[] { 1, 2 }, seen);
            Assert.Equal(0, f.Leases.Count);
            Assert.Equal(mode is "world-body" or "world-replacement" ? 0 : 1, acceptedWorldPublications);
            if (mode == "later-slot") { Assert.True(f.Players.TryGetItem(f.Id, 2, out var newer)); Assert.Equal(77, newer.Stack); }
            if (mode == "world-replacement") { Assert.True(f.Items.TryGetActive(f.Item.Handle.Slot, out var newer)); Assert.Equal(77, newer.Stack); }
        }
        foreach (string mode in new[] { "item-aba", "world-aba", "lease", "configuration" })
        {
            var f = new Fixture(); f.Fill(); f.Set(1, 3544, 9990); f.Seal();
            int offers = 0; f.Events.OnItem = (_, _) => offers++;
            f.Items.AttachOwnerFactsProvider(new TailProvider(() =>
            {
                if (mode == "item-aba") { f.Set(1, 3, 1); f.Set(1, 3544, 9990); }
                if (mode == "world-aba") { Assert.True(f.Items.TryRemove(f.Item.Handle.Slot, out _)); Assert.True(f.Items.TryAllocate(f.Drop, out _)); }
                if (mode == "configuration") f.Bot.Configuration = f.Bot.Configuration with { Mode = RuntimeBotMode.Idle };
                if (mode == "lease")
                {
                    var owner = new RuntimeBotLeaseOwner(f.Bot.Id, f.Bot.Player, f.Observation.World);
                    var key = RuntimeBotResourceKey.ForItem(f.Observation.World, f.Item.Handle);
                    Assert.True(f.Leases.Release(owner, key));
                    Assert.True(f.Leases.TryAcquire(owner with { BotId = owner.BotId + 1 }, key, f.Bot.CurrentTick));
                }
            }));
            Assert.Equal(RuntimeBotActionStatus.Failure, f.Pickup().Status);
            Assert.Equal(mode == "item-aba" ? 2 : 0, offers);
            Assert.True(f.Players.TryGetItem(f.Id, 1, out var intact)); Assert.Equal(9990, intact.Stack);
        }

        var direct = new Fixture(); direct.Set(0, 3, 17); direct.Seal();
        Assert.True(direct.Players.TryGet(direct.Bot.Player, out var beforeBatch));
        Assert.True(direct.Players.TryGetItem(direct.Id, 1, out var old1));
        Assert.True(direct.Players.TryGetItem(direct.Id, 2, out var old2));
        var next1 = new ServerPlayerItemState(1, VanillaItemIds.SuperHealingPotion, 9, VanillaPrefixIds.None, 255);
        var next2 = new ServerPlayerItemState(2, VanillaItemIds.SuperHealingPotion, 7, VanillaPrefixIds.None, 0);
        Assert.False(direct.Players.TryPrepareItemMutations(direct.Id, beforeBatch, new[] { old1, old1 }, new[] { next1, next1 }, out _));
        Assert.False(direct.Players.TryPrepareItemMutations(direct.Id, beforeBatch, new[] { old1, old2 }, new[] { next1, next2 with { Stack = 10000 } }, out _));
        Assert.True(direct.Players.TryGet(direct.Bot.Player, out var afterRefused)); Assert.Equal(beforeBatch, afterRefused);
        Assert.True(direct.Players.TryGetItem(direct.Id, 1, out var untouched)); Assert.Equal(old1, untouched);
        Assert.True(direct.Players.TryPrepareItemMutations(direct.Id, beforeBatch, new[] { old1, old2 }, new[] { next1, next2 }, out var batch));
        Assert.True(batch!.TryAdoptUnpublished()); Assert.False(batch.TryAdoptUnpublished());
        Assert.True(direct.Players.TryGet(direct.Bot.Player, out var acceptedBatch));
        Assert.Equal(beforeBatch.Revision.Value + 1, acceptedBatch.Revision.Value);
        Assert.True(batch.IsAcceptedCurrent);
        Assert.True(direct.Players.TryGetItem(direct.Id, 1, out var normalized)); Assert.Equal((byte)1, normalized.ItemFlags);
        var notifications = new List<short>();
        direct.Events.OnItem = (_, item) => notifications.Add(item.Slot);
        Assert.True(batch.TryPublishItems()); Assert.False(batch.TryPublishItems());
        Assert.Equal(new short[] { 1, 2 }, notifications);

    }

    private static System.Text.Json.JsonDocument ReadSource()
    {
        using var stream = typeof(BotPartialPickupIndependentSource1458Tests).Assembly.GetManifestResourceStream("BotPartialGetItemSource1458");
        Assert.NotNull(stream);
        using var gzip = new System.IO.Compression.GZipStream(stream!, System.IO.Compression.CompressionMode.Decompress);
        using var memory = new MemoryStream(); gzip.CopyTo(memory);
        Assert.Equal("b884da74fdfc2666bc6b2bd819dc5d57a22adc4c64c20f5a355bbda083b1c657", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(memory.ToArray())).ToLowerInvariant());
        return System.Text.Json.JsonDocument.Parse(memory.ToArray());
    }
    internal sealed class Fixture
    {
        public readonly ServerPlayerId Id = new("probe");
        public readonly Events Events = new();
        public readonly WorldEvents WorldEvents = new();
        public readonly ServerPlayerAuthority Players;
        public readonly ServerPlayerStateStore States;
        public readonly RuntimeWorldItemStore Items;
        public readonly WorldItemAuthority WorldItems;
        public readonly RuntimeBotResourceLeases Leases = new();
        public readonly BotState Bot;
        public readonly RuntimeBotInventory Inventory;
        public RuntimeBotObservationSnapshot Observation;
        public readonly WorldItemSnapshot Item;
        public readonly WorldItemStateUpdate Drop = new(160, 160, 0, 0, 1, 0, WorldItemOwnershipMode.None,
            (short)VanillaItemIds.SuperHealingPotion.Value, false, 0, 0, 255, 0, 255, 0);

        public Fixture(int type = 3544, short stack = 20, bool decorated = false)
        {
            Drop = Drop with { ItemNetId = (short)type, Stack = stack };
            if (decorated) Drop = Drop with { VelocityX = 0.1f, VelocityY = -0.1f, Shimmered = true,
                EnemyGrabDelayTime = 7, OwnerPlayerId = 0, TimeToKeepReservation = 17 };
            var pool = new PlayerSlotPool(8);
            var identities = new ServerPlayerSlotRegistry(pool);
            States = new ServerPlayerStateStore(identities, 8);
            Players = new(States, identities, events: Events);
            var created = Players.Create(Id, 160, 160);
            Assert.True(created.IsCreated);
            Assert.True(Players.SetVitals(Id, new(400, 400, 200, 200)));
            Items = new(WorldEvents);
            var tiles = new WorldTileStore(new WorldDimensions(300, 120));
            WorldItems = new(new PlayerAuthority(null, tiles), Items, new SystemWorldItemSpawnRandom(0), null);
            WorldRuntimeIdentity world = new(new(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                new(Guid.Parse("22222222-2222-2222-2222-222222222222")));
            Bot = new(1, Id, "probe", new(RuntimeBotMode.Collect, default),
                RuntimePlayerBotLoadoutCatalog1458.Pick(new Random(42)) with { ArrowAmmo = new ItemTypeId(type), BulletAmmo = new ItemTypeId(type) }, default, default, 0)
            {
                Player = created.Player,
                ObservationRevision = 1
            };
            Inventory = new(Bot, Players, WorldItems, Leases, world);
            Assert.True(Items.TryAllocate(Drop, out Item));
            Assert.True(Players.TryGet(created.Player, out var self));
            Observation = new(Bot.Id, world, 1, 1, 0, self, Bot.Configuration, null, null, Item, false, null, null);
        }

        public void Seal()
        {
            Assert.True(Players.TryGet(Bot.Player, out var self));
            Observation = Observation with { Self = self, Configuration = Bot.Configuration };
        }

        public void Set(short slot, int type, int stack, byte flags = 0) =>
            Assert.True(Players.SetItem(Id, new(slot, new ItemTypeId(type), (short)stack, VanillaPrefixIds.None, flags)));

        public void Fill()
        {
            for (short slot = 0; slot < 58; slot++) Set(slot, 3, 9999);
        }

        public RuntimeBotActionResult Pickup() => Inventory.Pickup(Observation, Item.Handle);
    }

    internal sealed class WorldEvents : IWorldItemStateCommitSink
    {
        public int Removals;
        public int Updates;
        public Action<WorldItemStateCommitKind, WorldItemSnapshot>? Callback;
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
            Callback?.Invoke(kind, snapshot);
            if (kind != WorldItemStateCommitKind.Remove) Updates++;
            if (kind == WorldItemStateCommitKind.Remove)
                Removals++;
        }
    }

    private sealed class TailProvider(Action callback) : IWorldItemOwnerFactsProvider1458, IWorldItemOwnerFactsSnapshot1458
    {
        public IWorldItemOwnerFactsSnapshot1458 Capture() { callback(); return this; }
        public bool IsCurrent => true;
        public bool TrySelectOwner(in WorldItemDropStateUpdate drop, int delay, byte grab, out byte owner)
        {
            owner = 255;
            return true;
        }
    }

    internal sealed class Events : IRuntimeServerPlayerEventSink
    {
        public Action<PlayerHandle, ServerPlayerItemState>? OnItem;
        public void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item) => OnItem?.Invoke(player, item);
        public void ServerPlayerCreated(in PlayerStateSnapshot player) { }
        public void ServerPlayerAppearanceUpdated(PlayerHandle player, in ServerPlayerAppearanceState value) { }
        public void ServerPlayerVitalsUpdated(PlayerHandle player, in ServerPlayerVitalsState value) { }
        public void ServerPlayerPvpUpdated(PlayerHandle player, bool value) { }
        public void ServerPlayerGodModeUpdated(PlayerHandle player, bool value) { }
        public void ServerPlayerDied(PlayerHandle player, DamageSource source, ProjectileTypeId type, int damage, int direction) { }
        public void ServerPlayerBuffTypesUpdated(PlayerHandle player, ReadOnlySpan<BuffTypeId> buffs) { }
        public void ServerPlayerMoved(in PlayerStateSnapshot player) { }
        public void ServerPlayerItemUsePresented(PlayerHandle player, float rotation, short ticks) { }
        public void ServerPlayerRecallPresented(in PlayerStateSnapshot player, short x, short y) { }
        public void ServerPlayerDespawned(PlayerHandle player) { }
    }
}
