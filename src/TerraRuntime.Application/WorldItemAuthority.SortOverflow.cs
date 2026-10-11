using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
namespace TerraRuntime.Application;
internal sealed partial class WorldItemAuthority
{
    internal bool IsValidSortOverflowOffer(in PlayerStateSnapshot state,in WorldItemDropStateUpdate proposal,
        WorldItemPickupSlot1458 expected)
    {
        if(expected.Type is <71 or >74||expected.Stack is <1 or >short.MaxValue||expected.Prefix!=0||expected.Favorite||
            !VanillaWorldItemOwner1458.TryGetSourceCreationNoGravity(new(expected.Type),out bool noGravity)||
            !IsSourceSpawnVelocity(proposal.VelocityX,-30,31)||
            !IsSourceSpawnVelocity(proposal.VelocityY,noGravity?-30:-40,noGravity?31:-15))return false;
        var body=state.HasMount?VanillaPlayerMountHitbox1458.Resolve(state.MountType):(Width:20f,Height:42f);
        var exact=new WorldItemDropStateUpdate(state.PositionX+body.Width/2f-VanillaWorldItemOwner1458.PhysicalWidth/2f,
            state.PositionY+body.Height/2f-VanillaWorldItemOwner1458.PhysicalHeight/2f,
            proposal.VelocityX,proposal.VelocityY,checked((short)expected.Stack),0,
            WorldItemOwnershipMode.ReserveForLocalPlayer,checked((short)expected.Type),false,0,100);
        return proposal==exact;
    }
    internal bool TryCompleteSortOverflow(ConnectionHandle connection,RuntimePlayerMember member,
        PlayerAuthority.PendingCursorMove receipt,PlayerAuthority.PendingShopJournal journal,
        RuntimePlayerInventoryStore.SortOverflowPreparation inventory)
    {
        if(ownedPickupTransactionRunning||journal.SortOverflow is not {} pending||!ReferenceEquals(pending.Owner,this)||
            pending.Offers.Count!=pending.Expected.Length||pending.Offers.Count!=inventory.GroundFunding.Length||
            SpawnRandom is not SystemWorldItemSpawnRandom source||
            !players.IsCurrentSortOverflowInventory(connection,member,receipt,journal,inventory))return false;
        var live=source.SourceRandom;var before=live.Clone();var after=before.Clone();
        var captures=players.CaptureItemOwnerPlayers();
        var views=captures.Select(c=>
        {
            var body=c.State.HasMount?VanillaPlayerMountHitbox1458.Resolve(c.State.MountType):(Width:20f,Height:42f);
            return new WorldItemAllocationPlayer1458(c.State.Player.Slot.Value,c.State.PositionX,c.State.PositionY,(int)body.Width,(int)body.Height);
        }).ToArray();
        using var allocation=worldItems.CreateAllocationPreview(views);
        for(int i=0;i<pending.Offers.Count;i++)
        {
            var offer=pending.Offers[i];
            if(!IsValidSortOverflowOffer(member.CaptureSnapshot(),offer,pending.Expected[i])||
                !VanillaWorldItemOwner1458.TryGetSourceCreationNoGravity(new(offer.ItemNetId),out bool noGravity))return false;
            _=after.Next(-30,31);_=noGravity?after.Next(-30,31):after.Next(-40,-15);
            if(!allocation.TrySpawnFundedCoinSource(offer,out short slot,connection.Player.Slot.Value,inventory.GroundFunding[i])||
                slot>=worldItems.Capacity)return false;
        }
        // The claim can invoke the owner-facts provider. Every pure joint guard follows it.
        if(!allocation.TryClaim()||!players.IsCurrentItemOwnerPlayers(captures)||!live.HasSameState(before)||
            !players.IsCurrentSortOverflowInventory(connection,member,receipt,journal,inventory))return false;
        ownedPickupTransactionRunning=true;
        try
        {
            if(!allocation.TryAdoptUnpublished(out var publication)||publication is null)
                throw new InvalidOperationException("Claimed Sort overflow lost callback-free world adoption.");
            players.AdoptSortOverflowInventory(connection,member,receipt,journal,inventory);
            live.CopyStateFrom(after);AppliedAllocations+=pending.Offers.Count;
            Exception? failure=null;
            while(true)
            {
                try{if(!publication.TryPublishNext(out _))break;}
                catch(Exception exception){failure??=exception;}
            }
            try{players.PublishSortOverflowInventory(connection,member,journal,inventory.Inventory);}
            catch(Exception exception){failure??=exception;}
            if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }
        finally{ownedPickupTransactionRunning=false;}
    }
}
