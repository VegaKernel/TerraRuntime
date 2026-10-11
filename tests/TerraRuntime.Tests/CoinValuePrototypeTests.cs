using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Application;
using TerraRuntime.Core.Worlds;
using Xunit;
namespace TerraRuntime.Tests;
public sealed class CoinValuePrototypeTests
{
 static RuntimePlayerInventoryStore Inventory(OwnedHumanPickupAuthority1458Tests.Fixture f)=>(RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Players)!;
 static long Value(OwnedHumanPickupAuthority1458Tests.Fixture f,int slot){Assert.True(Inventory(f).TryGetFundedCoinValue(f.Connection,slot,out long value));return value;}
 [Fact]
 public async Task Mixed_conversion_transfer_and_full_drop_preserve_exact_value()
 {
  using(var unknown=new OwnedHumanPickupAuthority1458Tests.Fixture())
  {
   Assert.True(unknown.Items.TryAllocate(new WorldItemStateUpdate(1600,1606,0,0,1,0,WorldItemOwnershipMode.None,72,false,0,0,0,15,1,0),out var raw));
   unknown.State.Tick();Assert.True(unknown.Items.TryGetActive(raw.Handle.Slot,out var current));Assert.True(unknown.Items.TryGetFundedCoinValue(current,out long value));Assert.Equal(0,value);
   Assert.All(unknown.ItemImage(), i=>Assert.True(i.IsEmpty));Assert.Equal(0,Inventory(unknown).CaptureFundedCoinValues(unknown.Connection).Sum());
   var clientAllocate=new TaskCompletionSource<WorldItemSnapshot?>();unknown.State.Apply(new WorldItemAllocateRuntimeCommand(unknown.Connection,new WorldItemDropStateUpdate(1600,1606,0,0,1,0,WorldItemOwnershipMode.None,72,false,0,0),clientAllocate));
   var clientGround=await clientAllocate.Task;Assert.NotNull(clientGround);Assert.True(unknown.Items.TryGetFundedCoinValue(clientGround!.Value,out long clientValue));Assert.Equal(0,clientValue);
   unknown.State.Tick();Assert.All(unknown.ItemImage(),i=>Assert.True(i.IsEmpty));

  }
  {
   var mixedWorld=new RuntimeWorldItemStore();
   Assert.True(mixedWorld.TryReserveDrop(new WorldItemDropStateUpdate(1600,1606,0,0,99,0,WorldItemOwnershipMode.None,71,false,0,0),out var lease));Assert.True(mixedWorld.TryCommitReservedDrop(lease,out _));
   Assert.True(mixedWorld.TryUpsert(1,new WorldItemStateUpdate(1600,1606,0,0,1,0,WorldItemOwnershipMode.None,71,false,0,0,255,0,255,0),out _));
   for(short slot=2;slot<400;slot++)Assert.True(mixedWorld.TryUpsert(slot,new WorldItemStateUpdate(5000,1606,0,0,9999,0,WorldItemOwnershipMode.None,1,false,0,0,255,0,255,0),out _));
   Assert.True(mixedWorld.TryAllocateSourceDrop(new WorldItemDropStateUpdate(5000,1606,0,0,1,0,WorldItemOwnershipMode.None,38,false,0,0),default,out _,out _));
   var images=new WorldItemSnapshot[400];int count=mixedWorld.CopyActive(images);var silver=Assert.Single(images.Take(count),x=>x.ItemNetId==72);
   Assert.True(mixedWorld.TryGetFundedCoinValue(silver,out long beforeValue));Assert.Equal(99,beforeValue);
   Assert.True(mixedWorld.TryApplyOwner(silver.Handle.Slot,new(0,15,1,0,1600,1606),out silver));
   using var f=new OwnedHumanPickupAuthority1458Tests.Fixture(providedItems:mixedWorld);
   f.State.Tick();Assert.False(mixedWorld.TryGetActive(silver.Handle.Slot,out _));Assert.Equal(72,f.ItemImage()[53].ItemType.Value);Assert.Equal(1,f.ItemImage()[53].Stack);Assert.Equal(99,Value(f,53));
  }
  using(var f=new OwnedHumanPickupAuthority1458Tests.Fixture())
  {
   f.Set(50,71,99);f.Drop(71,1);f.State.Tick();
   Assert.Equal(72,f.ItemImage()[50].ItemType.Value);Assert.Equal(1,f.ItemImage()[50].Stack);Assert.Equal(1,Value(f,50));
   var detach=new TaskCompletionSource<RuntimePlayerTransferState?>();f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection,detach));var dto=await detach.Task;Assert.NotNull(dto);Assert.Equal(1,dto!.FundedInventoryCoinValues[50]);
   f.Connection=new(GameCommandSourceId.FromConnection(98902),new(f.Connection.Player.Slot,new(f.Connection.Player.Generation.Value+1)));
   var attach=new TaskCompletionSource<bool>();f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection,dto,100,103,true,false,attach));Assert.True(await attach.Task);Assert.Equal(1,Value(f,50));
   f.Set(50,72,2);Assert.Equal(1,Value(f,50));f.Set(50,73,1);Assert.Equal(0,Value(f,50));f.Set(50,72,1);Assert.Equal(0,Value(f,50));
  }
  using(var f=new OwnedHumanPickupAuthority1458Tests.Fixture())
  {
   f.Drop(71,99);f.State.Tick();Assert.Equal(99,Value(f,53));f.Drop(71,1);f.State.Tick();Assert.Equal(72,f.ItemImage()[53].ItemType.Value);Assert.Equal(100,Value(f,53));
  }
  foreach(short type in new short[]{71,72,73,74})
  {
   using var f=new OwnedHumanPickupAuthority1458Tests.Fixture();
   // Occupied coin slots direct the real storage journal to the selected ordinary slot.
   for(short slot=50;slot<54;slot++)f.Set(slot,3,9999);
   f.Drop(type,3);f.State.Tick();Assert.Equal(type,f.ItemImage()[49].ItemType.Value);
   f.Set(0,type,3); // This reported identity has no funding; it cannot supply an owned drop.
   Assert.Equal(0,Value(f,0));
   long total=3*(type==71?1L:type==72?100L:type==73?10000L:1000000L);
   Assert.Equal(total,Value(f,49));
   f.Set(0,0,0);f.Set(49,0,0);f.Set(58,type,3);Assert.Equal(total,Value(f,58));
   f.Set(0,type,3);f.Set(58,0,0);Assert.Equal(total,Value(f,0));
   bool observed=false;f.WorldEvents.Callback=()=>{observed=true;Assert.True(f.ItemImage()[0].IsEmpty);Assert.Equal(0,Value(f,0));};
   var state=f.Member.CaptureSnapshot();var body=new WorldItemDropStateUpdate(state.PositionX+2,state.PositionY+13,4,-2,3,0,WorldItemOwnershipMode.GrabDelayForLocalPlayer,type,false,0,100);
   var completion=new TaskCompletionSource<WorldItemSnapshot?>();f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection,body,completion));var ground=await completion.Task;Assert.NotNull(ground);Assert.True(observed);Assert.True(f.Items.TryGetFundedCoinValue(ground!.Value,out long groundValue));Assert.Equal(total,groundValue);

  }
  using(var f=new OwnedHumanPickupAuthority1458Tests.Fixture())
  {
   for(short slot=50;slot<54;slot++)f.Set(slot,3,9999);
   f.Set(0,71,99);f.Drop(71,1);f.State.Tick();Assert.Equal(72,f.ItemImage()[0].ItemType.Value);Assert.Equal(1,Value(f,0));
   var state=f.Member.CaptureSnapshot();var body=new WorldItemDropStateUpdate(state.PositionX+2,state.PositionY+13,4,-2,1,0,WorldItemOwnershipMode.GrabDelayForLocalPlayer,72,false,0,100);
   var completion=new TaskCompletionSource<WorldItemSnapshot?>();f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection,body,completion));Assert.Null(await completion.Task);Assert.Equal(1,Value(f,0));Assert.Equal(1,f.ItemImage()[0].Stack);Assert.Equal(0,f.Items.ActiveCount);
  }

 }
 [Fact]
 public async Task Original_coin_cursor_and_drop_images_match_value_owned_routes()
 {
  using var input=typeof(CoinValuePrototypeTests).Assembly.GetManifestResourceStream("CoinValueOriginal1458");Assert.NotNull(input);
  using var gz=new GZipStream(input,CompressionMode.Decompress);using var doc=JsonDocument.Parse(gz);
  int cursorCompared=0,dropCompared=0;
  foreach(bool windows in new[]{false,true})
  {
   foreach(var row in doc.RootElement.GetProperty("cursor").GetProperty("rows").EnumerateArray())
   {
    var before=row.GetProperty("before");var after=row.GetProperty("after");
    var slot=Tuple(before.GetProperty("inventory")[2]);var mouse=Tuple(before.GetProperty("mouse"));
    var wantedSlot=Tuple(after.GetProperty("inventory")[2]);var wantedMouse=Tuple(after.GetProperty("mouse"));
    bool accepted=row.GetProperty("spec").GetProperty("Operation").GetString()=="left"
     ? VanillaInventoryCursorMove1458.TryPlanLeftClick(slot,mouse,out var actualSlot,out var actualMouse)
     : VanillaInventoryCursorMove1458.TryPlanSingleToCursor(slot,mouse,out actualSlot,out actualMouse);
    Assert.True(accepted);Assert.Equal(wantedSlot,actualSlot);Assert.Equal(wantedMouse,actualMouse);
    using var f=new OwnedHumanPickupAuthority1458Tests.Fixture(windows);
    for(short coinSlot=50;coinSlot<54;coinSlot++)f.Set(coinSlot,3,9999);
    short seedType=checked((short)(slot.Type!=0?slot.Type:mouse.Type));
    short seedAmount=checked((short)Math.Min(3,slot.Type!=0?slot.Stack:mouse.Stack));
    f.Drop(seedType,seedAmount);f.State.Tick();f.Set(49,0,0);f.Set(58,seedType,seedAmount);
    if(slot.Type!=0){f.Set(2,seedType,seedAmount);f.Set(58,0,0);}
    f.Set(2,(short)slot.Type,(short)slot.Stack,0,slot.Favorite?(byte)1:(byte)0);
    f.Set(58,(short)mouse.Type,(short)mouse.Stack,0,mouse.Favorite?(byte)1:(byte)0);
    long budget=Value(f,2)+Value(f,58);Assert.True(budget>0);
    f.Set(2,(short)wantedSlot.Type,(short)wantedSlot.Stack,0,wantedSlot.Favorite?(byte)1:(byte)0);
    f.Set(58,(short)wantedMouse.Type,(short)wantedMouse.Stack,0,wantedMouse.Favorite?(byte)1:(byte)0);
    Assert.Equal(wantedSlot.Type,f.ItemImage()[2].ItemType.Value);Assert.Equal(wantedSlot.Stack,f.ItemImage()[2].Stack);Assert.Equal(wantedSlot.Favorite,f.ItemImage()[2].ItemFlags==1);
    Assert.Equal(wantedMouse.Type,f.ItemImage()[58].ItemType.Value);Assert.Equal(wantedMouse.Stack,f.ItemImage()[58].Stack);Assert.Equal(wantedMouse.Favorite,f.ItemImage()[58].ItemFlags==1);
    Assert.Equal(budget,Value(f,2)+Value(f,58));cursorCompared++;
   }
   foreach(var row in doc.RootElement.GetProperty("drop").GetProperty("rows").EnumerateArray())
   {
    using var f=new OwnedHumanPickupAuthority1458Tests.Fixture(windows);
    short type=(short)row.GetProperty("id").GetInt32();short amount=(short)row.GetProperty("before")[row.GetProperty("from").GetInt32()].GetProperty("stack").GetInt32();
    bool mouse=row.GetProperty("profile").GetString()=="mouse58";
    for(short coinSlot=50;coinSlot<54;coinSlot++)f.Set(coinSlot,3,9999);
    if(amount==100 && type!=74)
    {
     f.Drop(type,1);f.State.Tick();f.Set(49,0,0);f.Set(58,type,1);
     f.Drop(type,99);f.State.Tick();f.Set(49,type,100);f.Set(58,0,0);
    }
    else { f.Drop(type,amount);f.State.Tick(); }
    f.Set(49,0,0);f.Set(58,type,amount);
    if(!mouse){f.Set(0,type,amount);f.Set(58,0,0);}
    int selected=mouse?58:0;Assert.Equal(amount,f.ItemImage()[selected].Stack);
    long unit=type==71?1L:type==72?100L:type==73?10000L:1000000L;Assert.Equal(amount*unit,Value(f,selected));
    var ground=row.GetProperty("ground");var position=ground.GetProperty("position");var velocity=ground.GetProperty("velocity");
    var body=new WorldItemDropStateUpdate(position.GetProperty("x").GetSingle()+1200,position.GetProperty("y").GetSingle()+1206,
     velocity.GetProperty("x").GetSingle(),velocity.GetProperty("y").GetSingle(),amount,0,WorldItemOwnershipMode.GrabDelayForLocalPlayer,type,false,0,100);
    var completion=new TaskCompletionSource<WorldItemSnapshot?>();f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection,body,completion));
    var result=await completion.Task;Assert.NotNull(result);Assert.True(f.ItemImage()[selected].IsEmpty);Assert.Equal(0,Value(f,selected));
    Assert.True(f.Items.TryGetFundedCoinValue(result!.Value,out long value));Assert.Equal(amount*unit,value);Assert.Equal(row.GetProperty("afterClocks").GetProperty("animation").GetInt32(),f.Member.ItemAnimation);dropCompared++;
   }
  }
  Assert.Equal(64,cursorCompared);Assert.Equal(32,dropCompared);
 }
 static WorldItemPickupSlot1458 Tuple(JsonElement e)=>e.GetProperty("type").GetInt32()==0?default:new(e.GetProperty("type").GetInt32(),e.GetProperty("stack").GetInt32(),e.GetProperty("prefix").GetInt32(),e.GetProperty("favorite").GetBoolean());

}
