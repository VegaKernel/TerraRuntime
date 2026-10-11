using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using Xunit;
namespace TerraRuntime.Tests;
public sealed class CoreCoinValuePrototypeTests { [Fact] public void Generation_owned_value_survives_source_conversion_and_raw_images_never_fund() {
int checks=0;
void Check(bool ok,string label){checks++;Assert.True(ok,label);}
WorldItemDropStateUpdate Drop(short type,short quantity)=>new(400,500,0,0,quantity,0,WorldItemOwnershipMode.None,type,false,0,0);
long Value(RuntimeWorldItemStore store,WorldItemSnapshot snapshot){Check(store.TryGetFundedCoinValue(snapshot,out long value),"exact value");return value;}
foreach(string mode in new[]{"mixed99","mixed1","all100"})
{
 var store=new RuntimeWorldItemStore();
 for(short slot=0;slot<2;slot++)
 {
  short amount=slot==0?(short)99:(short)1;
  bool trusted=mode=="all100"||(mode=="mixed99"?slot==0:slot==1);
  if(trusted){Check(store.TryReserveDrop(Drop(71,amount),out var lease),"reserve trusted");Check(store.TryCommitReservedDrop(lease,out _),"commit trusted");}
  else Check(store.TryUpsert(slot,new WorldItemStateUpdate(400,500,0,0,amount,0,WorldItemOwnershipMode.None,71,false,0,0,255,0,255,0),out _),"raw coin");
 }
 for(short slot=2;slot<400;slot++)Check(store.TryUpsert(slot,new WorldItemStateUpdate(5000,500,0,0,9999,0,WorldItemOwnershipMode.None,1,false,0,0,255,0,255,0),out _),"fill emergency");
 Check(store.TryAllocateSourceDrop(Drop(38,1) with{PositionX=5000},default,out _,out _),"actual emergency source merge");
 WorldItemSnapshot silver=default;
 for(short slot=0;slot<400;slot++)if(store.TryGetActive(slot,out var current)&&current.ItemNetId==72)silver=current;
 Check(silver.Handle.IsAssigned && silver.Stack==1,"100 physical converts");
 long expected=mode=="all100"?100:mode=="mixed99"?99:1;
 Check(Value(store,silver)==expected,"conversion preserves exact copper budget");
 Check(store.TryGetFundedQuantity(silver,out int whole)&&whole==expected/100,"legacy whole quantity floors");
 using(var take=store.CreateAllocationPreview())
 {
  Check(take.TryTakeFundedCoinSource(silver,1,out long transferred)&&transferred==expected,"coin transfers exact value");
  Check(take.TryClaim(),"claim");Check(take.TryAdoptUnpublished(out var publish)&&publish is not null,"adopt");while(publish!.TryPublishNext(out _)){}
 }
 Check(!store.TryGetActive(silver.Handle.Slot,out _),"physical coin taken");
 Check(store.TryApplyDrop(silver.Handle.Slot,Drop(72,1),out silver),"reuse raw");
 Check(Value(store,silver)==0,"reuse value reset");
}
foreach(short type in new short[]{71,72,73,74})
{
 var store=new RuntimeWorldItemStore();var drop=Drop(type,3);
 Check(store.TryAllocateSourceDrop(drop,default,out var item,out _),"trusted coin");
 long unit=type==71?1:type==72?100:type==73?10000:1000000;
 Check(Value(store,item)==3*unit,"trusted exact denomination");
 Check(store.TryGetFundedQuantity(item,out var quantity)&&quantity==3,"legacy trusted quantity");
 Check(store.TryApplyDrop(item.Handle.Slot,drop with{Stack=7},out item),"raw increase");Check(Value(store,item)==3*unit,"no raw mint");
 Check(store.TryApplyDrop(item.Handle.Slot,drop with{Stack=1},out item),"raw decrease");Check(Value(store,item)==unit,"decrease");
 Check(store.TryApplyDrop(item.Handle.Slot,drop,out item),"raw restore");Check(Value(store,item)==unit,"restore no mint");
 using(var take=store.CreateAllocationPreview())
 {Check(take.TryTakeFundedCoinSource(item,2,out var value)&&value==unit,"mixed partial exactbudget");Check(take.TryClaim(),"partial claim");Check(take.TryAdoptUnpublished(out var p)&&p is not null,"partial adopt");while(p!.TryPublishNext(out _)){}}
 Check(store.TryGetActive(item.Handle.Slot,out item)&&item.Stack==1,"partial physicalresidual");Check(Value(store,item)==0,"partial spentvalue");
}

} }
