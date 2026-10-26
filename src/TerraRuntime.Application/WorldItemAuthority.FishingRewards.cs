using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class WorldItemAuthority
{
    internal bool TryFishingRewards(ConnectionHandle connection, short debitSlot, int debitType,
        bool debitAfterRewards, Func<FishingRewardSession, bool> roll, Func<bool>? guard = null, Action? adopt = null,
        RuntimePlayerInventoryStore.ShopJournalCapture? sourceCapture = null)
    {
        if (ownedPickupTransactionRunning || SpawnRandom is not SystemWorldItemSpawnRandom source ||
            !players.TryPrepareFishingRewardInventory(connection, debitSlot, debitType, out var capture, out var inventory, sourceCapture) ||
            capture is null || inventory is null || players.PickupWorld is not { } world ||
            players.PickupWindowsArithmetic is not { } windows || guard?.Invoke() == false) return false;
        var live = source.SourceRandom; var before = live.Clone(); var after = before.Clone();
        var captures = players.CaptureItemOwnerPlayers();
        var views = captures.Select(c =>
        {
            var body = c.State.HasMount ? VanillaPlayerMountHitbox1458.Resolve(c.State.MountType) : (Width: 20f, Height: 42f);
            return new WorldItemAllocationPlayer1458(c.State.Player.Slot.Value, c.State.PositionX, c.State.PositionY, (int)body.Width, (int)body.Height);
        }).ToArray();
        using var allocation = worldItems.CreateAllocationPreview(views);
        var session = new FishingRewardSession(after, inventory, allocation, capture.Player, world, windows);
        if ((!debitAfterRewards && !inventory.TryDebit()) || !roll(session) || session.Failed ||
            (debitAfterRewards && !inventory.TryDebit()) || !inventory.TrySeal() ||
            guard?.Invoke() == false || !allocation.TryClaim() || guard?.Invoke() == false ||
            !players.IsCurrentItemOwnerPlayers(captures) || !players.IsCurrentProjectileUse(capture) ||
            !inventory.IsCurrent || !live.HasSameState(before))
            return false;
        ownedPickupTransactionRunning = true;
        try
        {
            if (!allocation.TryAdoptUnpublished(out var publication) || publication is null || !inventory.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared fishing reward lost callback-free joint adoption.");
            live.CopyStateFrom(after);
            adopt?.Invoke(); // Pure authoritative participant; no observers until all owners agree.
            Exception? failure = null;
            try { while (publication.TryPublishNext(out _)) { } }
            catch (Exception exception) { failure = exception; }
            try { players.PublishFishingRewards(capture, inventory); }
            catch (Exception exception) { failure ??= exception; }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }
        finally { ownedPickupTransactionRunning = false; }
    }

    internal sealed class FishingRewardSession(VanillaUnifiedRandom1458 random,
        RuntimePlayerInventoryStore.FishingRewardPreparation inventory,
        RuntimeWorldItemStore.AllocationPreview allocation, PlayerStateSnapshot player,
        VanillaItemPrefixWorld1458 world, bool windows)
    {
        private int grants;
        internal VanillaUnifiedRandom1458 Random => random;
        internal bool Failed { get; private set; }
        internal bool MatchesReports(ReadOnlySpan<RuntimePlayerInventoryItem> reports, ReadOnlySpan<bool> seen) =>
            inventory.MatchesReports(reports, seen);

        internal bool QuickSpawn(int type, int stack = 1)
        {
            if (!VanillaItemIds.TryCreate(type, out var item) ||
                VanillaItemPrefixNormalization1458.ResolveNatural(item, random.Next, windows, out var prefix, world) !=
                    VanillaItemPrefixNormalizationStatus1458.Resolved) return Fail();
            return Grant(type, stack, prefix.Value);
        }

        internal bool Grant(int type, int stack = 1, int prefix = 0, bool canGoIntoVoidVault = true)
        {
            if (Failed || ++grants > 64 || stack is <= 0 or > short.MaxValue ||
                !VanillaItemIds.TryCreate(type, out var itemType) || !VanillaPrefixIds.TryCreate(prefix, out _) ||
                !VanillaDefinitionCatalog.IsValidKnownStack(itemType, checked((short)stack)) ||
                !inventory.TryGrant(new(type, stack, prefix, false), world, canGoIntoVoidVault, out var residual)) return Fail();
            if (residual.Type == 0 || residual.Stack <= 0) return true;
            if (!VanillaWorldItemOwner1458.TryGetSourceCreationNoGravity(itemType, out bool noGravity)) return Fail();
            var body = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (Width: 20f, Height: 42f);
            var drop = new WorldItemDropStateUpdate(player.PositionX + body.Width / 2f - VanillaWorldItemOwner1458.PhysicalWidth / 2f,
                player.PositionY + body.Height / 2f - VanillaWorldItemOwner1458.PhysicalHeight / 2f,
                random.Next(-30, 31) * .1f, (noGravity ? random.Next(-30, 31) : random.Next(-40, -15)) * .1f,
                checked((short)residual.Stack), checked((byte)prefix), WorldItemOwnershipMode.ReserveForLocalPlayer,
                checked((short)type), false, 0, 100);
            return allocation.TrySpawnSource(drop, 0, out short slot, player.Player.Slot.Value) && slot < RuntimeWorldItemStore.VanillaCapacity || Fail();
        }
        private bool Fail() { Failed = true; return false; }
    }
}
