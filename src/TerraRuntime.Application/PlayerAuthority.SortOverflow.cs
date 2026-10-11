using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
namespace TerraRuntime.Application;
internal sealed partial class PlayerAuthority
{
    internal sealed class PendingSortOverflow(WorldItemAuthority owner, WorldItemPickupSlot1458[] expected)
    {
        internal WorldItemAuthority Owner => owner;
        internal WorldItemPickupSlot1458[] Expected => expected;
        internal readonly List<WorldItemDropStateUpdate> Offers = new();
        internal readonly RuntimePlayerInventoryStore.SortOverflowResolution Result = new();
    }
    internal bool TryRetainSortOverflow(WorldItemAuthority owner, WorldItemAllocateRuntimeCommand command)
    {
        if(!HasOwnedPickupContext||!membership.TryGet(command.Connection,out var member)||member.IsDead||
            member.ItemPhase is null||member.ProjectileUseInputRevision==ulong.MaxValue||
            (member.MovementFlags&VanillaPlayerHealthContext1458.GhostMovementFlag)!=0)return false;
        var receipt=member.CursorMove;var journal=receipt?.Shop;
        if(journal?.SortOverflow is null)
        {
            if(receipt is not null||!inventory.TryCaptureShopJournal(command.Connection,out var capture)||capture is null)return false;
            var image=capture.Before.AsSpan(0,59).ToArray().Select(i=>new WorldItemPickupSlot1458(i.ItemType.Value,i.Stack,i.Prefix.Value,
                (i.ItemFlags&PlayerEquipmentCommitRequest.FavoriteItemFlag)!=0)).ToArray();
            if(!VanillaWorldItemOwner1458.TryPlanSort(image,true,0,228,false,receiveWindowsArithmetic,receivePrefixWorld,
                out var plan)||plan is null||plan.Overflow is not {Length:>0 and <=4} expected)return false;
            foreach(var item in expected)if(item.Type is <71 or >74||item.Stack is <1 or >short.MaxValue||item.Prefix!=0||item.Favorite)return false;
            if(!owner.IsValidSortOverflowOffer(member.CaptureSnapshot(),command.State,expected[0]))return false;
            journal=new(capture,null,null){ObservedBatch=true,ObservedMember=member,ObservedActor=member.CaptureSnapshot(),
                ObservedInputRevision=member.ProjectileUseInputRevision,SortOverflow=new(owner,expected.ToArray())};
            receipt=new(member.CaptureSnapshot(),member.ItemPhase,member.ProjectileUseInputRevision,null,null,journal);
            member.CursorMove=receipt;
        }
        var pending=journal.SortOverflow!;
        if(!ReferenceEquals(pending.Owner,owner)||journal.IsSealed||journal.Invalid||journal.IsRetired||journal.LastIndex>=0||
            !journal.Capture.IsCurrent||!IsCurrentShopActor(member,receipt!)||pending.Offers.Count>=pending.Expected.Length||
            !owner.IsValidSortOverflowOffer(member.CaptureSnapshot(),command.State,pending.Expected[pending.Offers.Count]))
        {DropBankBatchAlternative(member,journal);RejectedEquipmentUpdates++;command.Completion?.TrySetResult(null);return true;}
        pending.Offers.Add(command.State);
        // A retained image has no published handle; the ordinary client has no completion callback.
        command.Completion?.TrySetResult(null);
        return true;
    }
    private bool TrySealSortOverflow(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal)
    {
        if(journal.SortOverflow is not {} pending)return false;
        if(pending.Offers.Count!=pending.Expected.Length||!journal.Capture.IsCurrent||!IsCurrentShopActor(member,receipt)||journal.Invalid)
        {DropBankBatchAlternative(member,journal);RejectedEquipmentUpdates++;return true;}
        journal.IsSealed=true;journal.PlanningAnimation=member.ItemAnimation;journal.PlanningItemTime=member.ItemPhase?.Selected.ItemTime??0;
        journal.Work=inventory.PrepareSortOverflowWork(connection,journal.Capture,journal.Reports,journal.Seen,
            pending.Expected,receivePrefixWorld,journal.PlanningAnimation,journal.PlanningItemTime,pending.Result).GetEnumerator();
        return true;
    }
    private void CompleteSortOverflowJournal(ConnectionHandle connection,RuntimePlayerMember member,
        PendingCursorMove receipt,PendingShopJournal journal)
    {
        var pending=journal.SortOverflow!;
        if(pending.Result.Ambiguous||pending.Result.Accepted is not {} accepted||!IsCurrentShopActor(member,receipt)||
            !journal.Capture.IsCurrent||member.Revision==ulong.MaxValue||member.ProjectileUseInputRevision==ulong.MaxValue||
            !pending.Owner.TryCompleteSortOverflow(connection,member,receipt,journal,accepted))
        {DropBankBatchAlternative(member,journal);RejectedEquipmentUpdates++;}
    }
    internal bool IsCurrentSortOverflowInventory(ConnectionHandle connection,RuntimePlayerMember member,
        PendingCursorMove receipt,PendingShopJournal journal,RuntimePlayerInventoryStore.SortOverflowPreparation prepared) =>
        membership.TryGet(connection,out var current)&&ReferenceEquals(current,member)&&ReferenceEquals(member.CursorMove,receipt)&&
        IsCurrentShopActor(member,receipt)&&journal.Capture.IsCurrent&&prepared.Inventory.IsCurrent&&member.Revision<ulong.MaxValue;
    internal void AdoptSortOverflowInventory(ConnectionHandle connection,RuntimePlayerMember member,
        PendingCursorMove receipt,PendingShopJournal journal,RuntimePlayerInventoryStore.SortOverflowPreparation prepared)
    {
        if(!IsCurrentSortOverflowInventory(connection,member,receipt,journal,prepared)||!prepared.Inventory.TryAdoptUnpublished())
            throw new InvalidOperationException("Prepared Sort overflow lost callback-free inventory adoption.");
        member.CursorMove=null;member.TryAdvanceRevision();MirrorAcceptedBank4Reports(connection,journal);
    }
    internal void PublishSortOverflowInventory(ConnectionHandle connection,RuntimePlayerMember member,
        PendingShopJournal journal,RuntimePlayerInventoryStore.ShopBankMovePreparation prepared) =>
        PublishAcceptedBankJournal(connection,member,journal,prepared);
}
