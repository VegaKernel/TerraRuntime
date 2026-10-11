using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
namespace TerraRuntime.Tests;
internal static class BankObservedImageCertificateChecks { internal static void Run() {
int checks=0;
void Check(bool condition,string label){checks++;if(!condition)throw new InvalidOperationException(label);}
for(int mode=0;mode<17;mode++)
{
 var store=new RuntimePlayerInventoryStore();
 var c=new ConnectionHandle(GameCommandSourceId.FromConnection(93000),new(new(0),new(1)));
 var other=new ConnectionHandle(GameCommandSourceId.FromConnection(93001),new(new(1),new(1)));
 Check(store.TryAttach(c)&&store.TryAttach(other),"attach");
 var old=new RuntimePlayerInventoryItem(new(109),7,new(0),0);var raw=old with { Stack=3 };
 Check(store.TrySet(c,old.ToCommitRequest(c.Player.Slot,10)),"seed physical");
 var q=new short[59];q[10]=4;store.AdoptFundedQuantities(c,q);
 store.AdoptOwnedQuantities(c,[new(10,new(109),7)]);
 var bank=new RuntimePlayerInventoryItem[160];bank[120]=new(new(110),3,new(0),1);
 var bq=new short[160];bq[120]=2;var caps=new short[800];Array.Fill(caps,(short)-1);caps[120*5+1]=3;
 Check(store.TryAdoptShopBanks(c,bank,new long[160],bq,caps),"seed bank");
 Check(store.TryCaptureShopJournal(c,out var capture)&&capture!=null,"capture");
 if(mode==1)Check(store.TrySet(other,new(other.Player.Slot,2,1,0,9,0)),"disjoint before prepare");
 Check(capture!.TryPrepareObservedBatchImage(10,in raw,out var prepared)&&prepared!=null,"prepare");
 RuntimePlayerInventoryStore.ShopJournalCapture.ObservedShopImagePreparation? competing=null;
 if(mode==6)Check(capture.TryPrepareObservedBatchImage(10,in raw,out competing)&&competing!=null,"competing prepare");
 if(mode==15)Check(store.TrySet(other,new(other.Player.Slot,2,1,0,9,0)),"disjoint forbidden gap");
 if(mode!=3)Check(store.TrySet(c,raw.ToCommitRequest(c.Player.Slot,10)),"physical receive");
 if(mode==2)Check(store.TrySet(other,new(other.Player.Slot,2,1,0,9,0)),"disjoint after write");
 if(mode==4)Check(store.TrySet(c,new(c.Player.Slot,11,1,0,2,0)),"second own write");
 if(mode==5){store.Clear(c);Check(store.TryAttach(new(c.Source,new(c.Player.Slot,new(2)))),"replacement generation");}
 // Cache-only static private-layout mutations deliberately keep stamps unchanged:
 // this isolates full-image custody from the serial checks in the omission.
 if(mode==7)Fields.Items(store)[10]=raw with { ItemFlags=1 };
 if(mode==8)Fields.Quantities(store)[10]=2;
 if(mode==9)Fields.Copper(store)[10]=1;
 if(mode==10)Fields.Caps(store)[10*5]=6;
 if(mode==11)Fields.Banks(store)[120]=bank[120] with { ItemFlags=0 };
 if(mode==12)Fields.BankQuantities(store)[120]=1;
 if(mode==13)Fields.BankCopper(store)[120]=1;
 if(mode==14)Fields.BankCaps(store)[120*5+1]=2;
 bool accepted=mode is 0 or 1 or 2 or 6 or 16;
 Check(prepared!.TryAcceptUnpublished()==accepted,"certificate outcome mode="+mode);
 Check(!prepared.TryAcceptUnpublished(),"single-use mode="+mode);
 if(mode==6)Check(!competing!.TryAcceptUnpublished(),"only first accepted certificate");
 if(accepted){Check(capture.IsCurrent,"accepted current");Check(capture.Before[10]==old&&capture.Quantity[10]==4,"original economic facts");Check(store.CaptureFundedQuantities(c)[10]==3,"raw min quantity");}
 if(mode==16){Action observer=()=>{Check(store.TrySet(c,new(c.Player.Slot,11,1,0,2,0)),"observer reentrant owner write");Check(!capture.IsCurrent,"observer invalidates accepted image");Check(!capture.TryPrepareObservedBatchImage(10,in raw,out _),"observer cannot prepare stale continuation");};observer();}
 else if(!accepted) Check(!capture.IsCurrent || mode is 3 or 15,"rejected currentness");
}

}
static class Fields
{
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="items")] internal static extern ref RuntimePlayerInventoryItem[] Items(RuntimePlayerInventoryStore owner);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="fundedQuantities")] internal static extern ref short[] Quantities(RuntimePlayerInventoryStore owner);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="coinFundedValues")] internal static extern ref long[] Copper(RuntimePlayerInventoryStore owner);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="ownedQuantityCaps")] internal static extern ref short[] Caps(RuntimePlayerInventoryStore owner);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="shopBankItems")] internal static extern ref RuntimePlayerInventoryItem[] Banks(RuntimePlayerInventoryStore owner);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="shopBankFundedQuantities")] internal static extern ref short[] BankQuantities(RuntimePlayerInventoryStore owner);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="shopBankFundedCopper")] internal static extern ref long[] BankCopper(RuntimePlayerInventoryStore owner);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="shopBankQuantityCaps")] internal static extern ref short[] BankCaps(RuntimePlayerInventoryStore owner);
}
}
