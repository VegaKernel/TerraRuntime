using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
namespace TerraRuntime.Tests;
internal static class BankQueueContinuationAuthorityChecks {
internal static int Run() {
int checks=0;
void Check(bool ok,string label){checks++;if(!ok)throw new InvalidOperationException(label);}
for(int mode=0;mode<3;mode++){
var state=new ServerRuntimeState(worldTiles:new WorldTileStore(new WorldDimensions(400,300)),playerUpdateRandomSeed:new(0),itemPrefixWorld:new(false,false,false));
var players=Fields.Runtime(state).Players;
players.SetPlayerUpdateWorldFacts(new(400,300,80,false,false));
var membership=Fields.Members(players);var inventory=Fields.Items(players);
var connections=new ConnectionHandle[3];
for(byte player=0;player<3;player++){
 var c=new ConnectionHandle(GameCommandSourceId.FromConnection(95000+player),new(new(player),new(1)));connections[player]=c;
 membership.Commit(new RuntimePlayerMember{Connection=c,Slot=c.Player.Slot,Revision=1,HasHealth=true,Life=400,MaxLife=400});Check(inventory.TryAttach(c),"attach");Check(Fields.Raw(players).TryAttach(c.Player),"same-owner raw occupation attach");
 if(player==2)continue;
 var q=new short[59];Check(inventory.TrySet(c,new(c.Player.Slot,10,20,0,9,0)),"funded ordinary source");q[10]=20;inventory.AdoptFundedQuantities(c,q);
 var banks=new RuntimePlayerInventoryItem[160];var bankq=new short[160];var caps=new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount];Array.Fill(caps,(short)-1);
 banks[0]=new(new(9),3,new(0),0);bankq[0]=3;Check(inventory.TryAdoptShopBanks(c,banks,new long[160],bankq,caps),"funded bank source");
}
foreach(var c in connections[..2]){
 Check(players.TryApply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,10,0,0,0,0))),"source-shaped ordinary debit");
 Check(players.TryApply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,99,23,0,9,0))),"source-shaped bank merge");
}
foreach(var c in connections[..2])Check(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(c)),"seal actual138");
Check(!players.CanApplyShopSource(connections[0].Source),"bank source parked");
var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
int updates = 0;
int maximumSteps = 0;
using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(state,
    (s, command) => {
        s.Apply(command);
        if (command is WorkerResultCommand worker) events.Enqueue("worker:" + worker.Value);
        else if (command is PlayerDisconnectRuntimeCommand) events.Enqueue("disconnect");
        else events.Enqueue("other-player");
    },
    s => {
        int update = Interlocked.Increment(ref updates);
        if (mode == 0 && update > 3) {
            players.TickShopJournalWork();
            maximumSteps = Math.Max(maximumSteps, players.LastShopWorkSteps);
            Check(players.LastShopWorkSteps <= 64, "global64 on existing queue owned work");
        }
    },
    new GameLoopOptions { TicksPerSecond = 1000, CommandCapacity = 8,
        MaxCommandIngressPerTick = 8, MaxCommandsPerTick = 8,
        MaxCommandsPerSourcePerTick = 8, MaxPendingCommandsPerSource = 2 },
    (s, source, command) => s.CanApplyCommand(source, command));
Check(loop.TryPost(connections[0].Source, new WorkerResultCommand(1)), "parked first accepted");
Check(loop.TryPost(connections[0].Source, new WorkerResultCommand(2)), "parked second accepted");
Check(!loop.TryPost(connections[0].Source, new WorkerResultCommand(3)), "same bounded pending capacity");
Check(loop.TryPost(GameCommandSourceId.System, new WorkerResultCommand(42)), "system accepted while actor sealed");
Check(loop.TryPost(connections[2].Source,
    new PlayerEquipmentRuntimeCommand(connections[2], new(connections[2].Player.Slot, 2, 1, 0, 9, 0))),
    "other actor command accepted");
loop.Start();
Check(SpinWait.SpinUntil(() => Volatile.Read(ref updates) >= 3, TimeSpan.FromSeconds(5)), "owned updates while source parked");
if (mode != 0) {
    Check(!events.Contains("worker:1"), "parked first not applied before invalidation or stop");
    Check(loop.Snapshot.PendingCommands == 2, "parked commands retain accounting");
}
if (mode == 1) {
    Check(loop.TryPost(GameCommandSourceId.System, new PlayerDisconnectRuntimeCommand(connections[0])),
        "system lifecycle accepted while actor parked");
}
if (mode != 2) {
    Check(SpinWait.SpinUntil(() => events.Contains("worker:2") || loop.Fault is not null,
        TimeSpan.FromSeconds(15)), "parked FIFO eventually drains");
}
Check(loop.Stop(TimeSpan.FromSeconds(5)), "shutdown does not wait for parked readiness");
if(loop.Fault is not null)Console.Error.WriteLine(loop.Fault);
Check(loop.Fault is null, "actual queue has no fault");
var observed = events.ToArray();
Check(observed.Contains("worker:42") && observed.Contains("other-player"), "system and third-player progress");
Check(inventory.TryGet(connections[2], 2, out var third) && third.ItemType == new ItemTypeId(9),
    "third-player actual image adopted");
if (mode != 2) {
    int first = Array.IndexOf(observed, "worker:1");
    int second = Array.IndexOf(observed, "worker:2");
    Check(first >= 0 && second > first, "same-source FIFO preserved");
    Check(Array.IndexOf(observed, "worker:42") < first, "system passed parked head");
    Check(Array.IndexOf(observed, "other-player") < first, "other actor passed parked head");
    Check(loop.Snapshot.PendingCommands == 0, "completed pending counters released");
}
if (mode == 1) {
    Check(!membership.TryGet(connections[0], out _), "system disconnect removes exact actor");
    Check(Array.IndexOf(observed, "disconnect") < Array.IndexOf(observed, "worker:1"),
        "lifecycle cancels before parked successors");
}
if (mode == 2) Check(!observed.Contains("worker:1") && !observed.Contains("worker:2"),
    "shutdown never applies still-blocked commands");

}
return checks;
}
static class Fields {
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name="npcRawSlots")]
    internal static extern ref RuntimeNpcRawPlayerSlots1458 Raw(PlayerAuthority p);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name="_runtime")]
    internal static extern ref ServerRuntimeComposition Runtime(ServerRuntimeState s);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name="membership")]
    internal static extern ref RuntimePlayerMembership Members(PlayerAuthority p);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name="inventory")]
    internal static extern ref RuntimePlayerInventoryStore Items(PlayerAuthority p);
}

}
