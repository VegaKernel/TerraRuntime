using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class WorldItemAuthority
{
    internal bool OwnsWiringItems(RuntimeWorldItemStore store, IWorldItemSpawnRandom random) =>
        ReferenceEquals(worldItems, store) && ReferenceEquals(SpawnRandom, random);

    internal bool CanCreateWiringStatue(ItemTypeId item) => SpawnRandom is SystemWorldItemSpawnRandom && VanillaItemStatue1458.IsSupported(item) &&
        (!VanillaItemStatue1458.NeedsCalendar(item) || seasonalContext is not null);

    internal bool TryCreateWiringStatue(ItemTypeId item, int x, int y, int verticalOffset, Func<bool> currentSource)
    {
        if (ownedPickupTransactionRunning || !CanCreateWiringStatue(item)) return false;
        ownedPickupTransactionRunning = true;
        try
        {
            var live = ((SystemWorldItemSpawnRandom)SpawnRandom).SourceRandom;
            var checkpoint = live.Clone();
            var captures = players.CaptureItemOwnerPlayers();
            var views = captures.Select(c =>
            {
                var body = c.State.HasMount ? TerraRuntime.Gameplay.Players.VanillaPlayerMountHitbox1458.Resolve(c.State.MountType) : (Width: 20f, Height: 42f);
                return new TerraRuntime.Gameplay.Items.WorldItemAllocationPlayer1458(c.State.Player.Slot.Value,
                    c.State.PositionX, c.State.PositionY, (int)body.Width, (int)body.Height);
            }).ToArray();
            using var allocation = worldItems.CreateAllocationPreview(views);
            if (!allocation.AllowsItemStatue(item, x, y)) return false;
            var request = new WorldItemDropStateUpdate(x - 8, y - verticalOffset - 8, 0, 0,
                1, 0, WorldItemOwnershipMode.None, checked((short)item.Value), false, 0, 0);
            if (!TryPrepareSourceCreation(in request, out var created, out _, out _, out var after, out var calendar, useSuppliedVelocity: false) ||
                !live.HasSameState(checkpoint) || !allocation.TrySpawnSource(in created, 0, out _) ||
                !allocation.TryClaim()) return false;
            // Finish external calendar, item-owner and clock queries before the
            // callback-free player/store/RNG comparison and joint adoption.
            bool calendarCurrent = seasonalContext is null || seasonalContext() == calendar;
            bool ownerCurrent = allocation.IsCurrent;
            bool sourceCurrent = currentSource();
            if (!calendarCurrent || !ownerCurrent || !sourceCurrent ||
                !players.IsCurrentItemOwnerPlayers(captures) || !allocation.IsCurrentOwned ||
                !live.HasSameState(checkpoint)) return false;
            if (!allocation.TryAdoptUnpublished(out var publication) || publication is null)
                throw new InvalidOperationException("Accepted statue item lost its allocation participant.");
            live.CopyStateFrom(after!);
            while (publication.TryPublishNext(out _)) { }
            return true;
        }
        finally { ownedPickupTransactionRunning = false; }
    }
}
