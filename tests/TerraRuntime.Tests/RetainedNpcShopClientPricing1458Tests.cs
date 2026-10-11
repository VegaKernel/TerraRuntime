using System.Reflection;
using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.World;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using Xunit;

namespace TerraRuntime.Tests;

// Configured runtime custody checks; this is not whole natural UI or Native parity.
// Independent official near-half price corpus: manifest c5406572c709c7e91becdd6e6862030471653c8df6119015500ff1b6353962a6.
// Genuine CLR4/x86 Windows151 and Linux152 for original custom150 * 1.01f.
public sealed class RetainedNpcShopClientPricing1458Tests
{
    [Fact]
    public void Actual_bootstrap_images_resolve_client_arithmetic_and_refuse_platform_reconstruction()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; Assert.True(value, label); }
        T Owner<T>(PlayerAuthority player, string field) => (T)typeof(PlayerAuthority).GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
        for (int mode = 0; mode < 6; mode++)
        {
            bool clientWindows = mode is 1 or 2;
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            Check(tiles.TryAttachWorldSurface(80), "end to end surface");
            var players = new PlayerAuthority(null, tiles);
            players.SetPlayerUpdateRandom(new(0));
            players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
            players.BindReceiveEquipmentRandom(new(0), !clientWindows, new(false, false, false));
            var queue = new CommandQueue();
            var spawn = new ActualSpawn(players);
            using var bootstrap = new PlayerBootstrapFrameSink(new PlayerSlotPool(1),
                new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 8192, 1024)),
                PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 }),
                GameCommandSourceId.FromConnection(708), spawn, appearanceIngress: null,
                equipmentIngress: new RuntimePlayerEquipmentIngress(queue), movementIngress: null);
            TerrariaFrame Frame(byte id, byte[] payload) => new((ushort)(3 + payload.Length), id,
                ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(payload));
            Check(bootstrap.OnFrame(Frame(1, new byte[] { 11, 84, 101, 114, 114, 97, 114, 105, 97, 51, 50, 54 })) ==
                TerrariaFrameSinkResult.Continue, "end to end hello");
            Check(bootstrap.OnFrame(Frame(6, [])) == TerrariaFrameSinkResult.Continue, "end to end world");
            Check(bootstrap.OnFrame(Frame(8, new byte[9])) == TerrariaFrameSinkResult.Continue, "end to end sections");
            var spawnPayload = new byte[15];
            BinaryPrimitives.WriteInt16LittleEndian(spawnPayload.AsSpan(1), 20);
            BinaryPrimitives.WriteInt16LittleEndian(spawnPayload.AsSpan(3), 20);
            Check(bootstrap.OnFrame(Frame(12, spawnPayload)) == TerrariaFrameSinkResult.Continue &&
                bootstrap.JoinState == PlayerJoinState.Playing, "actual authoritative spawn from bootstrap");
            var connection = spawn.Connection;
            var membership = Owner<RuntimePlayerMembership>(players, "membership");
            var inventory = Owner<RuntimePlayerInventoryStore>(players, "inventory");
            Check(membership.TryGet(connection, out var member), "spawn owns member");
            Check(inventory.TrySet(connection, new(connection.Player.Slot, 50, 10, 0, 73, 0)), "configured gold image");
            var budget = new long[59]; budget[50] = 100_000;
            inventory.AdoptFundedQuantities(connection, new short[59], budget);
            var npcs = new RuntimeNpcStore();
            var state = new NpcStateUpdate(17, 17, 300, 300, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = 250, LifeMax = 250 });
            Check(npcs.TrySpawn(0, in state, out _), "end to end vendor");
            var facts = default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80, RockLayer = 120,
                InitialDayTime = true, GameMode = 0, SpawnTileY = 20 };
            var town = new TownNpcAuthority(players, npcs, new RuntimeProjectileStore(), tiles,
                new RuntimeWorldProgressionMutations(), null, null, facts, null, null, false, false, null, false, false);
            town.ApplyTalk(connection, 0, new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 1));
            Check(membership.TryGetTownShopSession(connection.Player, out var session) && session?.Quote is not null,
                "production talk quote available to economic commands");
            Check(players.TrySetTalkNpc(connection, -1), "retire natural Town session before configured custom-price fixture");
            Check(players.TrySetTalkNpc(connection, 0), "new configured talk");
            if (mode is 2 or 3) member.ItemPhase = member.ItemPhase!.Value.ApplyAcceptedShopPriceContexts(9);
            if (mode >= 4) member.ItemPhase = member.ItemPhase!.Value with {
                ShopPriceContextStates = (byte)(mode == 4 ? 4 : 1),
                DerivedCombat = VanillaPlayerCombatSnapshot.Baseline with { DiscountEquipped = true } };
            session = new RuntimeTownShopSession1458(0, new(17), RuntimeTownShopSessionKind1458.OrdinaryShop,
                1.01f, false, default, [new(0, new(8), 150)], 0)
                { VendorHandle = session!.VendorHandle, VendorStore = npcs };
            Check(membership.TrySetTownShopSession(connection, session), "configured scalar fixture in real factory");
            Check(players.TryBindTownShopQuote(connection, session, npcs), "real factory binds client contexts");
            Check(session.Quote!.TryCapture(out var quote), "configured quote capture");
            Check(quote.CapturedPriceContextMask == (mode is 2 or 3 ? 9 : mode == 4 ? 1 : mode == 5 ? 3 : 5),
                "factory keeps exact platform/discount pairs and closed-only equipment latch");
            if (mode >= 4) continue;
            int torchSlot = Array.FindIndex(quote.Stock40, x => x.Tuple.Type == 8);
            Check(torchSlot >= 0, "merchant actually offers torch");
            var before = new RuntimePlayerInventoryItem[59];
            Check(inventory.TryCopyInventory(connection, before), "capture configured wallet");
            var tuple = before.Select(x => x.IsEmpty ? default : new WorldItemPickupSlot1458(x.ItemType.Value,
                x.Stack, x.Prefix.Value, (x.ItemFlags & 1) != 0)).ToArray();
            var pricing = new ShopPriceContext1458(quote.PriceAdjustment, mode == 3, clientWindows, new(false, false, false));
            Check(VanillaShopTransaction1458.TryPlanBuy(tuple, new WorldItemPickupSlot1458[160], quote.Stock40,
                quote.Memos, default, torchSlot, 1, pricing, out var plan) && plan is not null, "client concrete purchase plan");
            if(mode < 2) Check(plan!.CopperDelta == -(clientWindows ? 151 : 152),
                "independent genuine near-half source price Windows151 Linux152");
            for (int slot = 0; slot < 59; slot++) {
                if (tuple[slot] == plan!.Wallet.Inventory[slot]) continue;
                var item = plan.Wallet.Inventory[slot];
                var payload = new byte[9]; payload[0] = 251;
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), (short)slot);
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), (short)item.Stack);
                payload[5] = (byte)item.Prefix;
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6), (short)item.Type);
                payload[8] = (byte)(item.Favorite ? 1 : 0);
                Check(bootstrap.OnFrame(Frame(5, payload)) == TerrariaFrameSinkResult.Continue, "literal packet5 queued");
            }
            Check(bootstrap.OnFrame(Frame(138, [])) == TerrariaFrameSinkResult.Continue, "literal empty138 queued");
            Check(queue.Commands.Last() is PlayerInventorySyncCompleteRuntimeCommand, "delimiter after all5");
            foreach (var command in queue.Commands.SkipLast(1)) Check(players.TryApply(command), "actual queued image dispatch");
            Check(players.TryApply(queue.Commands.Last()), "actual source138 dispatch");
            Assert.False(players.CanApplyShopSource(connection.Source));
            Assert.True(inventory.TryGet(connection, 58, out var beforeOwnedWork) && beforeOwnedWork.IsEmpty);
            Assert.Equal(100000, inventory.CaptureFundedCoinValues(connection).Sum());
            DrainOwnedShopWork(players, connection.Source);
            if(mode == 2) {
                Check(inventory.TryGet(connection,58,out var refused) && refused.IsEmpty,"missing correlated context refuses grant");
                Check(inventory.CaptureFundedCoinValues(connection).Sum()==100000,"missing correlated context refuses payment");
                continue;
            }
            if(mode == 3) {
                Check(inventory.TryGet(connection,58,out var indistinguishable) && indistinguishable.ItemType.Value==8 && indistinguishable.Stack==1,
                    "indistinguishable discount price has admitted WindowsTrue witness");
                Check(inventory.CaptureFundedCoinValues(connection).Sum()==100000+plan!.CopperDelta,
                    "indistinguishable source price debits exact witness");
                Check(member.ItemPhase!.Value.CapturedShopPriceContextMask==8 && member.ItemPhase.Value.CapturedClientUiPriceArithmeticMask==2,
                    "only compatible admitted pair survives; missing LinuxTrue is not invented");
                continue;
            }
            Check(inventory.TryGet(connection,58,out var paid) && paid.ItemType.Value==8 && paid.Stack==1,"opposite host/client platform grant");
            Check(inventory.TryGetFundedQuantity(connection,58,out var quantity) && quantity==1,"grant funded exactlyonce");
            long expected=100000-(clientWindows?151:152);
            Check(inventory.CaptureFundedCoinValues(connection).Sum()==expected,"source literal price debit");
            Check(member.ItemPhase!.Value.CapturedClientUiPriceArithmeticMask==(clientWindows?2:1),"price witness narrows existing player phase");
            Check(member.ItemPhase.Value.CapturedShopPriceContextMask==(clientWindows?4:1),"accepted exact context retained");
            Check(players.TrySetTalkNpc(connection,-1) && players.TrySetTalkNpc(connection,0),"next shop lifecycle");
            session = new RuntimeTownShopSession1458(0,new(17),RuntimeTownShopSessionKind1458.OrdinaryShop,
                1.01f,false,default,[new(0,new(8),150)],0){VendorHandle=session.VendorHandle,VendorStore=npcs};
            Check(membership.TrySetTownShopSession(connection,session) && players.TryBindTownShopQuote(connection,session,npcs),"next factory binds narrowed platform");
            Check(session.Quote!.TryCapture(out quote) && quote.CapturedPriceArithmeticMask==(clientWindows?2:1),"platform persists across shop sessions");
            Check(inventory.TryCopyInventory(connection,before),"next physical snapshot");
            tuple=before.Select(x=>x.IsEmpty?default:new WorldItemPickupSlot1458(x.ItemType.Value,x.Stack,x.Prefix.Value,(x.ItemFlags&1)!=0)).ToArray();
            var cursor=new ShopItem1458(tuple[58],quote.Stock40[0].Value,false,false);
            pricing=new ShopPriceContext1458(quote.PriceAdjustment,false,!clientWindows,new(false,false,false));
            Check(VanillaShopTransaction1458.TryPlanBuy(tuple,new WorldItemPickupSlot1458[160],quote.Stock40,quote.Memos,cursor,0,1,pricing,out plan) && plan is not null,"opposite platform attempted next trade");
            queue.Commands.Clear();
            for(int slot=0;slot<59;slot++) {
                if(tuple[slot]==plan!.Wallet.Inventory[slot])continue;
                var item=plan.Wallet.Inventory[slot];var payload=new byte[9];payload[0]=251;
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1),(short)slot);
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3),(short)item.Stack);payload[5]=(byte)item.Prefix;
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6),(short)item.Type);payload[8]=(byte)(item.Favorite?1:0);
                Check(bootstrap.OnFrame(Frame(5,payload))==TerrariaFrameSinkResult.Continue,"next literal5 queue");
            }
            Check(bootstrap.OnFrame(Frame(138,[]))==TerrariaFrameSinkResult.Continue,"next literal138 queue");
            foreach(var command in queue.Commands)Check(players.TryApply(command),"next typed dispatch");
            DrainOwnedShopWork(players, connection.Source);
            Check(inventory.TryGet(connection,58,out paid) && paid.Stack==1,"known platform flip cannot grant");
            Check(inventory.TryGetFundedQuantity(connection,58,out quantity) && quantity==1,"known platform flip cannot fund");
            Check(inventory.CaptureFundedCoinValues(connection).Sum()==expected,"known platform flip cannot debit");
        }
        Assert.Equal(195, checks);
    }

    // The unchanged literal138 seals the receipt; only the existing owned tick
    // hook advances its bounded matcher. Expected economic predicates remain below.
    private static void DrainOwnedShopWork(PlayerAuthority players, GameCommandSourceId source)
    {
        int ticks = 0;
        while (!players.CanApplyShopSource(source))
        {
            Assert.True(++ticks <= 100000, "sealed shop receipt must complete in bounded owned work");
            players.TickShopJournalWork();
            Assert.InRange(players.LastShopWorkSteps, 0, PlayerAuthority.ShopWorkStepsPerTick);
        }
    }

    private sealed class CommandQueue : IGameCommandIngress<RuntimeCommand>
    {
        public List<RuntimeCommand> Commands { get; } = [];
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { Commands.Add(command); return true; }
    }
    private sealed class ActualSpawn(PlayerAuthority players) : IPlayerSpawnCommitIngress
    {
        public ConnectionHandle Connection { get; private set; }
        public PlayerSpawnCommitRequest Request { get; private set; }
        public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request)
        {
            Request = request;
            Connection = new(source, session.Handle);
            return players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, request));
        }
    }
}
