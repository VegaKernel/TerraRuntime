using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
namespace TerraRuntime.Tests;
internal static class BankGlobalWorkAuthorityChecks{
internal static int Run(){
int checks=0;
void Check(bool ok,string label){checks++;if(!ok)throw new InvalidOperationException(label);}
foreach(bool retire in new[]{false,true}){
var players=new PlayerAuthority(null,new WorldTileStore(new WorldDimensions(400,300)));
players.SetPlayerUpdateRandom(new(0));players.SetPlayerUpdateWorldFacts(new(400,300,80,false,false));players.BindReceiveEquipmentRandom(new(0),false,new(false,false,false));
var membership=Fields.Members(players);var inventory=Fields.Items(players);
var connections=new ConnectionHandle[3];
for(byte player=0;player<3;player++){
 var c=new ConnectionHandle(GameCommandSourceId.FromConnection(95000+player),new(new(player),new(1)));connections[player]=c;
 membership.Commit(new RuntimePlayerMember{Connection=c,Slot=c.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400});Check(inventory.TryAttach(c),"attach");
 if(player==2)continue;
 int[] types=[3,9,23,109,110,189,500,2209];var q=new short[59];
 for(short slot=0;slot<58;slot++){int type=types[slot%types.Length];Check(inventory.TrySet(c,new(c.Player.Slot,slot,100,0,(short)type,0)),"source-shaped main seed");q[slot]=100;}
 inventory.AdoptFundedQuantities(c,q);
 var banks=new RuntimePlayerInventoryItem[160];var bankq=new short[160];var caps=new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount];Array.Fill(caps,(short)-1);
 for(int slot=0;slot<160;slot++){banks[slot]=new(new(types[slot%types.Length]),100,new(0),0);bankq[slot]=100;}
 Check(inventory.TryAdoptShopBanks(c,banks,new long[160],bankq,caps),"source-shaped full banks");
}
var cpu=Process.GetCurrentProcess();long alloc=GC.GetAllocatedBytesForCurrentThread();TimeSpan startCpu=cpu.TotalProcessorTime;var stopwatch=Stopwatch.StartNew();
foreach(var c in connections[..2]){
 Check(players.TryApply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,10,99,0,23,0))),"first physical debit");
 int[] types=[3,9,23,109,110,189,500,2209];
 for(int offset=0;offset<160;offset++){short slot=(short)(offset<40?99+offset:offset<80?299+offset-40:offset<120?500+offset-80:700+offset-120);Check(players.TryApply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,slot,99,0,(short)types[offset%8],0))),"nonmatching bank physical image");}
}
var ingress=new{wallMilliseconds=stopwatch.Elapsed.TotalMilliseconds,cpuMilliseconds=(cpu.TotalProcessorTime-startCpu).TotalMilliseconds,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-alloc};
var beforeSeal=connections[..2].Select(c=>{membership.TryGet(c,out var m);return new{player=c.Player.Slot.Value,hasReceipt=m.CursorMove is not null,hasJournal=m.CursorMove?.Shop is not null,lastIndex=m.CursorMove?.Shop?.LastIndex,seen=m.CursorMove?.Shop?.Seen.Count(x=>x),captureCurrent=m.CursorMove?.Shop?.Capture.IsCurrent,invalid=m.CursorMove?.Shop?.Invalid,input=m.ProjectileUseInputRevision,capturedInput=m.CursorMove?.Shop?.ObservedInputRevision};}).ToArray();
alloc=GC.GetAllocatedBytesForCurrentThread();startCpu=cpu.TotalProcessorTime;stopwatch.Restart();
foreach(var c in connections[..2])Check(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(c)),"actual delimiter138");
var sealing=new{wallMilliseconds=stopwatch.Elapsed.TotalMilliseconds,cpuMilliseconds=(cpu.TotalProcessorTime-startCpu).TotalMilliseconds,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-alloc};
bool cooperative=true;
if(cooperative)Check(connections[..2].All(c=>!players.CanApplyShopSource(c.Source)),"138 seals both without synchronous completion");
if(retire)Check(players.TryApply(new PlayerHealthRuntimeCommand(connections[0],new(connections[0].Player.Slot,0,400))),"actual death invalidates pending receipt");
var ticks=new List<object>();int tick=0;
while(connections[..2].Any(c=>!players.CanApplyShopSource(c.Source))){
 Check(tick++<5000,"finite retirement guard");alloc=GC.GetAllocatedBytesForCurrentThread();startCpu=cpu.TotalProcessorTime;stopwatch.Restart();players.TickShopJournalWork();
 Check(players.LastShopWorkSteps<=64,"global64 across both players");
 if(cooperative&&tick==1&&!retire)Check(players.LastShopWorkSteps==64,"first saturated shared tick is exactly64");
 ticks.Add(new{tick,steps=players.LastShopWorkSteps,wallMilliseconds=stopwatch.Elapsed.TotalMilliseconds,cpuMilliseconds=(cpu.TotalProcessorTime-startCpu).TotalMilliseconds,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-alloc});
 Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connections[2],new(connections[2].Player.Slot,2,1,0,9,0))),"disjoint player not blocked by shared work");
}
foreach(var c in connections[..2]){
 Check(inventory.TryCaptureShopBankOwnership(c,out var q,out _),"final q");Check(q.Sum(x=>(int)x)<=160*100,"no funded bank grant");
 Check(inventory.CaptureFundedQuantities(c).Sum(x=>(int)x)<=58*100,"no funded main grant");
 Check(players.CanApplyShopSource(c.Source),"failed journal retired");
}
if(cooperative)Check(tick>0,"actual cooperative tick work exercised");
if(retire)Check(membership.TryGet(connections[0],out var dead)&&dead.Life==0,"newer dead body retained");
}
return checks;
}
static class Fields{
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="membership")]internal static extern ref RuntimePlayerMembership Members(PlayerAuthority p);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="inventory")]internal static extern ref RuntimePlayerInventoryStore Items(PlayerAuthority p);
}

}
