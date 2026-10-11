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
public sealed class RetainedNpcShopLifecycle1458Tests
{
    [Fact]
    public void Actual_factory_and_lifecycle_preserve_quote_history_and_retire_custody()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; Assert.True(value, label); }
        T Owner<T>(PlayerAuthority player, string field) => (T)typeof(PlayerAuthority).GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
        ConnectionHandle Connection() => new(GameCommandSourceId.FromConnection(708), new(new(0), new(1)));
        for (int mode = 0; mode < 9; mode++)
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            var players = new PlayerAuthority(null, tiles);
            players.SetPlayerUpdateRandom(new(0));
            players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
            players.BindReceiveEquipmentRandom(new(0), false, new(false, false, false));
            var connection = Connection();
            var membership = Owner<RuntimePlayerMembership>(players, "membership");
            var inventory = Owner<RuntimePlayerInventoryStore>(players, "inventory");
            var member = new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
                Revision = 1, HasHealth = true, Life = 100, MaxLife = 100, PositionX = 100, PositionY = 100 };
            membership.Commit(member);
            Check(inventory.TryAttach(connection), "attach");
            if (mode == 6) member.ItemPhase = null;
            else if (mode == 7) member.ItemPhase = member.ItemPhase!.Value with {
                DerivedCombat = VanillaPlayerCombatSnapshot.Baseline with { DiscountEquipped = true } };
            else if (mode == 8) member.ItemPhase = member.ItemPhase!.Value with { DiscountAvailable = true };
            var npcs = new RuntimeNpcStore();
            var state = new NpcStateUpdate(17, 17, 100, 100, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = 250, LifeMax = 250 });
            Check(npcs.TrySpawn(0, in state, out var vendor), "vendor");
            VanillaTownShopEntry1458[] offers = mode switch {
                1 => [new(0, new(2623))], // Verified variant has a known inactive world context.
                2 => [new(0, new(24)), new(0, new(8))],
                3 => [new(40, new(24))],
                4 => [new(0, new(24))],
                5 => [new(0, new(24)), new(1, new(3816), 5, VanillaTownShopCurrency1458.DefenderMedals)],
                _ => [new(0, new(226)), new(1, new(24))]
            };
            var session = new RuntimeTownShopSession1458(0, new(17), RuntimeTownShopSessionKind1458.OrdinaryShop,
                1, false, default, offers, mode == 4 ? RuntimeTownCommerceMissingFacts1458.LiveWeather : 0)
                { VendorHandle = vendor.Handle, VendorStore = npcs };
            Check(membership.TrySetTalkNpc(connection, 0), "talk");
            Check(membership.TrySetTownShopSession(connection, session), "session");
            bool bound = players.TryBindTownShopQuote(connection, session, npcs);
            Check(bound == (mode is not (2 or 3 or 4 or 6)), "factory admission");
            if (!bound) { Check(session.Quote is null, "failed factory has no residue"); continue; }
            Check(session.Quote!.TryCapture(out var quote), "source quote");
            Check(quote.Stock40.Length == 40 && quote.Memos.Length == 0, "forty stock, fresh memo");
            Check(!member.ItemPhase!.Value.ShopOpen, "quote does not invent Main.OpenShop");
            Check(quote.DiscountMask == (mode == 7 ? 3 : mode == 8 ? 2 : 1), "discount alternatives");
            foreach (var offer in offers) {
                var item = quote.Stock40[offer.Slot];
                Check(item.Tuple.Stack == 1 && item.Tuple.Prefix == 0 && !item.Tuple.Favorite && !item.BuyOnce && item.IsShopItem,
                    "source SetupShop offer shape");
                Check(item.Currency == (offer.Currency == VanillaTownShopCurrency1458.Coins ? null : 0), "source currency registration");
            }
            if (mode == 0) Check(quote.Stock40[0].Tuple.Type == 227, "source SKU alias canonicalization");
            Check(!players.TryBindTownShopQuote(connection, session, npcs), "bind once");
        }
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            Check(tiles.TryAttachWorldSurface(80), "Town surface");
            var players = new PlayerAuthority(null, tiles);
            players.SetPlayerUpdateRandom(new(0));
            players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
            players.BindReceiveEquipmentRandom(new(0), false, new(false, false, false));
            var connection = Connection();
            var membership = Owner<RuntimePlayerMembership>(players, "membership");
            var inventory = Owner<RuntimePlayerInventoryStore>(players, "inventory");
            membership.Commit(new RuntimePlayerMember { Connection = connection, Slot = connection.Player.Slot,
                Revision = 1, HasHealth = true, Life = 100, MaxLife = 100, PositionX = 100, PositionY = 100 });
            Check(inventory.TryAttach(connection), "Town attach");
            var npcs = new RuntimeNpcStore();
            var state = new NpcStateUpdate(17, 17, 100, 100, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = 250, LifeMax = 250 });
            Check(npcs.TrySpawn(0, in state, out _), "Town vendor");
            var facts = default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80, RockLayer = 120,
                InitialDayTime = true, GameMode = 0, SpawnTileY = 20 };
            var town = new TownNpcAuthority(players, npcs, new RuntimeProjectileStore(), tiles,
                new RuntimeWorldProgressionMutations(), null, null, facts, null, null, false, false, null, false, false);
            var clock = new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 1);
            town.ApplyTalk(connection, 0, clock);
            Check(membership.TryGetTownShopSession(connection.Player, out var session) && session is not null,
                "actual Town.ApplyTalk creates session");
            Check(session!.MissingFacts == 0, "actual owned merchant facts complete");
            Check(session.Quote is not null && session.Quote.TryCapture(out _), "actual Town.ApplyTalk binds production quote");
            Check(membership.TryGet(connection, out var member) && member.ItemPhase!.Value.ShopOpen == false,
                "actual talk leaves UI open unknown");
            town.ApplyTalk(connection, -1, clock);
            Check(!membership.TryGetTownShopSession(connection.Player, out _), "actual close retires session");
            Check(membership.TrySetTalkNpc(connection, 0) && membership.TrySetTownShopSession(connection, session),
                "explicit same-object session rebind fixture");
            Check(!session.Quote!.TryCapture(out _), "removed quote cannot be revived without a pending receipt");
        }
        for (int closeMode = 0; closeMode < 11; closeMode++)
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            Check(tiles.TryAttachWorldSurface(80), "end to end surface");
            var players = new PlayerAuthority(null, tiles);
            players.SetPlayerUpdateRandom(new(0));
            players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
            players.BindReceiveEquipmentRandom(new(0), false, new(false, false, false));
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
            Check(session!.Quote!.TryCapture(out var quote), "end to end quote capture");
            int torchSlot = Array.FindIndex(quote.Stock40, x => x.Tuple.Type == 8);
            Check(torchSlot >= 0, "merchant actually offers torch");
            var before = new RuntimePlayerInventoryItem[59];
            Check(inventory.TryCopyInventory(connection, before), "capture configured wallet");
            var tuple = before.Select(x => x.IsEmpty ? default : new WorldItemPickupSlot1458(x.ItemType.Value,
                x.Stack, x.Prefix.Value, (x.ItemFlags & 1) != 0)).ToArray();
            var pricing = new ShopPriceContext1458(quote.PriceAdjustment, false, false, new(false, false, false));
            Check(VanillaShopTransaction1458.TryPlanBuy(tuple, new WorldItemPickupSlot1458[160], quote.Stock40,
                quote.Memos, default, torchSlot, 1, pricing, out var plan) && plan is not null, "bounded torch plan");
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
            if (closeMode is 7 or 8) {
                Check(session.Quote.TryObserveDiscount((byte)2), "in-flight equipment discount observed");
                Check(session.Quote.TryCapture(out var retainedPrice) && retainedPrice.Revision == quote.Revision &&
                    retainedPrice.DiscountMask == quote.DiscountMask, "first image price history stays frozen until delimiter");
                if (closeMode == 8) Check(inventory.TrySet(connection, new(connection.Player.Slot, 49, 1, 0, 9, 0)),
                    "non-economic journal invalidation retains later price history");
            }
            if (closeMode is >= 1 and <= 6) {
                town.ApplyTalk(connection, -1, new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 1));
                Check(!session.Quote.TryCapture(out _) && !session.Quote.TryObserveDiscount(true),
                    "retired quote cannot supply new capture or discount observation");
                if (closeMode == 2) Check(players.TryApply(queue.Commands.SkipLast(1).Last()), "post-close image explicitly refused");
                if (closeMode == 3) town.ApplyTalk(connection, 0, new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 1));
                if (closeMode == 4) {
                    Check(npcs.TryDespawn(session.VendorHandle), "retired vendor despawn");
                    Check(npcs.TrySpawn(0, in state, out _), "retired vendor same slot replacement");
                }
                if (closeMode == 5) Check(member.TryAdvanceRevision(), "retired actor/input changed");
                if (closeMode == 6) Check(inventory.TrySet(connection, new(connection.Player.Slot, 49, 1, 0, 9, 0)),
                    "retired inventory currentness changed");
            }
            if (closeMode == 9) Check(players.TryApply(new PlayerRespawnRuntimeCommand(connection, spawn.Request)),
                "actual respawn command closes existing shop boundary");
            if (closeMode == 10) { member.IsDead = true; players.TickItemAnimation(); }
            if (closeMode >= 9) {
                Check(!membership.TryGetTownShopSession(connection.Player, out _), "lifecycle session removed");
                Check(member.ItemPhase!.Value.CapturedShopDiscountStates == 5,
                    "respawn and dead tick preserve possible UI closure through shared boundary");
                Check(membership.TrySetTalkNpc(connection, 0) && membership.TrySetTownShopSession(connection, session),
                    "lifecycle same-session rebind fixture");
                Check(!session.Quote.TryCapture(out _), "lifecycle retired quote cannot revive");
                Check(membership.TrySetTalkNpc(connection, -1), "restore lifecycle NoNpc fixture");
            }
            Check(players.TryApply(queue.Commands.Last()), "actual delimiter dispatch");
            DrainOwnedShopWork(players, connection.Source);
            if (closeMode is >= 2 and <= 6 || closeMode >= 8) {
                Check(inventory.TryGet(connection, 58, out var refusedCursor) && refusedCursor.IsEmpty,
                    "invalid retired receipt cannot grant cursor");
                Check(inventory.TryGetFundedQuantity(connection, 58, out var refusedFunded) && refusedFunded == 0,
                    "invalid retired receipt cannot fund cursor");
                Check(inventory.CaptureFundedCoinValues(connection).Sum() == 100_000,
                    "invalid retired receipt cannot charge wallet");
                if (closeMode == 8) {
                    Check(session.Quote.TryCapture(out var deferred) && deferred.DiscountMask == 3,
                        "failed journal preserves equipment price history for future trades");
                    town.ApplyTalk(connection, -1, new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 1));
                    Check(member.ItemPhase!.Value.DiscountAvailableMask == 3,
                        "unpaid hidden opening history survives conversation close");
                    town.ApplyTalk(connection, 0, new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 1));
                    Check(membership.TryGetTownShopSession(connection.Player, out var nextSession) &&
                        nextSession!.Quote!.TryCapture(out var nextQuote) && nextQuote.DiscountMask == 3,
                        "next actual Town quote consumes correlated availability alternatives");
                }
                continue;
            }
            Check(inventory.TryGet(connection, 58, out var cursor) && cursor.ItemType.Value == 8 && cursor.Stack == 1,
                "bootstrap talk factory journal grant");
            Check(inventory.TryGetFundedQuantity(connection, 58, out var funded) && funded == 1, "grant funded once");
            Check(inventory.CaptureFundedCoinValues(connection).Sum() == 100_000 + plan!.CopperDelta,
                "exact configured price charged");
            Check(member.ItemPhase!.Value.CapturedShopDiscountStates == (closeMode == 1 ? 5 : 4),
                "paid witness projects false latch; retired close preserves correlated open/closed states");
            if (closeMode is 0 or 7) Check(session.Quote.TryCapture(out quote) && quote.Memos.Any(x => x.Type == 8 && x.Stack == 1),
                "accepted source sellback memo");
            else Check(!session.Quote.TryCapture(out _), "successful retired drain never reopens quote");
            Check(players.TryApply(queue.Commands.Last()) && inventory.TryGetFundedQuantity(connection, 58, out funded) && funded == 1,
                "duplicate real delimiter cannot pay or grant twice");
        }
        Assert.Equal(439, checks);
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
