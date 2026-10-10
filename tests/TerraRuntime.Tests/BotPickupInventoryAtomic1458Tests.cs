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

// Trusted host BOT producer ownership checks. Original PickupItem/Receive151 chronology
// is independent cache evidence; these tests do not claim vanilla GetItem storage parity.
public sealed class BotPickupInventoryAtomic1458Tests
{
    [Fact]
    public void Pickup_adopts_both_owners_before_observers_and_preserves_reentry()
    {
        foreach (string mode in new[] { "plain", "inventory", "replacement", "throw" })
        {
            var f = new Fixture();
            int offers = 0;
            f.Events.OnItem = (player, item) =>
            {
                // The callback's own SetItem has a separate legitimate notification.
                if (offers != 0)
                    return;
                offers++;
                Assert.False(f.Items.TryGetActive(f.Item.Handle.Slot, out _));
                Assert.True(f.Players.TryGetItem(f.Id, item.Slot, out var accepted));
                Assert.Equal(item, accepted);
                if (mode == "inventory")
                    Assert.True(f.Players.SetItem(f.Id, new(item.Slot, VanillaItemIds.StoneBlock, 77, VanillaPrefixIds.None, 0)));
                if (mode == "replacement")
                {
                    Assert.True(f.Items.TryAllocate(f.Drop with { ItemNetId = 3, Stack = 77 }, out var replacement));
                    Assert.Equal(f.Item.Handle.Slot, replacement.Handle.Slot);
                    Assert.NotEqual(f.Item.Handle.Generation, replacement.Handle.Generation);
                }
                if (mode == "throw")
                    throw new InvalidOperationException("expected observer throw");
            };

            if (mode == "throw")
                Assert.Equal("expected observer throw", Assert.Throws<InvalidOperationException>(() => f.Pickup()).Message);
            else
                Assert.Equal(RuntimeBotActionStatus.Success, f.Pickup().Status);
            Assert.Equal(1, offers);
            Assert.Equal(mode == "replacement" ? 0 : 1, f.WorldEvents.Removals);
            Assert.True(f.Players.TryGetItem(f.Id, 1, out var final));
            Assert.Equal(mode == "inventory" ? VanillaItemIds.StoneBlock : VanillaItemIds.SuperHealingPotion, final.ItemType);
            Assert.Equal(mode == "inventory" ? 77 : 1, final.Stack);
            if (mode == "replacement")
            {
                Assert.True(f.Items.TryGetActive(f.Item.Handle.Slot, out var replacement));
                Assert.Equal(77, replacement.Stack);
                Assert.NotEqual(f.Item.Handle.Generation, replacement.Handle.Generation);
            }
            Assert.Equal(RuntimeBotActionStatus.Failure, f.Pickup().Status);
            Assert.Equal(1, offers);
            Assert.Equal(0, f.Leases.Count);
        }
    }

    [Fact]
    public void Provider_currentness_generation_and_once_guards_refuse_stale_proposals()
    {
        var provider = new Fixture();
        provider.Items.AttachOwnerFactsProvider(new TailProvider(() =>
            provider.Bot.Configuration = provider.Bot.Configuration with { Mode = RuntimeBotMode.Idle }));
        int offers = 0;
        provider.Events.OnItem = (_, _) => offers++;
        Assert.Equal(RuntimeBotActionStatus.Failure, provider.Pickup().Status);
        Assert.Equal(RuntimeBotMode.Idle, provider.Bot.Configuration.Mode);
        Assert.Equal(0, offers);
        Assert.Equal(0, provider.WorldEvents.Removals);
        Assert.True(provider.Items.TryGetActive(provider.Item.Handle.Slot, out var intact));
        Assert.Equal(provider.Item, intact);
        Assert.Equal(0, provider.Leases.Count);

        var leaseChanged = new Fixture();
        var leaseOwner = new RuntimeBotLeaseOwner(leaseChanged.Bot.Id, leaseChanged.Bot.Player, leaseChanged.Observation.World);
        var resource = RuntimeBotResourceKey.ForItem(leaseChanged.Observation.World, leaseChanged.Item.Handle);
        var replacementOwner = leaseOwner with { BotId = leaseOwner.BotId + 1 };
        leaseChanged.Items.AttachOwnerFactsProvider(new TailProvider(() =>
        {
            Assert.True(leaseChanged.Leases.Release(leaseOwner, resource));
            Assert.True(leaseChanged.Leases.TryAcquire(replacementOwner, resource, leaseChanged.Bot.CurrentTick));
        }));
        int leaseOffers = 0;
        leaseChanged.Events.OnItem = (_, _) => leaseOffers++;
        Assert.Equal(RuntimeBotActionStatus.Failure, leaseChanged.Pickup().Status);
        Assert.Equal(0, leaseOffers);
        Assert.Equal(0, leaseChanged.WorldEvents.Removals);
        Assert.True(leaseChanged.Items.TryGetActive(leaseChanged.Item.Handle.Slot, out var leaseIntact));
        Assert.Equal(leaseChanged.Item, leaseIntact);
        Assert.True(leaseChanged.Leases.IsOwned(replacementOwner, resource, leaseChanged.Bot.CurrentTick));
        Assert.Equal(1, leaseChanged.Leases.Count);

        var empty = new Fixture();
        Assert.True(empty.Players.TryGet(empty.Bot.Player, out var emptyBefore));
        Assert.True(empty.Players.TryGetItem(empty.Id, 1, out var emptyOld));
        var oneItem = new ServerPlayerItemState(1, VanillaItemIds.SuperHealingPotion, 1, VanillaPrefixIds.None, 0);
        Assert.True(empty.Players.TryPrepareItemMutation(empty.Id, emptyBefore, emptyOld, oneItem, out var initialNull));
        Assert.True(initialNull!.TryAdoptUnpublished());
        Assert.True(initialNull.IsAcceptedCurrent);
        Assert.True(initialNull.TryPublish());
        Assert.True(empty.Players.TryGet(empty.Bot.Player, out var withItem));
        Assert.True(empty.Players.TryPrepareItemMutation(empty.Id, withItem, oneItem, emptyOld, out var clearLast));
        Assert.True(clearLast!.TryAdoptUnpublished());
        Assert.True(clearLast.IsAcceptedCurrent);
        Assert.True(empty.Players.TryGetItem(empty.Id, 1, out var cleared));
        Assert.Equal(emptyOld, cleared);
        Assert.True(clearLast.TryPublish());

        var f = new Fixture();
        Assert.True(f.Players.TryGet(f.Bot.Player, out var before));
        Assert.True(f.Players.TryGetItem(f.Id, 1, out var old));
        var next = new ServerPlayerItemState(1, VanillaItemIds.SuperHealingPotion, 2, VanillaPrefixIds.None, 0);
        Assert.True(f.Players.TryPrepareItemMutation(f.Id, before, old, next, out var stale));
        Assert.True(f.Players.SetItem(f.Id, old)); // Exact same value still advances the owned revision.
        Assert.False(stale!.IsCurrent);
        Assert.False(stale.TryAdoptUnpublished());
        Assert.True(f.Players.TryGetItem(f.Id, 1, out var unchanged));
        Assert.Equal(old, unchanged);

        Assert.True(f.Players.TryGet(f.Bot.Player, out before));
        Assert.True(f.Players.TryPrepareItemMutation(f.Id, before, old, next, out var once));
        Assert.True(once!.TryAdoptUnpublished());
        Assert.False(once.TryAdoptUnpublished());
        int publications = 0;
        f.Events.OnItem = (_, _) =>
        {
            publications++;
            throw new InvalidOperationException("once throw");
        };
        Assert.Equal("once throw", Assert.Throws<InvalidOperationException>(() => once.TryPublish()).Message);
        Assert.False(once.TryPublish());
        Assert.Equal(1, publications);
        f.Events.OnItem = null;

        Assert.True(f.WorldItems.TryPrepareTrustedTake(f.Item, out var motion));
        using (motion)
        {
            Assert.True(f.Items.TryAdvanceMotion(f.Item.Handle, 161, 160, 0, 0, out _));
            Assert.False(motion!.IsCurrent);
            Assert.False(motion.TryClaim());
        }
        Assert.True(f.Items.TryGetActive(f.Item.Handle.Slot, out var current));
        Assert.True(f.WorldItems.TryPrepareTrustedTake(current, out var generation));
        using (generation)
        {
            Assert.True(f.Items.TryRemove(current.Handle.Slot, out _));
            Assert.True(f.Items.TryAllocate(f.Drop, out var replacement));
            Assert.NotEqual(current.Handle.Generation, replacement.Handle.Generation);
            Assert.False(generation!.IsCurrent);
            Assert.False(generation.TryClaim());
        }
        Assert.True(f.Players.TryGet(f.Bot.Player, out before));
        Assert.True(f.Players.TryGetItem(f.Id, 1, out old));
        Assert.True(f.Players.TryPrepareItemMutation(f.Id, before, old, next, out var retired));
        Assert.True(f.Players.Despawn(f.Id));
        Assert.True(f.Players.Create(f.Id, 160, 160).IsCreated);
        Assert.False(retired!.IsCurrent);
        Assert.False(retired.TryAdoptUnpublished());
    }

    private sealed class Fixture
    {
        public readonly ServerPlayerId Id = new("probe");
        public readonly Events Events = new();
        public readonly WorldEvents WorldEvents = new();
        public readonly ServerPlayerAuthority Players;
        public readonly RuntimeWorldItemStore Items;
        public readonly WorldItemAuthority WorldItems;
        public readonly RuntimeBotResourceLeases Leases = new();
        public readonly BotState Bot;
        public readonly RuntimeBotInventory Inventory;
        public readonly RuntimeBotObservationSnapshot Observation;
        public readonly WorldItemSnapshot Item;
        public readonly WorldItemStateUpdate Drop = new(160, 160, 0, 0, 1, 0, WorldItemOwnershipMode.None,
            (short)VanillaItemIds.SuperHealingPotion.Value, false, 0, 0, 255, 0, 255, 0);

        public Fixture()
        {
            var pool = new PlayerSlotPool(8);
            var identities = new ServerPlayerSlotRegistry(pool);
            Players = new(new ServerPlayerStateStore(identities, 8), identities, events: Events);
            var created = Players.Create(Id, 160, 160);
            Assert.True(created.IsCreated);
            Assert.True(Players.SetVitals(Id, new(400, 400, 200, 200)));
            Items = new(WorldEvents);
            var tiles = new WorldTileStore(new WorldDimensions(300, 120));
            WorldItems = new(new PlayerAuthority(null, tiles), Items, new SystemWorldItemSpawnRandom(0), null);
            WorldRuntimeIdentity world = new(new(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                new(Guid.Parse("22222222-2222-2222-2222-222222222222")));
            Bot = new(1, Id, "probe", new(RuntimeBotMode.Collect, default),
                RuntimePlayerBotLoadoutCatalog1458.Pick(new Random(42)), default, default, 0)
            {
                Player = created.Player,
                ObservationRevision = 1
            };
            Inventory = new(Bot, Players, WorldItems, Leases, world);
            Assert.True(Items.TryAllocate(Drop, out Item));
            Assert.True(Players.TryGet(created.Player, out var self));
            Observation = new(Bot.Id, world, 1, 1, 0, self, Bot.Configuration, null, null, Item, false, null, null);
        }

        public RuntimeBotActionResult Pickup() => Inventory.Pickup(Observation, Item.Handle);
    }

    private sealed class WorldEvents : IWorldItemStateCommitSink
    {
        public int Removals;
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
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

    private sealed class Events : IRuntimeServerPlayerEventSink
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
