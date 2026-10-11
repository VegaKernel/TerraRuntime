using System.Reflection;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using Xunit;
namespace TerraRuntime.Tests;
public sealed class CloseAndJointChecks
{
 static RuntimePlayerInventoryStore Inventory(OwnedHumanPickupAuthority1458Tests.Fixture f) => (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Players)!;
 static long Value(OwnedHumanPickupAuthority1458Tests.Fixture f,int slot){Assert.True(Inventory(f).TryGetFundedCoinValue(f.Connection,slot,out var value));return value;}
 [Fact]
 public async Task Joint_fractional_cursor_transfer_and_discard_never_use_floor_quantity()
 {
  foreach(bool complete in new[]{true,false})
  {
   using var f=new OwnedHumanPickupAuthority1458Tests.Fixture();
   for(short s=50;s<54;s++) f.Set(s,3,9999);
   f.Set(0,71,99);f.Drop(71,1);f.State.Tick();Assert.Equal(72,f.ItemImage()[0].ItemType.Value);Assert.Equal(1,Value(f,0));
   f.Set(0,0,0);
   if(complete){f.Set(58,72,1);Assert.Equal(1,Value(f,58));}
   var detach=new TaskCompletionSource<RuntimePlayerTransferState?>();f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection,detach));var dto=await detach.Task;Assert.NotNull(dto);
   Assert.Equal(complete?1:0,dto!.FundedInventoryCoinValues.Sum());
   if(complete){Assert.Equal(72,dto.Inventory[0].ItemType.Value);Assert.Equal(1,dto.FundedInventoryCoinValues[0]);Assert.True(dto.Inventory[58].IsEmpty);}
   f.Connection=new(GameCommandSourceId.FromConnection(98917),new(f.Connection.Player.Slot,new(f.Connection.Player.Generation.Value+1)));
   var attach=new TaskCompletionSource<bool>();f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection,dto,100,103,true,false,attach));Assert.True(await attach.Task);
   Assert.Equal(complete?1:0,Inventory(f).CaptureFundedCoinValues(f.Connection).Sum());
   if(complete){f.Set(0,72,2);Assert.Equal(1,Value(f,0));f.Set(0,72,1);var state=f.Member.CaptureSnapshot();var body=new WorldItemDropStateUpdate(state.PositionX+2,state.PositionY+13,4,-2,1,0,WorldItemOwnershipMode.GrabDelayForLocalPlayer,72,false,0,100);var c=new TaskCompletionSource<WorldItemSnapshot?>();f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection,body,c));Assert.Null(await c.Task);Assert.Equal(1,Value(f,0));f.Set(0,73,1);Assert.Equal(0,Value(f,0));f.Set(0,72,1);Assert.Equal(0,Value(f,0));}
  }
 }

 static RuntimePlayerInventoryItem Read(JsonElement v)=>v.GetProperty("type").GetInt32()==0?default:new(new(v.GetProperty("type").GetInt32()),v.GetProperty("stack").GetInt16(),new(v.GetProperty("prefix").GetInt32()),v.GetProperty("favorite").GetBoolean()?(byte)1:(byte)0);
 static JsonDocument Source(string name){using var input=typeof(CloseAndJointChecks).Assembly.GetManifestResourceStream(name);Assert.NotNull(input);using var gz=new GZipStream(input!,CompressionMode.Decompress);return JsonDocument.Parse(gz);}
 static void Seed(OwnedHumanPickupAuthority1458Tests.Fixture f,JsonElement row)
 {
  OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f,new(new(row.GetProperty("id").GetInt32()),20,new(0),0),58);
  short s=0;foreach(var v in row.GetProperty("before").EnumerateArray()){var i=Read(v);if(s!=58)f.Set(s,checked((short)i.ItemType.Value),i.Stack,checked((byte)i.Prefix.Value),i.ItemFlags);s++;}
  var mouse=Read(row.GetProperty("before")[58]);f.Set(58,checked((short)mouse.ItemType.Value),mouse.Stack,checked((byte)mouse.Prefix.Value),mouse.ItemFlags);
  Assert.Equal(row.GetProperty("before").EnumerateArray().Select(Read).ToArray(),f.ItemImage());f.Events.Seen.Clear();
 }
 [Fact]
 public async Task Original_closed_cursor_images_adopt_return_residual_and_server_offers_once()
 {
  using var old=Source("InventoryClose1458");using var more=Source("InventoryCloseNonfavorite1458");int compared=0;
  foreach(bool windows in new[]{false,true})
  foreach(var row in old.RootElement.GetProperty(windows?"windowsClr4X86":"linux").GetProperty("rows").EnumerateArray().Concat(more.RootElement.GetProperty("rows").EnumerateArray()))
  {
   using var f=new OwnedHumanPickupAuthority1458Tests.Fixture(windows);Seed(f,row);var expected=row.GetProperty("after").EnumerateArray().Select(Read).ToArray();var random=((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;var before=random.Clone();var after=before.Clone();bool ground=row.GetProperty("ground").GetProperty("active").GetBoolean();int residual=ground?row.GetProperty("ground").GetProperty("item").GetProperty("stack").GetInt32():0;
   if(ground){_=after.Next(-30,31);_=after.Next(-40,-15);}
   int observations=0;Action observe=()=>{observations++;Assert.Equal(expected,f.ItemImage());Assert.True(random.HasSameState(after));Assert.Equal(20,Inventory(f).CaptureFundedQuantities(f.Connection).Sum(i=>(int)i)+residual);};f.Events.Callback=observe;f.WorldEvents.Callback=observe;
   if(ground){var g=row.GetProperty("ground");var item=g.GetProperty("item");var position=g.GetProperty("position");var velocity=g.GetProperty("velocity");var proposal=new WorldItemDropStateUpdate(position.GetProperty("x").GetSingle()+1200,position.GetProperty("y").GetSingle()+1206,velocity.GetProperty("x").GetSingle(),velocity.GetProperty("y").GetSingle(),item.GetProperty("stack").GetInt16(),item.GetProperty("prefix").GetByte(),WorldItemOwnershipMode.ReserveForLocalPlayer,item.GetProperty("type").GetInt16(),false,0,100);Assert.True(f.Players.TryPrepareCursorReturn(f.Connection,true,out _,requireFavorite:false,animateResidual:false),$"return prep id={row.GetProperty("id")} profile={row.GetProperty("profile")}");Assert.True(f.Players.TryPrepareInventoryClose(f.Connection,proposal,out _),$"close prep id={row.GetProperty("id")} profile={row.GetProperty("profile")}");var result=new TaskCompletionSource<WorldItemSnapshot?>();f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection,proposal,result));Assert.True((await result.Task).HasValue,$"close rejected id={row.GetProperty("id")} profile={row.GetProperty("profile")} windows={windows}");var actual=(await result.Task)!.Value;Assert.Equal(proposal.PositionX,actual.PositionX);Assert.Equal(proposal.PositionY,actual.PositionY);Assert.Equal(proposal.VelocityX,actual.VelocityX);Assert.Equal(proposal.VelocityY,actual.VelocityY);Assert.Equal(residual,actual.Stack);Assert.True(f.Items.TryGetFundedQuantity(actual,out int quantity));Assert.Equal(residual,quantity);Assert.Equal(f.Connection.Player.Slot.Value,actual.OwnerPlayerId);Assert.True(f.Items.TryGetActive(actual.Handle.Slot,out var reserved));Assert.Equal(f.Connection.Player.Slot.Value,reserved.OwnerPlayerId);}
   else{for(short s=0;s<59;s++)if(f.ItemImage()[s]!=expected[s]){var i=expected[s];f.Set(s,checked((short)i.ItemType.Value),i.Stack,checked((byte)i.Prefix.Value),i.ItemFlags);}}
   Assert.Equal(expected,f.ItemImage());Assert.True(random.HasSameState(after));Assert.Equal(0,f.Member.ItemAnimation);Assert.Equal(0,f.Member.ItemPhase!.Value.Selected.ItemTime);Assert.True(observations>0);compared++;
  }
  Assert.Equal(144,compared);
 }
 [Fact]
 public async Task Close_publication_drains_current_owner_and_inventory_after_first_observer_throws()
 {
  using var source = Source("InventoryClose1458");
  var row = source.RootElement.GetProperty("linux").GetProperty("rows").EnumerateArray()
   .First(r => r.GetProperty("id").GetInt32() == 110 && r.GetProperty("profile").GetString() == "favorite_mouse58_partial_merge");
  using var f = new OwnedHumanPickupAuthority1458Tests.Fixture();
  Seed(f, row);
  var expected = row.GetProperty("after").EnumerateArray().Select(Read).ToArray();
  var ground = row.GetProperty("ground");
  var item = ground.GetProperty("item");
  var position = ground.GetProperty("position");
  var velocity = ground.GetProperty("velocity");
  var proposal = new WorldItemDropStateUpdate(position.GetProperty("x").GetSingle() + 1200,
   position.GetProperty("y").GetSingle() + 1206, velocity.GetProperty("x").GetSingle(),
   velocity.GetProperty("y").GetSingle(), item.GetProperty("stack").GetInt16(),
   item.GetProperty("prefix").GetByte(), WorldItemOwnershipMode.ReserveForLocalPlayer,
   item.GetProperty("type").GetInt16(), false, 0, 100);
  using (var fullFit = new OwnedHumanPickupAuthority1458Tests.Fixture())
  {
   // Authority-policy regression over the independently verified GetItem storage
   // owner: a nonfavorite cursor return fills two destinations, not one LeftClick.
   Seed(fullFit, row);
   for (short slot = 0; slot < 50; slot++) fullFit.Set(slot, 3, 9999);
   fullFit.Set(2, 110, 9990); fullFit.Set(3, 110, 9988);
   fullFit.Set(58, 110, 20);
   Assert.True(fullFit.Players.TryPrepareCursorReturn(fullFit.Connection, false, out var fullReturn, requireFavorite:false, animateResidual:false), "nonfavorite multimerge preparation");
   Assert.Equal(new short[] { 2, 3, 58 }, fullReturn!.ChangedSlots);
   fullFit.Set(2, 110, 9999); fullFit.Set(3, 110, 9999);
   fullFit.Set(58, 0, 0);
   Assert.Equal(9999, fullFit.ItemImage()[2].Stack);
   Assert.Equal(9999, fullFit.ItemImage()[3].Stack);
   Assert.True(fullFit.ItemImage()[58].IsEmpty);
   Assert.Equal(20, Inventory(fullFit).CaptureFundedQuantities(fullFit.Connection).Sum(value => (int)value));
  }
  using (var offLattice = new OwnedHumanPickupAuthority1458Tests.Fixture())
  {
   Seed(offLattice, row);
   var beforeItems = offLattice.ItemImage();
   var randomBefore = ((SystemWorldItemSpawnRandom)offLattice.Runtime.WorldItems.SpawnRandom).SourceRandom.Clone();
   var refused = new TaskCompletionSource<WorldItemSnapshot?>();
   offLattice.State.Apply(new WorldItemAllocateRuntimeCommand(offLattice.Connection,
    proposal with { VelocityX = proposal.VelocityX + 0.001f }, refused));
   Assert.Null(await refused.Task); Assert.Equal(beforeItems, offLattice.ItemImage());
   Assert.True(((SystemWorldItemSpawnRandom)offLattice.Runtime.WorldItems.SpawnRandom).SourceRandom.HasSameState(randomBefore));
  }
  foreach (short budget in new short[] { 12, 13, 20 })
  {
   using var mixed = new OwnedHumanPickupAuthority1458Tests.Fixture();
   Seed(mixed, row);
   mixed.Set(58, 110, budget, flags: 1);
   mixed.Set(58, 110, 20, flags: 1);
   var beforeItems = mixed.ItemImage();
   var mixedRandom = ((SystemWorldItemSpawnRandom)mixed.Runtime.WorldItems.SpawnRandom).SourceRandom;
   var beforeRandom = mixedRandom.Clone();
   var completion = new TaskCompletionSource<WorldItemSnapshot?>();
   mixed.State.Apply(new WorldItemAllocateRuntimeCommand(mixed.Connection, proposal, completion));
   var created = await completion.Task;
   if (budget == 12)
   {
    Assert.Null(created); Assert.Equal(beforeItems, mixed.ItemImage());
    Assert.True(mixedRandom.HasSameState(beforeRandom));
   }
   else
   {
    Assert.NotNull(created); Assert.Equal(expected, mixed.ItemImage());
    Assert.True(mixed.Items.TryGetFundedQuantity(created.Value, out int fundedResidual));
    Assert.Equal(13, fundedResidual);
    Assert.Equal(budget - 13, Inventory(mixed).CaptureFundedQuantities(mixed.Connection).Sum(value => (int)value));
   }
  }
  using (var stale = new OwnedHumanPickupAuthority1458Tests.Fixture())
  {
   Seed(stale, row);
   var store = new RuntimeWorldItemStore();
   store.AttachOwnerFactsProvider(new OwnedHumanPickupAuthority1458Tests.LateCurrentnessProvider(() => stale.Set(2, 9, 1)));
   var spawnRandom = new SystemWorldItemSpawnRandom(0);
   var beforeRandom = spawnRandom.SourceRandom.Clone();
   var authority = new WorldItemAuthority(stale.Players, store, spawnRandom, null);
   var completion = new TaskCompletionSource<WorldItemSnapshot?>();
   var exception = Record.Exception(() => authority.TryApply(new WorldItemAllocateRuntimeCommand(stale.Connection, proposal, completion)));
   Assert.Null(exception); // Refusal must not debit ground and then throw on the inventory tail.
   Assert.Null(await completion.Task); Assert.Equal(0, store.ActiveCount);
   Assert.Equal(20, stale.ItemImage()[58].Stack);
   Assert.True(spawnRandom.SourceRandom.HasSameState(beforeRandom));
  }
  var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
  var after = random.Clone(); _ = after.Next(-30,31); _ = after.Next(-40,-15);
  var kinds = new List<WorldItemStateCommitKind>();
  f.WorldEvents.Callback = () =>
  {
   kinds.Add(f.WorldEvents.LastKind);
   Assert.Equal(expected, f.ItemImage());
   Assert.True(random.HasSameState(after));
   if (kinds.Count == 1) throw new InvalidOperationException("first close observer");
  };
  var result = new TaskCompletionSource<WorldItemSnapshot?>();
  var error = Assert.Throws<InvalidOperationException>(() => f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, proposal, result)));
  Assert.Equal("first close observer", error.Message);
  Assert.Equal(2, kinds.Count); // Source21 and source22 share a committed allocation step.
  Assert.NotEmpty(f.Events.Seen); // The independent inventory journal still publishes.
  Assert.Equal(expected, f.ItemImage());
 }
}
