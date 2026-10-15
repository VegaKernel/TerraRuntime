using Xunit;

using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Network;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

using TerraRuntime.Gameplay.Items;
namespace TerraRuntime.Tests;
internal static partial class CraftStateChecks
{
    internal static void Run()
    {
        using (var reachInput = CraftSource("CraftingReachLinuxFirst1458"))
        using (var reachGzip = new GZipStream(reachInput, CompressionMode.Decompress))
        using (var reach = JsonDocument.Parse(reachGzip))
        foreach (bool artisan in new[]{false,true})
        {
            using var boundary = new Fixture("inactive-first",true);
            var c = boundary.Connections[2];
            byte unlocks = artisan ? (byte)64 : (byte)1;
            boundary.Apply(new PlayerAppearanceRuntimeCommand(c,new(c.Player.Slot,0,0,0,0,"Craft",0,0,0,
                default,default,default,default,default,default,default,0,0,unlocks)));
            // Source-configured constructor position matching the independent
            // reach corpus; not a claim that a remote movement can teleport.
            boundary.Member(2).PositionX = 400;
            boundary.Member(2).PositionY = 400;
            boundary.Member(2).VelocityX = boundary.Member(2).VelocityY = 0;
            boundary.Tick();
            Assert.True(boundary.Member(2).ItemPhase?.PermanentCraftingRegion is not null);
            var actual = boundary.Member(2).ItemPhase!.Value.PermanentCraftingRegion!.Value;
            var expected = reach.RootElement.GetProperty("rows").EnumerateArray().First(row=>
                row.GetProperty("gameMode").GetInt32()==0 && !row.GetProperty("far").GetBoolean() &&
                row.GetProperty("artisan").GetBoolean()==artisan && row.GetProperty("mounted").GetInt32()==-1);
            Assert.Equal(expected.GetProperty("left").GetInt32(),actual.Left);
            Assert.Equal(expected.GetProperty("top").GetInt32(),actual.Top);
            Assert.Equal(expected.GetProperty("right").GetInt32(),actual.Right);
            Assert.Equal(expected.GetProperty("bottom").GetInt32(),actual.Bottom);
        }
        foreach(string corpus in new[]{"crafting-local-original-source-v1","crafting-current-bank-original-source-v1"})
        foreach(string platform in new[]{"Linux","Windows"})
        {
            using var stream=CraftSource((corpus == "crafting-local-original-source-v1" ? "CraftingLocal" : "CraftingCurrentBank") + platform + "First1458");
            using var gz=new GZipStream(stream,CompressionMode.Decompress);using var data=JsonDocument.Parse(gz);
            foreach(var row in data.RootElement.EnumerateArray())
            {
                if(!row.GetProperty("enough").GetBoolean() || !row.GetProperty("environment").GetBoolean() || row.GetProperty("before").GetProperty("alchemyTable").GetBoolean())continue;
                foreach (int bankProjectile in row.GetProperty("mode").GetString()=="bench-openbank" ? new[]{0,525,960} : new[]{0})
                foreach(string censusMutation in bankProjectile==525 ? new[]{"none","unrelated","birth","motion"} : new[]{"none"})
                foreach(string callbackMode in row.GetProperty("mode").GetString()=="torch-normal" ? new[]{"none","throw","reenter"} : new[]{"none"})
                foreach (int policy in row.GetProperty("mode").GetString() is "torch-normal" or "torch-cursor-merge" ? new[]{0,1,2} : new[]{0})
                {
                bool deny = policy == 1;
                bool partial = policy == 2;
                bool invalidCensus = censusMutation is "birth" or "motion";
                if(callbackMode!="none" && policy!=0)continue;
                using var f=new Fixture("inactive-first",true);
                var c=f.Connections[2];
                RuntimePlayerInventoryItem Tuple(JsonElement x)=>x.GetProperty("type").GetInt32()==0?default:new(new(x.GetProperty("type").GetInt32()),(short)x.GetProperty("stack").GetInt32(),new(x.GetProperty("prefix").GetInt32()),x.GetProperty("favorite").GetBoolean()?PlayerEquipmentCommitRequest.FavoriteItemFlag:(byte)0);
                var before=row.GetProperty("before").GetProperty("inventory").EnumerateArray().Select(Tuple).ToArray();
                for(short slot=0;slot<59;slot++) f.Apply(new PlayerEquipmentRuntimeCommand(c,before[slot].ToCommitRequest(c.Player.Slot,slot)));
                var banks=row.GetProperty("before").GetProperty("banks").EnumerateArray().SelectMany(bank=>bank.EnumerateArray().Select(Tuple)).ToArray();
                short[] bankStarts=[99,299,500,700];
                for(int bank=0;bank<4;bank++)for(int slot=0;slot<40;slot++)
                    if(!banks[bank*40+slot].IsEmpty) f.Apply(new PlayerEquipmentRuntimeCommand(c,banks[bank*40+slot].ToCommitRequest(c.Player.Slot,(short)(bankStarts[bank]+slot))));
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                for(int tick=0;!f.Players.CanApplyShopSource(c.Source)&&tick<2000;tick++)f.Tick();
                Assert.True(f.Players.CanApplyShopSource(c.Source));
                var store=Inventory(f.Players);
                var initialFunding=before.Select((x,slot)=>slot==58 ? x.Stack : deny ? (short)0 : partial && !x.IsEmpty ? (short)1 : x.Stack).ToArray();
                store.AdoptFundedQuantities(c,initialFunding);
                banks.Select(x=>x.Stack).ToArray().CopyTo(BankQuantities(store),2*160);
                var adjacent=row.GetProperty("before").GetProperty("adjTile").EnumerateArray().Select(x=>x.GetBoolean()).ToArray();
                int tileOffset=0;
                for(int tile=0;tile<adjacent.Length;tile++)if(adjacent[tile])
                    f.Tiles.Set(102+tileOffset++,100,new WorldTile{Type=(ushort)tile,Flags=WorldTileFlags.Active});
                for(int bank=0;bank<4;bank++)if(corpus=="crafting-local-original-source-v1" && bankProjectile==0 && banks.Skip(bank*40).Take(40).Any(x=>!x.IsEmpty))
                    f.Tiles.Set(103+bank,99,new WorldTile{Type=(ushort)(bank switch{0=>29,1=>97,2=>463,_=>491})});
                ProjectileSnapshot bankSnapshot=default;
                if(bankProjectile!=0)
                {
                    // Configured source query composition: metadata defaults and
                    // Nearby source eligibility are independently captured. This
                    // adds no original whole-State/AI claim.
                    var pstate=new ProjectileStateUpdate(new(bankProjectile),17,1680,1606,0,0,default,0,0,0,0);
                    Assert.True(f.Projectiles.TrySpawn(9,in pstate,out bankSnapshot));
                }
                f.Players.SetCraftingWorldFacts(default(RuntimeTownCommerceWorldFacts1458) with {WorldSurface=80});
                Assert.True(store.TryCaptureShopJournal(c,out var starting)&&starting is not null);
                long rejectedBefore=f.Players.RejectedEquipmentUpdates;
                if(corpus=="crafting-current-bank-original-source-v1")
                {
                    short currentChest = (short)row.GetProperty("before").GetProperty("chest").GetInt32();
                    // Configured source UI envelope preceding original craft sync;
                    // CraftItem capture itself does not publish an intent packet.
                    f.Apply(new ClientActiveChestRuntimeCommand(c,new(currentChest,0,0,0,string.Empty)));
                    Assert.True(f.Chests.TryCaptureContainerContext(c,out var ui));
                    Assert.True(ui.Observed);
                    Assert.Equal(currentChest,ui.ChestId);
                }
                foreach(var frame in row.GetProperty("syncFrames").EnumerateArray())
                {
                    byte[] bytes=Convert.FromHexString(frame.GetString()!);
                    var wire=new TerrariaFrame((ushort)bytes.Length,bytes[2],new ReadOnlySequence<byte>(bytes),new ReadOnlySequence<byte>(bytes.AsMemory(3)));
                    if(bytes[2]==5){Assert.Equal(TerrariaPlayerEquipmentDecodeResult.Decoded,TerrariaPlayerEquipmentCodec.TryDecode(in wire,out var item));f.Apply(new PlayerEquipmentRuntimeCommand(c,new(c.Player.Slot,item.SlotId,item.Stack,item.Prefix,item.ItemNetId,item.ItemFlags)));}
                    else if(bytes[2]==138) f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                    else throw new InvalidOperationException("unexpected original craft packet");
                }

                Assert.True(!f.Players.CanApplyShopSource(c.Source));
                Assert.True(store.TryCaptureShopJournal(c,out var rawBeforeWork) && rawBeforeWork is not null);
                bool callbackEntered=false;
                int callbackThrows=0, oldOutputEvents=0;
                if(callbackMode!="none")
                    f.Events.OnEquipment=(observedConnection,update)=>
                    {
                        if(observedConnection!=c)return;
                        if(callbackEntered)
                        {
                            if(update.SlotId==58 && update.ItemNetId==8 && update.Stack>0)oldOutputEvents++;
                            return;
                        }
                        callbackEntered=true;
                        Assert.True(store.TryCaptureShopJournal(c,out var joint)&&joint is not null);
                        var jointPhysical=row.GetProperty("afterSync").GetProperty("inventory").EnumerateArray().Select(Tuple)
                            .Concat(row.GetProperty("afterSync").GetProperty("banks").EnumerateArray().SelectMany(bank=>bank.EnumerateArray().Select(Tuple))).ToArray();
                        for(int slot=0;slot<219;slot++)
                        {
                            Assert.Equal(jointPhysical[slot],joint!.Before[slot]);
                            Assert.Equal(jointPhysical[slot].Stack,joint.Quantity[slot]);
                            Assert.Equal(starting!.Copper[slot],joint.Copper[slot]);
                        }
                        for(int lane=0;lane<joint!.Caps.Length;lane++)Assert.Equal(starting!.Caps[lane],joint.Caps[lane]);
                        if(callbackMode=="throw")throw new CallbackFailure();
                        f.Apply(new PlayerDisconnectRuntimeCommand(c));
                        Assert.True(!f.Players.TryGet(c.Player.Slot.Value,out _));

                    };
                int slices=0;
                if(censusMutation!="none")
                {
                    f.Tick(); slices++;
                    if(censusMutation=="unrelated")
                    {
                        var arrow=new ProjectileStateUpdate(new(1),17,100,100,1,0,default,0,0,0,0);
                        Assert.True(f.Projectiles.TrySpawn(900,in arrow,out var extra));
                        Assert.True(f.Projectiles.TryRemove(extra.Handle,out _));
                    }
                    else if(censusMutation=="birth")
                    {
                        var bank=new ProjectileStateUpdate(new(960),17,1690,1627,0,0,default,0,0,0,0);
                        Assert.True(f.Projectiles.TrySpawn(900,in bank,out _));
                    }
                    else
                    {
                        var movedBank=new ProjectileStateUpdate(new(525),17,1700,1606,0,0,default,0,0,0,0);
                        Assert.True(f.Projectiles.TryUpdate(bankSnapshot.Handle,in movedBank,out _));
                    }
                }
                while(!f.Players.CanApplyShopSource(c.Source)&&slices<2000)
                {
                    try { f.Tick(); }
                    catch(CallbackFailure) { callbackThrows++; }
                    Assert.True(f.Players.LastShopWorkSteps<=64);slices++;
                }
                if(callbackMode!="none")
                {
                    Assert.True(callbackEntered);
                    Assert.Equal(callbackMode=="throw"?1:0,callbackThrows);
                    if(callbackMode=="reenter")Assert.Equal(0,oldOutputEvents);
                    f.Events.OnEquipment=null;
                }
                Assert.True(f.Players.CanApplyShopSource(c.Source));
                if(callbackMode=="reenter")
                {
                    Assert.True(!store.TryGet(c,58,out _));
                    // Late original output report cannot recreate a disconnected
                    // actor or its private budget.
                    var born=row.GetProperty("afterSync").GetProperty("inventory")[58];
                    f.Apply(new PlayerEquipmentRuntimeCommand(c,Tuple(born).ToCommitRequest(c.Player.Slot,58)));
                    Assert.True(!f.Players.TryGet(c.Player.Slot.Value,out _));
                    Assert.Equal((short)0,store.CaptureFundedQuantities(c)[58]);

                    continue;
                }
                Assert.Equal(rejectedBefore+(deny||invalidCensus?1L:0L),f.Players.RejectedEquipmentUpdates);
                var expected=row.GetProperty("afterSync").GetProperty("inventory").EnumerateArray().Select(Tuple).ToArray();
                if(deny) expected[58]=before[58];
                if(callbackMode=="reenter") { expected[20]=expected[58]; expected[58]=default; }
                if(invalidCensus) expected=rawBeforeWork!.Before.Take(59).ToArray();
                for(short slot=0;slot<59;slot++){Assert.True(store.TryGet(c,slot,out var actual));Assert.Equal(expected[slot],actual);}
                var q=store.CaptureFundedQuantities(c);
                for(int slot=0;slot<59;slot++)
                {
                    short expectedQ = invalidCensus ? rawBeforeWork!.Quantity[slot] : deny ? initialFunding[slot] : expected[slot].Stack;
                    if (partial)
                    {
                        int debit = before[slot].ItemType == expected[slot].ItemType ? before[slot].Stack - expected[slot].Stack : before[slot].Stack;
                        expectedQ = slot == 58 ? expected[slot].Stack : (short)Math.Max(0, initialFunding[slot] - debit);
                    }
                    Assert.Equal(expectedQ,q[slot]);
                }
                Assert.True(store.TryCaptureShopJournal(c,out var final)&&final is not null);
                var expectedBanks=invalidCensus ? rawBeforeWork!.Before.Skip(59).ToArray() : row.GetProperty("afterSync").GetProperty("banks").EnumerateArray().SelectMany(bank=>bank.EnumerateArray().Select(Tuple)).ToArray();
                for(int bankSlot=0;bankSlot<160;bankSlot++){Assert.Equal(expectedBanks[bankSlot],final!.Before[59+bankSlot]);Assert.Equal(invalidCensus ? rawBeforeWork!.Quantity[59+bankSlot] : expectedBanks[bankSlot].Stack,final.Quantity[59+bankSlot]);}
                for(int lane=0;lane<final!.Caps.Length;lane++)Assert.Equal(starting!.Caps[lane],final.Caps[lane]);
                for(int slot=0;slot<219;slot++)Assert.Equal(starting!.Copper[slot],final.Copper[slot]);
                if (deny && !before[58].IsEmpty)
                {
                    // Configured ordinary cursor producer after refusal: the
                    // preserved paid sibling is moved, not refunded or re-funded.
                    var oldMouse = new WorldItemPickupSlot1458(before[58].ItemType.Value, before[58].Stack,
                        before[58].Prefix.Value, (before[58].ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);
                    var empty = default(WorldItemPickupSlot1458);
                    Assert.True(VanillaInventoryCursorMove1458.TryPlanLeftClick(in empty, in oldMouse,
                        out var settled, out var cleared, VanillaInventoryCursorMove1458.OrdinaryContext));
                    Assert.Equal((short)0, cleared.Stack);
                    f.Apply(new PlayerEquipmentRuntimeCommand(c, before[58].ToCommitRequest(c.Player.Slot,20)));
                    f.Apply(new PlayerEquipmentRuntimeCommand(c, default(RuntimePlayerInventoryItem).ToCommitRequest(c.Player.Slot,58)));
                    f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                    for(int follow=0;!f.Players.CanApplyShopSource(c.Source)&&follow<2000;follow++) f.Tick();
                    Assert.True(f.Players.CanApplyShopSource(c.Source));
                    Assert.True(store.TryGet(c,20,out var settledOwned));
                    Assert.Equal(before[58],settledOwned);
                    Assert.True(store.TryGet(c,58,out var clearedOwned));
                    Assert.True(clearedOwned.IsEmpty);
                    var afterMove = store.CaptureFundedQuantities(c);
                    Assert.Equal(initialFunding[58],afterMove[20]);
                    Assert.Equal((short)0,afterMove[58]);
                    Assert.True(store.TryCaptureShopJournal(c,out var movedImage)&&movedImage is not null);
                    for(int slot=0;slot<219;slot++)
                    {
                        var wanted=slot==20 ? before[58] : slot==58 ? default : final.Before[slot];
                        Assert.Equal(wanted,movedImage!.Before[slot]);
                        Assert.Equal(final.Copper[slot],movedImage.Copper[slot]);
                    }
                    for(int lane=0;lane<final.Caps.Length;lane++)Assert.Equal(final.Caps[lane],movedImage!.Caps[lane]);
                }

                }
            }
        }
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "shopBankFundedQuantities")]
    private static extern ref short[] BankQuantities(RuntimePlayerInventoryStore store);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? UpdateRandom(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState owner);
    private sealed class Fixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeProjectileStore Projectiles;
        internal readonly RuntimeChestStore Chests = new(Array.Empty<WorldChest>());
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly ServerRuntimeState? state;
        private readonly RuntimeChestCommandProcessor? chestProcessor;
        internal readonly EventSink Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        internal readonly Dictionary<byte, short> ItemIds = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal Fixture(string mode, bool runtimeTick = false)
        {
            for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = Projectiles = new RuntimeProjectileStore();
            if (runtimeTick)
            {
                var chestCommands = chestProcessor = new RuntimeChestCommandProcessor(Chests, new RuntimeChestReplicationRegistry(),
                    Tiles, connection => state?.IsCurrentPlayerConnection(connection) == true);
                state = new(playerEvents: Events, worldTiles: Tiles, projectiles: projectiles, chestCommands: chestCommands,
                    townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
                    playerUpdateRandomSeed: new(0));
                Players = Composition(state).Players;
                Random = UpdateRandom(Players)!;
            }
            else { Players = new(Events, Tiles); Random = new(0); }
            // These prefix-none references have identical clocks on both source platforms.
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false)
            { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows() });
            Players.SetPlayerUpdateRandom(Random);
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            Players.SetNpcHealthWorldFacts(() => new(false, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new(lease!));
            }
            var membership = Membership(Players);
            // Slot255 is not a connection lease. A trusted imported source placeholder remains untouched.
            membership.Commit(new RuntimePlayerMember
            {
                Slot = new(255),
                Revision = 1,
                Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255), new(1))),
                ItemPhase = null,
                PhysicsPhase = null
            });
            foreach (byte slot in mode == "reverse-join" ? new byte[] { 2, 0 } : new byte[] { 0, 2 })
            {
                if (slot == 0 && mode == "inactive-first") continue;
                var session = sessions[slot]; session.ObserveWorldRequest(); session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = slot == 2 ? (short)0 : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
                if (slot == 2 && mode == "potion-first") id = 0;
                ItemIds.Add(slot, id);
                Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
                Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
                Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, id == 0 ? (short)0 : (short)3, 0, id, 0)));
                Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
                Move(slot, false);
                // Explicit source-configured imported lifecycle branches, after genuine membership creation.
                // Packet movement admission is not weakened to inject outside coordinates.
                if (slot == 0 && mode == "outside-first") { Member(slot).PositionX = 0; Member(slot).PositionY = 0; }
                if (slot == 0 && mode == "dead-first") Member(slot).IsDead = true;
                if (slot == 0 && mode == "ghost-first") Member(slot).MovementFlags |= VanillaPlayerHealthContext1458.GhostMovementFlag;
            }
        }
        internal RuntimePlayerMember Member(byte slot) { Assert.True(Players.TryGet(slot, out var member)); return member; }
        internal void Apply(RuntimeCommand command)
        {
            if (state is null) Assert.True(Players.TryApply(command));
            else if (chestProcessor?.TryApply(command) != true) state.Apply(command);
        }
        internal void Tick() { Assert.NotNull(state); state.Tick(); }
        internal bool Step()
        {
            if (state is null) return Players.TryTickRemotePlayerPhase();
            state.Tick();
            return Member(2).ItemPhase is not null;
        }
        internal void ReconnectFirst()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[0]));
            sessions[0].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            Assert.Equal(new PlayerSlotId(0), session.Slot);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99100), session.Handle);
            Connections[0] = connection;
            Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        }
        internal void Move(byte slot, bool control)
        {
            var member = Member(slot);
            bool first = moved.Add(slot);
            if (!first && member.PositionX == 0 && member.PositionY == 0)
            {
                // Trusted source-configured outside import: ordinary movement would reject this geometry.
                member.ControlFlags = control ? (byte)32 : (byte)0;
                return;
            }
            Apply(new PlayerMovementRuntimeCommand(Connections[slot], new(new(slot), control ? (byte)32 : (byte)0,
                (byte)(member.MovementFlags | 16), 0, 64, 0, first ? 1600 + slot * 40 : member.PositionX,
                first ? 1606 : member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal PlayerBuffState Buffs(byte slot) => Profiles(Players).CaptureBuffState(Connections[slot])!;
        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
    private sealed class CallbackFailure : Exception;
    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen = [];
        internal readonly List<ConnectionHandle> HealthConnections = [];
        internal readonly List<ConnectionHandle> BuffConnections = [];
        internal Action<ConnectionHandle, PlayerEquipmentCommitRequest>? OnEquipment = null;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) { Count++; Seen.Add(5); OnEquipment?.Invoke(c, r); }
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r) { Count++; Seen.Add(16); HealthConnections.Add(c); }
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r) { Count++; Seen.Add(42); }
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r) { Count++; Seen.Add(50); BuffConnections.Add(c); }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
