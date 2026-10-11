using TerraRuntime.Core.Players;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

internal static class SelectedPotionPresentationRelay1458Checks
{
    internal static int Run() => RunProfile(false)+RunProfile(true)+RunBoundedOwnerLoop();
    private static int RunBoundedOwnerLoop()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        var source = GameCommandSourceId.FromConnection(710);
        var origin = Outbound();
        var peerOutbound = Outbound();
        var replicator = new RuntimePlayerVitalsReplicator();
        using var bootstrap = new PlayerBootstrapFrameSink(new PlayerSlotPool(1), origin,
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 },
                Array.Empty<ReadOnlyMemory<byte>>(), new byte[] { 3, 0, 49 }), source, new Spawn());
        var connectionPolicy = new TerrariaConnectionPolicyState(TerrariaConnectionPolicyOptions.Default);
        Check(Pump(new TerrariaConnectionPolicySink(bootstrap, connectionPolicy), Frame(1, [11,84,101,114,114,97,114,105,97,51,50,54])) == TerrariaFrameSinkResult.Continue, "loop bootstrap");
        var connection = new ConnectionHandle(source, bootstrap.AssignedPlayerHandle!.Value);
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(711), new(new(1), new(1)));
        Check(replicator.TryRegister(source, origin) && replicator.TryRegister(peer.Source, peerOutbound), "loop endpoints");
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 100, 100, 0, 0, 0, 0, 0);
        var peerSpawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 100, 100, 0, 0, 0, 0, 0);
        replicator.PlayerSpawned(connection, in spawn);
        replicator.PlayerSpawned(peer, in peerSpawn);
        while (Access.Queue(origin).TryRead(out _)) { }
        var state = new ServerRuntimeState(playerEvents: new Events(replicator));
        var players = Access.Composition(state).Players;
        var member = new RuntimePlayerMember { Slot = connection.Player.Slot, Connection = connection,
            Revision = 7, Life = 32767, Mana = 32767, HasHealth = true, HasMana = true };
        Access.Membership(players).Commit(member);
        var inventory = Access.Inventory(players);
        Check(inventory.TrySet(connection, new(connection.Player.Slot, 0, 3, 0, 28, 0)), "loop physical unfunded setup");
        var beforeInventory = new RuntimePlayerInventoryItem[59];
        Check(inventory.TryCopyInventory(connection, beforeInventory), "loop inventory capture");
        var beforeQuantity = inventory.CaptureFundedQuantities(connection);
        var beforeCopper = inventory.CaptureFundedCoinValues(connection);
        var beforeCaps = inventory.CaptureOwnedQuantities(connection);
        var beforeBody = member.CaptureSnapshot();
        var buffs = new PlayerBuffState();
        buffs.ReplaceNetworkSnapshot(new BuffTypeId[] { new(21), new(94) });
        Access.Profiles(players).RestoreBuffState(connection, buffs);
        var random = new VanillaUnifiedRandom1458(1458);
        players.SetPlayerUpdateRandom(random);
        var expectedRandom = random.Clone();
        using var applied = new ManualResetEventSlim();
        var appliedIds = new List<byte>();
        int ownerThread = 0;
        using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(state,
            (owned, command) =>
            {
                ownerThread = Environment.CurrentManagedThreadId;
                owned.Apply(command);
                appliedIds.Add(((PlayerVitalsPresentationRuntimeCommand)command).MessageId);
                if (appliedIds.Count == 2) applied.Set();
            }, _ => { }, new GameLoopOptions { CommandCapacity = 2, MaxPendingCommandsPerSource = 2,
                MaxCommandIngressPerTick = 2, MaxCommandsPerTick = 2, MaxCommandsPerSourcePerTick = 2 });
        var ingress = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(loop);
        var sink = new PlayerVitalsFrameSink(source, bootstrap, new Health(), new Mana(),
            new RuntimePlayerVitalsPresentationIngress(ingress));
        var wire = new TerrariaConnectionPolicySink(sink, connectionPolicy);
        Check(Pump(wire, Frame(35, [255, 255, 255])) == TerrariaFrameSinkResult.Continue, "first bounded occurrence");
        Check(Pump(wire, Frame(43, [255, 0, 128])) == TerrariaFrameSinkResult.Continue, "second bounded occurrence");
        Check(Pump(wire, Frame(35, [255, 1, 0])) == TerrariaFrameSinkResult.Continue, "pressure does not disconnect");
        Check(loop.Snapshot.PendingCommands == 2 && loop.Snapshot.RejectedCommands == 1, "capacity2 rejects third occurrence");
        Check(peerOutbound.QueuedFrames == 0, "no event before owner loop starts");
        int producerThread = Environment.CurrentManagedThreadId;
        loop.Start();
        Check(applied.Wait(TimeSpan.FromSeconds(5)), "bounded owner completion signal");
        loop.Dispose();
        Check(loop.Fault is null && ownerThread != producerThread, "actual State.Apply runs on owner thread");
        Check(appliedIds.SequenceEqual(new byte[] { 35, 43 }), "real mailbox preserves occurrence FIFO");
        Check(origin.QueuedFrames == 0 && peerOutbound.QueuedFrames == 2, "no origin or fabricated third relay");
        Check(Access.Queue(peerOutbound).TryRead(out var heal) && heal.Bytes.Span.SequenceEqual(Convert.FromHexString("06002300FFFF")), "ordered signed healing relay");
        Check(Access.Queue(peerOutbound).TryRead(out var mana) && mana.Bytes.Span.SequenceEqual(Convert.FromHexString("06002B000080")), "ordered signed mana relay");
        Check(member.CaptureSnapshot() == beforeBody, "loop cosmetic report cannot mutate vitals or member");
        var afterInventory = new RuntimePlayerInventoryItem[59];
        Check(inventory.TryCopyInventory(connection, afterInventory) && afterInventory.AsSpan().SequenceEqual(beforeInventory), "loop full59 unchanged");
        Check(inventory.CaptureFundedQuantities(connection).AsSpan().SequenceEqual(beforeQuantity) &&
            inventory.CaptureFundedCoinValues(connection).AsSpan().SequenceEqual(beforeCopper) &&
            inventory.CaptureOwnedQuantities(connection).AsSpan().SequenceEqual(beforeCaps), "loop q/copper/caps unchanged");
        Check(Access.Profiles(players).IsCurrentBuffState(connection, buffs), "loop indexed44 unchanged");
        Check(random.Next() == expectedRandom.Next(), "loop public RNG cursor unchanged");
        return checks;
    }
    private static int RunProfile(bool fullVitals)
    {
        int checks=0;
        void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
        var preSource=GameCommandSourceId.FromConnection(700);
        var preMailbox=new Mailbox();
        using(var preBootstrap=new PlayerBootstrapFrameSink(new PlayerSlotPool(1),Outbound(),
        PlayerBootstrapPacketSet.CreateForTesting(new byte[]{3,0,7},Array.Empty<ReadOnlyMemory<byte>>(),new byte[]{3,0,49}),preSource,new Spawn()))
        {
            var preSink=new PlayerVitalsFrameSink(preSource,preBootstrap,new Health(),new Mana(),new RuntimePlayerVitalsPresentationIngress(preMailbox));
            Check(Pump(new TerrariaConnectionPolicySink(preSink,new TerrariaConnectionPolicyState(TerrariaConnectionPolicyOptions.Default)),Frame(35,new byte[]{255,1,0}))==TerrariaFrameSinkResult.Stop,"preassignment delegates original handshake refusal");
            Check(preMailbox.Commands.Count==0&&preBootstrap.AssignedPlayerHandle is null,"no preassignment effect command or actor");
        }
        var source=GameCommandSourceId.FromConnection(701);
        var ingress=new Mailbox();
        using var bootstrap=new PlayerBootstrapFrameSink(new PlayerSlotPool(2),Outbound(),
        PlayerBootstrapPacketSet.CreateForTesting(new byte[]{3,0,7},Array.Empty<ReadOnlyMemory<byte>>(),new byte[]{3,0,49}),source,new Spawn());
        var sink=new PlayerVitalsFrameSink(source,bootstrap,new Health(),new Mana(),new RuntimePlayerVitalsPresentationIngress(ingress));
        var wire = new TerrariaConnectionPolicySink(sink, new TerrariaConnectionPolicyState(TerrariaConnectionPolicyOptions.Default));
        Check(Pump(wire, Frame(1,[11,84,101,114,114,97,114,105,97,51,50,54]))==TerrariaFrameSinkResult.Continue,"hello");
        var actor=bootstrap.AssignedPlayerHandle!.Value;
        var connection=new ConnectionHandle(source,actor);
        var peer=new ConnectionHandle(GameCommandSourceId.FromConnection(702),new(new(1),new(1)));
        var originOut=Outbound();var peerOut=Outbound();var secondPeerOut=Outbound();var rep=new RuntimePlayerVitalsReplicator();
        var secondPeer=new ConnectionHandle(GameCommandSourceId.FromConnection(703),new(new(2),new(1)));
        Check(rep.TryRegister(source,originOut)&&rep.TryRegister(peer.Source,peerOut)&&rep.TryRegister(secondPeer.Source,secondPeerOut),"endpoints");
        var a=new PlayerSpawnCommitRequest(actor.Slot,100,100,0,0,0,0,0);var z=new PlayerSpawnCommitRequest(peer.Player.Slot,100,100,0,0,0,0,0);
        rep.PlayerSpawned(connection,in a);rep.PlayerSpawned(peer,in z);
        var secondSpawn=new PlayerSpawnCommitRequest(secondPeer.Player.Slot,100,100,0,0,0,0,0);rep.PlayerSpawned(secondPeer,in secondSpawn);
        var state = new ServerRuntimeState(playerEvents: new Events(rep));
        var players = Access.Composition(state).Players;
        var member=new RuntimePlayerMember{Slot=actor.Slot,Connection=connection,Revision=7,Life=(short)(fullVitals?32767:45),Mana=(short)(fullVitals?32767:12),HasHealth=true,HasMana=true};
        Access.Membership(players).Commit(member);
        var inventory=Access.Inventory(players);
        Check(inventory.TrySet(connection,new(actor.Slot,0,3,0,28,0)),"unconfirmed visible healing potion setup");
        Check(inventory.TrySet(connection,new(actor.Slot,1,3,0,110,0)),"unconfirmed visible mana potion setup");
        var buffs=new PlayerBuffState();buffs.ReplaceNetworkSnapshot(new BuffTypeId[]{new(21),new(94)});
        Access.Profiles(players).RestoreBuffState(connection,buffs);
        var beforeBody=member.CaptureSnapshot();var beforePhase=member.ItemPhase;var beforeRevision=member.Revision;
        var beforeInventory=new RuntimePlayerInventoryItem[59];Check(inventory.TryCopyInventory(connection,beforeInventory),"full inventory snapshot");
        var beforeQuantity=inventory.CaptureFundedQuantities(connection);var beforeCopper=inventory.CaptureFundedCoinValues(connection);var beforeCaps=inventory.CaptureOwnedQuantities(connection);
        Check(beforeQuantity.All(q=>q==0),"physical reports do not fund cosmetic effects");
        var random=new VanillaUnifiedRandom1458(1458);players.SetPlayerUpdateRandom(random);var expectedRandom=random.Clone();
        using var sourceRows=System.Text.Json.JsonDocument.Parse(new GZipStream(typeof(SelectedPotionPresentationRelay1458Checks).Assembly.GetManifestResourceStream("OriginalSelectedPotionPresentationRelay1458")!,CompressionMode.Decompress));
        foreach(var row in sourceRows.RootElement.EnumerateArray())foreach(int repeat in new[]{0,1}){
            byte id=row.GetProperty("id").GetByte();short amount=row.GetProperty("amount").GetInt16();
            byte[] payload=[row.GetProperty("actor").GetByte(),0,0];BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1),amount);
            Check(Pump(wire, Frame(id,payload))==TerrariaFrameSinkResult.Continue,"frame accepted");
            Check(ingress.Commands.Count==1,"one ordered ingress occurrence");
            var command=(PlayerVitalsPresentationRuntimeCommand)ingress.Commands.Dequeue();
            Check(command.Connection==connection&&command.Amount==amount&&command.MessageId==id,"authenticated signed payload");
            state.Apply(command);
            Check(member.Revision==beforeRevision&&member.Life==(fullVitals?32767:45)&&member.Mana==(fullVitals?32767:12)&&member.ItemPhase==beforePhase,"no gameplay mutation");
            Check(member.CaptureSnapshot()==beforeBody,"entire member remains unchanged by cosmetic report");
            var currentInventory=new RuntimePlayerInventoryItem[59];Check(inventory.TryCopyInventory(connection,currentInventory)&&currentInventory.AsSpan().SequenceEqual(beforeInventory),"all59 inventory slots unchanged");
            Check(inventory.CaptureFundedQuantities(connection).AsSpan().SequenceEqual(beforeQuantity)&&inventory.CaptureFundedCoinValues(connection).AsSpan().SequenceEqual(beforeCopper)&&inventory.CaptureOwnedQuantities(connection).AsSpan().SequenceEqual(beforeCaps),"q0/copper/caps remain unchanged, no payment");
            Check(Access.Profiles(players).IsCurrentBuffState(connection,buffs),"full indexed44 types/durations unchanged");
            Check(originOut.QueuedFrames==0&&peerOut.QueuedFrames==1&&secondPeerOut.QueuedFrames==1,"each playing peer only, repeated occurrences not suppressed");
            Check(Access.Queue(peerOut).TryRead(out var frame),"outbound present");
            byte[] expected=Convert.FromHexString(row.GetProperty("frames")[repeat*2].GetString()!);
            Check(frame.Bytes.Span.SequenceEqual(expected),"exact original six byte relay");
            Check(Access.Queue(secondPeerOut).TryRead(out var secondFrame)&&secondFrame.Bytes.Span.SequenceEqual(Convert.FromHexString(row.GetProperty("frames")[repeat*2+1].GetString()!)),"second original peer relay");
        }
        var stale=connection with{Player=new(actor.Slot,new(actor.Generation.Value+1))};
        state.Apply(new PlayerVitalsPresentationRuntimeCommand(stale,35,123));Check(peerOut.QueuedFrames==0&&secondPeerOut.QueuedFrames==0,"stale has no relay");
        rep.PlayerDisconnected(connection);state.Apply(new PlayerVitalsPresentationRuntimeCommand(connection,35,123));Check(peerOut.QueuedFrames==0&&secondPeerOut.QueuedFrames==0,"disconnected endpoint no relay");
        Check(!new RuntimePlayerVitalsPresentationIngress(ingress).TryPost(connection,16,2),"invalid kind");
        ingress.Accept=false;Check(Pump(wire, Frame(43,[251,0,0]))==TerrariaFrameSinkResult.Continue&&ingress.Commands.Count==0,"rejected configured ingress has no fabricated occurrence");
        Check(Pump(wire, Frame(35,[251,0]))==TerrariaFrameSinkResult.Stop&&sink.RejectionCategory==TerrariaFrameRejectionCategory.MalformedProtocol,"malformed exact length");
        Check(random.Next()==expectedRandom.Next(),"public RNG cursor offer unchanged by every relay occurrence");
        return checks;
    }
static TerrariaFrameSinkResult Pump(ITerrariaFrameSink sink, TerrariaFrame frame)
{
    var pipe = new System.IO.Pipelines.Pipe();
    pipe.Writer.WriteAsync(frame.Packet.ToArray()).AsTask().GetAwaiter().GetResult();
    pipe.Writer.Complete();
    var result = TerrariaPipeFramePump.RunAsync(pipe.Reader, sink).AsTask().GetAwaiter().GetResult();
    pipe.Reader.Complete();
    if (result == TerrariaPipePumpResult.SinkStopped) return TerrariaFrameSinkResult.Stop;
    if (result != TerrariaPipePumpResult.Completed) throw new Exception($"Unexpected raw pipe result {result}");
    return TerrariaFrameSinkResult.Continue;
}
static TerrariaConnectionOutboundQueue Outbound()=>new(new OutboundQueueOptions(64,8192,1024));
static TerrariaFrame Frame(byte id,byte[] payload){byte[] bytes=new byte[payload.Length+3];BinaryPrimitives.WriteUInt16LittleEndian(bytes,(ushort)bytes.Length);bytes[2]=id;payload.CopyTo(bytes,3);return new((ushort)bytes.Length,id,new(bytes),new(payload));}
sealed class Mailbox:IGameCommandIngress<RuntimeCommand>{public readonly Queue<RuntimeCommand> Commands=new();public bool Accept=true;public bool TryPost(GameCommandSourceId source,RuntimeCommand command){if(!Accept)return false;Commands.Enqueue(command);return true;}}
sealed class Spawn:IPlayerSpawnCommitIngress{public bool TryPost(GameCommandSourceId source,PlayerJoinSession session,in PlayerSpawnCommitRequest request)=>true;}
sealed class Health:IPlayerHealthIngress{public bool TryPost(ConnectionHandle c,in PlayerHealthCommitRequest r)=>true;}
sealed class Mana:IPlayerManaIngress{public bool TryPost(ConnectionHandle c,in PlayerManaCommitRequest r)=>true;}
sealed class Events(RuntimePlayerVitalsReplicator rep):IRuntimePlayerEventSink{
 public void PlayerVitalsPresentation(ConnectionHandle c,byte id,short amount)=>rep.PlayerVitalsPresentation(c,id,amount);
 public void PlayerAppearanceUpdated(ConnectionHandle c,in PlayerAppearanceCommitRequest r){}
 public void PlayerEquipmentUpdated(ConnectionHandle c,in PlayerEquipmentCommitRequest r){}
 public void PlayerSpawned(ConnectionHandle c,in PlayerSpawnCommitRequest r){}
 public void PlayerMoved(ConnectionHandle c,in PlayerMovementCommitRequest r){}
 public void PlayerDisconnected(ConnectionHandle c){}
}
static class Access{[UnsafeAccessor(UnsafeAccessorKind.Field,Name="inventory")]internal static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority players);[UnsafeAccessor(UnsafeAccessorKind.Field,Name="transferProfiles")]internal static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority players);[UnsafeAccessor(UnsafeAccessorKind.Field,Name="_runtime")]internal static extern ref ServerRuntimeComposition Composition(ServerRuntimeState state);[UnsafeAccessor(UnsafeAccessorKind.Field,Name="_queue")]internal static extern ref BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue queue);[UnsafeAccessor(UnsafeAccessorKind.Field,Name="membership")]internal static extern ref RuntimePlayerMembership Membership(PlayerAuthority players);}
}
