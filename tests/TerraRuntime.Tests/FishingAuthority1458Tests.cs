using System.Buffers;
using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Real_cast_pond_bite_pull_and_return_pay_bait_once_and_preserve_cancellation_boundaries()
    {
        using (var f = new FishingFixture(12))
        {
            bool rewarded = false;
            for (int cast = 0; cast < 6 && !rewarded; cast++)
            {
                var birth = f.Cast();
                var bite = f.WaitForBite();
                int item = bite.Result.ItemType;
                Assert.True(item > 0);
                Assert.Equal(0, f.Count(item));
                long births = f.Authority.PromotedClientProjectileSpawns;
                f.Report(f.BirthPacket with { Ai1 = item, Ai0 = 1 });
                Assert.Equal(bite, f.Authority.CaptureFishingBobber(f.Connection));
                Assert.Equal(0, f.Count(item));
                Assert.Equal(births, f.Authority.PromotedClientProjectileSpawns);
                f.Pull();
                var returning = f.Authority.CaptureFishingBobber(f.Connection);
                Assert.NotNull(returning); Assert.True(returning.Returning);
                short paidBait = f.Item(f.BaitSlot).Stack;
                f.Control(true); f.Control(true);
                f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection,
                    new(f.BirthPacket.Key, birth.PositionX, birth.PositionY)));
                Assert.Equal(paidBait, f.Item(f.BaitSlot).Stack);
                f.Return();
                int quantity = f.Count(item);
                if (returning.PaidCatch) { Assert.True(quantity > 0); rewarded = true; }
                else Assert.Equal(0, quantity);
                for (int tick = 0; tick < 10; tick++) f.Tick();
                f.Report(f.BirthPacket with { Ai0 = 1, Ai1 = item });
                f.Pull();
                Assert.Equal(quantity, f.Count(item));
                Assert.Equal(paidBait, f.Item(f.BaitSlot).Stack);
            }
            Assert.True(rewarded);
        }
        using (var f = new FishingFixture(12))
        {
            bool switchedPaidCatch = false;
            for (int cast = 0; cast < 6 && !switchedPaidCatch; cast++)
            {
                f.Cast(); var bite = f.WaitForBite(); f.Pull();
                if (f.Authority.CaptureFishingBobber(f.Connection) is not { PaidCatch: true })
                { f.Return(); continue; }
                int item = bite.Result.ItemType;
                f.Control(false, 1); f.Tick();
                Assert.Null(f.Authority.CaptureFishingBobber(f.Connection));
                Assert.True(f.Count(item) > 0);
                int once = f.Count(item);
                f.Report(f.BirthPacket with { Ai0 = 1, Ai1 = item });
                for (int tick = 0; tick < 10; tick++) f.Tick();
                Assert.Equal(once, f.Count(item));
                switchedPaidCatch = true;
            }
            Assert.True(switchedPaidCatch);
        }
        using (var f = new FishingFixture(1))
        {
            for (int cast = 0; cast < 16 && f.Item(f.BaitSlot).Stack > 0; cast++)
            {
                f.Cast(); f.WaitForBite(); f.Pull(); f.Return();
            }
            Assert.True(f.Item(f.BaitSlot).IsEmpty);
            Assert.Equal(0, f.Funded(f.BaitSlot));
            var paidInventory = f.InventoryImage();
            f.Cast();
            for (int tick = 0; tick < 1000; tick++) f.Tick();
            Assert.False(f.Authority.CaptureFishingBobber(f.Connection)?.Result.HasBite ?? false);
            f.Pull(); f.Return();
            Assert.Equal(paidInventory, f.InventoryImage());
        }
        foreach (string cancellation in new[] { "switch", "death", "disconnect", "pulley" })
        {
            using var f = new FishingFixture(1);
            f.Cast(); var bite = f.WaitForBite(); int item = bite.Result.ItemType;
            short bait = f.Item(f.BaitSlot).Stack;
            if (cancellation == "switch") f.Control(false, 1);
            else if (cancellation == "death") f.Apply(new PlayerHealthRuntimeCommand(f.Connection,
                new(f.Connection.Player.Slot, 0, 400)));
            else if (cancellation == "disconnect") f.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
            else f.Control(false, 0, true);
            f.Tick();
            Assert.Null(f.Authority.CaptureFishingBobber(f.Connection));
            Assert.Equal(0, f.Count(item));
            if (cancellation != "disconnect") Assert.Equal(bait, f.Item(f.BaitSlot).Stack);
        }
    }

    internal sealed class FishingFixture : IDisposable
    {
        // Keep this real liquid pond above the source Underworld evaporation
        // band; a300-row synthetic world puts row100 inside that band.
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 600));
        internal readonly RuntimeProjectileReplicationRegistry Replication = new();
        internal readonly RuntimeWorldItemReplicationRegistry ItemReplication = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(7);
        internal readonly RuntimeProjectileStore Shots;
        internal readonly RuntimeWorldItemStore Items;
        internal readonly ServerRuntimeState State;
        internal readonly ConnectionHandle Connection;
        private readonly PlayerSlotPool pool = new(1);
        private readonly PlayerJoinSession session;
        private ushort identity;
        private readonly FishingPole1458 pole;
        internal TerrariaProjectileUpdateState BirthPacket;
        internal readonly short BaitSlot;
        internal ServerRuntimeComposition Runtime => Composition(State);
        internal PlayerAuthority Players => Runtime.Players;
        internal ProjectileAuthority Authority => Runtime.Projectiles;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(Connection, out var member)); return member; } }
        internal FishingFixture(short bait, short baitType = 2674,
            WorldLiquidKind liquid = WorldLiquidKind.Water, short poleType = 2294,
            RuntimeWorldClock? worldClock = null, bool importedBait = false,
            IReadOnlyDictionary<short, RuntimePlayerInventoryItem>? initialItems = null)
        {
            Assert.True(FishingRules1458.TryGetPole(poleType, out pole));
            for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            for (int x = 112; x <= 146; x++)
                for (int y = 100; y <= 116; y++)
                    Tiles.Set(x, y, new WorldTile { LiquidAmount = y == 100 ? (byte)128 : (byte)255, LiquidKind = liquid });
            for (int y = 100; y <= 117; y++)
                foreach (int x in new[] { 111, 147 }) Tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            for (int x = 111; x <= 147; x++) Tiles.Set(x, 117, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Shots = new(1001, Replication); Items = new(ItemReplication);
            State = new(worldTiles: Tiles, worldItems: Items, projectiles: Shots,
                worldItemReplication: ItemReplication, projectileReplication: Replication,
                playerEvents: new RuntimePlayerEventFanout(ItemReplication, Replication),
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random),
                worldClock: worldClock ?? new RuntimeWorldClock(10000, true, default, 0, 1, freezeWind: true, freezeRain: true),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80, RockLayer = 180 },
                itemPrefixWorld: new(false, false, false),
                playerUpdateRandomSeed: new(0), sourcePlayerWorld: MiningSourceScene1458.Facts(Tiles));
            // Both production streams keep their real owners. An explicit
            // initial seed makes the source-derived catch matrix reproducible.
            DropRandom(Authority).SetSeed(1458);
            ToolPhaseWorldFixture1458.BindFreshInactiveEvent(State, Tiles);
            Assert.True(pool.TryAcquireConnection(out var lease)); session = new(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99400), session.Handle);
            Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 200, 200)));
            for (short slot = 0; slot < 59; slot++)
            {
                short type = slot == 0 ? poleType : slot == 1 ? (short)1 : (short)0;
                var item = new RuntimePlayerInventoryItem(new(type), type == 0 ? (short)0 : (short)1, default, 0);
                if (importedBait && slot == 54) item = new(new(baitType), bait, default, 0);
                if (initialItems is not null && initialItems.TryGetValue(slot, out var supplied)) item = supplied;
                Apply(new PlayerEquipmentRuntimeCommand(Connection, item.ToCommitRequest(session.Slot, slot)));
            }
            Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Apply(new PlayerInventorySyncCompleteRuntimeCommand(Connection));
            Apply(new PlayerBuffTypesRuntimeCommand(Connection, new(Connection.Player.Slot, Array.Empty<BuffTypeId>())));
            Control(false); Tick();
            Assert.NotNull(Member.ItemPhase);
            Assert.True(Players.HasOwnedPickupContext);
            if (importedBait)
            {
                BaitSlot = 54;
                Assert.Equal(bait, Item(BaitSlot).Stack);
                Assert.Equal(0, Funded(BaitSlot));
                return;
            }
            Assert.True(Items.TryAllocateSourceDrop(new(Member.PositionX, Member.PositionY, 0, 0, bait, 0,
                WorldItemOwnershipMode.None, baitType, false, 0, 0), default, out var sourceBait, out _));
            Assert.True(Items.TryApplyOwner(sourceBait.Handle.Slot,
                new(Connection.Player.Slot.Value, 15, 1, 0, Member.PositionX, Member.PositionY), out sourceBait));
            Assert.True(Items.TryGetFundedQuantity(sourceBait, out int fundedBait));
            Assert.Equal(bait, fundedBait);
            Apply(new WorldItemRemoveRuntimeCommand(Connection, sourceBait.Handle));
            Assert.False(Items.TryGetActive(sourceBait.Handle.Slot, out _));
            BaitSlot = Enumerable.Range(0, 58).Select(i => (short)i).Single(slot => Item(slot).ItemType.Value == baitType);
            Assert.Equal(bait, Funded(BaitSlot));
        }
        internal void Apply(RuntimeCommand command) => State.Apply(command);
        internal void Tick() => State.Tick();
        internal void Control(bool use, byte selected = 0, bool pulley = false)
        {
            var member = Member;
            Apply(new PlayerMovementRuntimeCommand(Connection,
                new(Connection.Player.Slot, use ? (byte)96 : (byte)64, pulley ? (byte)17 : (byte)16,
                    0, 64, selected, member.PositionX, member.PositionY,
                    false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal void Report(TerrariaProjectileUpdateState packet)
        {
            // Exercise the actual packet27 encoder/frame/typed decoder boundary.
            var frame = MiningSourceScene1458.ObservationFrame(new ClientProjectileUpdateRuntimeCommand(Connection, packet));
            Assert.Equal(TerrariaProjectileDecodeResult.Decoded, TerrariaProjectileDecoder.TryDecodeUpdate(frame, out var decoded));
            Apply(new ClientProjectileUpdateRuntimeCommand(Connection, decoded));
        }
        internal ProjectileSnapshot Cast()
        {
            Control(false);
            for (int tick = 0; tick < 20; tick++) Tick();
            Control(true);
            var member = Member;
            BirthPacket = new(new(Connection.Player.Slot.Value, ++identity, 1), pole.ProjectileType,
                member.PositionX + 3, member.PositionY + 14, pole.ShootSpeed * .8f, pole.ShootSpeed * -.6f,
                0, 0, 0, 0, 0, 0, 0);
            Report(BirthPacket); Tick(); Control(false);
            var bobber = Authority.CaptureFishingBobber(Connection);
            Assert.NotNull(bobber); Assert.True(Shots.IsCombatTrusted(bobber.Snapshot.Handle));
            return bobber.Snapshot;
        }
        internal FishingBobber1458 WaitForBite(int maximumTicks = 4000)
        {
            bool wet = false;
            for (int tick = 0; tick < maximumTicks; tick++)
            {
                Tick(); var bobber = Authority.CaptureFishingBobber(Connection); Assert.NotNull(bobber);
                Assert.True(Shots.TryGetLifecycle(bobber.Snapshot.Handle, out var life)); wet |= life.Liquid.Wet;
                if (bobber.BiteClock < 0 && bobber.Result.HasBite)
                { Assert.True(wet); return bobber; }
            }
            throw new InvalidOperationException("Genuine cast never produced a bite in the owned pond.");
        }
        internal void Pull() { Control(false); Control(true); Tick(); Control(false); }
        internal void Return()
        {
            for (int tick = 0; tick < 500; tick++)
            { Tick(); if (Authority.CaptureFishingBobber(Connection) is null) return; }
            throw new InvalidOperationException("Pulled bobber did not return.");
        }
        internal RuntimePlayerInventoryItem Item(short slot)
        { Assert.True(Players.TryGetInventoryItem(Connection, slot, out var item)); return item; }
        internal int Funded(short slot)
        { Assert.True(Inventory(Players).TryGetFundedQuantity(Connection, slot, out short funded)); return funded; }
        internal int Count(int type)
        {
            int sum = 0;
            if (Players.TryGet(Connection, out _))
                for (short i = 0; i < 59; i++) { var item = Item(i); if (item.ItemType.Value == type) sum += item.Stack; }
            var items = new WorldItemSnapshot[Items.Capacity]; int count = Items.CopyActive(items);
            for (int i = 0; i < count; i++) if (items[i].ItemNetId == type) sum += items[i].Stack;
            return sum;
        }
        internal string InventoryImage() => string.Join(";", Enumerable.Range(0, 59).Select(i => Item((short)i).ToString()));
        public void Dispose() => session.Dispose();
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState state);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority players);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "fishingDropRandom")]
    private static extern ref VanillaUnifiedRandom1458 DropRandom(ProjectileAuthority projectiles);
}
