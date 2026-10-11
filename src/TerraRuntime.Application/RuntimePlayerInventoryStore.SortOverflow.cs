using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;
internal sealed partial class RuntimePlayerInventoryStore
{
    internal sealed record SortOverflowPreparation(ShopBankMovePreparation Inventory, long[] GroundFunding)
    {
        internal bool HasSameAcceptedImage(SortOverflowPreparation other) =>
            Inventory.HasSameAcceptedImage(other.Inventory) && GroundFunding.AsSpan().SequenceEqual(other.GroundFunding);
    }
    internal sealed class SortOverflowResolution
    {
        internal SortOverflowPreparation? Accepted;
        internal bool Ambiguous;
    }
    internal IEnumerable<int> PrepareSortOverflowWork(ConnectionHandle connection, ShopJournalCapture capture,
        RuntimePlayerInventoryItem[] reports, bool[] seen, WorldItemPickupSlot1458[] overflow,
        VanillaItemPrefixWorld1458? world, int animation,int itemTime, SortOverflowResolution result)
    {
        if (!capture.IsCurrent || reports.Length != 219 || seen.Length != 219 ||
            overflow.Length is < 1 or > 4 || Serial >= ulong.MaxValue - 1) yield break;
        foreach (var item in overflow)
            if (item.Type is < 71 or > 74 || item.Stack is < 1 or > short.MaxValue || item.Prefix != 0 || item.Favorite) yield break;
        var physical = capture.Before.AsSpan(0,59).ToArray().Select(StorageTuple).ToArray();
        for(byte locale=0;locale<12;locale++)for(int order=0;order<256;order++)
        {
            int classes=0;for(int shift=0;shift<8;shift+=2)classes|=1<<((order>>shift)&3);
            if(classes!=15)continue;
            for(int arithmetic=0;arithmetic<2;arithmetic++)
            {
                yield return 0;
                if(!VanillaWorldItemOwner1458.TryPlanSort(physical,true,locale,(byte)order,false,
                    arithmetic!=0,world,out var plan)||plan is null||
                    !(plan.Overflow??[]).AsSpan().SequenceEqual(overflow))continue;
                var after=capture.Before.ToArray();
                for(int i=0;i<59;i++)after[i]=InventoryTuple(plan.Slots[i]);
                if(!after.AsSpan().SequenceEqual(reports))continue;
                bool complete=true;for(int i=0;i<219;i++)if(after[i]!=capture.Before[i]&&!seen[i])complete=false;
                if(!complete)continue;
                var copper=capture.Copper.ToArray();var quantity=capture.Quantity;var caps=capture.Caps;
                if(!TryApplySortFunding(capture.Before,plan,true,59,copper,quantity,out var replayed,
                    out var ground)||!replayed.AsSpan().SequenceEqual(after))continue;
                ReconcileFinalBankCaps(capture.Before,after,caps);
                if(!AreValidRetainedMainShopImage(after.AsSpan(0,59),quantity.AsSpan(0,59).ToArray(),
                    copper.AsSpan(0,59).ToArray(),caps.AsSpan(0,59*5))||
                    !AreValidShopBanks(after.AsSpan(59),copper.AsSpan(59))||
                    !AreValidShopBankOwnership(after.AsSpan(59),quantity.AsSpan(59),caps.AsSpan(59*5)))continue;
                var candidate=new SortOverflowPreparation(new(this,connection,capture,after,copper,quantity,caps),ground);
                if(result.Accepted is not null&&!result.Accepted.HasSameAcceptedImage(candidate))
                {result.Ambiguous=true;result.Accepted=null;yield break;}
                if(result.Accepted is null)
                {
                    var witness=new BankBulkResolution();
                    foreach(var step in candidate.Inventory.PrepareConservativeClosedCursorWitnesses(animation,itemTime,witness))
                    {yield return step;if(witness.Ambiguous)break;}
                    if(witness.Ambiguous){result.Ambiguous=true;result.Accepted=null;yield break;}
                }
                result.Accepted=candidate;
            }
        }
    }
}
