using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
namespace TerraRuntime.Tests;
internal static class BankRetainedMainCaptureChecks { internal static void Run() {
var cases=new List<object>();int checks=0,failures=0;
void Run(string name,int type,int stack,short? cap,bool expected,Action<RuntimePlayerInventoryStore>? corruption=null,short slot=10,int? historicalType=null){
 var owner=new RuntimePlayerInventoryStore();var c=new ConnectionHandle(GameCommandSourceId.FromConnection(96000),new(new(0),new(1)));
 if(!owner.TryAttach(c)||!owner.TrySet(c,new(c.Player.Slot,slot,(short)stack,0,(short)type,0)))throw new InvalidOperationException("setup");
 if(cap.HasValue)owner.AdoptOwnedQuantities(c,[new(slot,new(historicalType??type),cap.Value)]);
 corruption?.Invoke(owner);
 bool accepted=owner.TryCaptureShopJournal(c,out var capture);checks++;if(accepted!=expected)failures++;
 cases.Add(new{name,expected,accepted,assertionPassed=accepted==expected,hasCapture=capture is not null});
}
int[] lanes=[109,110,189,500,2209];
foreach(int type in lanes){
 Run(type+"-body3-cap0",type,3,0,false);
 Run(type+"-body3-cap2",type,3,2,false);
 Run(type+"-body3-cap3-equality",type,3,3,true);
 Run(type+"-empty-historical-cap0",0,0,0,true,historicalType:type);
 Run(type+"-other-historical-lane0",type,3,0,true,historicalType:lanes[(Array.IndexOf(lanes,type)+1)%5]);
}
Run("mouse58-body3-cap0",109,3,0,false,slot:58);
Run("malformed-cap-negative",109,3,null,false,o=>Fields.Caps(o)[10*5]=-2);
Run("malformed-funded-over-stack",109,3,null,false,o=>Fields.Quantity(o)[10]=4);
Run("malformed-funded-negative",109,3,null,false,o=>Fields.Quantity(o)[10]=-1);
Run("coin-noncoin-quantity",71,3,null,false,o=>Fields.Quantity(o)[10]=1);
Run("coin-copper-over-physical",71,3,null,false,o=>Fields.Copper(o)[10]=4);
Run("coin-copper-negative",71,3,null,false,o=>Fields.Copper(o)[10]=-1);
Run("noncanonical-zero-body",109,3,null,false,o=>Fields.Image(o)[10]=new(new(109),0,new(0),0));
Run("noncanonical-negative-stack",109,3,null,false,o=>Fields.Image(o)[10]=new(new(109),-1,new(0),0));
Run("invalid-body-flags",109,3,null,false,o=>Fields.Image(o)[10]=new(new(109),3,new(0),2));
foreach(int coin in new[]{71,72,73,74})Run(coin+"-raw-unconfirmed-canonical",coin,3,null,true);

if(failures!=0)throw new InvalidOperationException("capture boundary assertions failed="+failures);
}
static class Fields{
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="items")]internal static extern ref RuntimePlayerInventoryItem[] Image(RuntimePlayerInventoryStore s);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="ownedQuantityCaps")]internal static extern ref short[] Caps(RuntimePlayerInventoryStore s);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="fundedQuantities")]internal static extern ref short[] Quantity(RuntimePlayerInventoryStore s);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="coinFundedValues")]internal static extern ref long[] Copper(RuntimePlayerInventoryStore s);
}
}
