using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;
using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class OwnedBankClosedExchange1458Tests
{
    [Fact]
    public void Full_capacity_bank_object_exchanges_require_complete_private_convergence()
    {
        VerifyOriginalFavoriteCycles();
        VerifyOriginalReservedCycles();
        foreach (int slot in new[] { 50, 54 })
            foreach (int funding in new[] { 0, 1 }) Verify(0, funding, 3, true, false, false, slot, false);
        foreach (int type in new[] { 23, 40, 71, 72 })
            foreach (int funding in new[] { 0, 1 }) Verify(0, funding, type, true, false, false, type is 71 or 72 ? 50 : 54);

        for (int bank = 0; bank < 4; bank++)
            foreach (int funding in new[] { -1, 0, 1, 2 }) Verify(bank, funding, 71, false);
        foreach (int funding in new[] { -1, 0, 1, 2 }) Verify(0, funding, 23, true);
        foreach (int bank in new[] { 0, 3 })
            foreach (int funding in new[] { 0, 1 }) Verify(bank, funding, 23, false);
        foreach (bool mainPair in new[] { true, false })
            foreach (bool favoriteA in new[] { false, true })
                foreach (int funding in new[] { 0, 1 }) Verify(3, funding, 23, mainPair, true, favoriteA);
    }

    private static void VerifyOriginalFavoriteCycles()
    {
        // Configured original ItemSlot calls, not a whole Main/UI scheduler.
        // Object origins are independent evidence: public tuple no-op is not
        // private custody no-op. Context4 clears favorites and is not closed.
        int rows = 0;
        foreach (string name in new[] { "BankFavoriteLinuxfirst1458", "BankFavoriteLinuxrepeat1458",
            "BankFavoriteWindowsfirst1458", "BankFavoriteWindowsrepeat1458" })
        {
            using var resource = typeof(OwnedBankClosedExchange1458Tests).Assembly.GetManifestResourceStream(name);
            Assert.NotNull(resource);
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                rows++;
                int context = row.GetProperty("context").GetInt32();
                Assert.True(context is 0 or 4 or 32);
                var before = row.GetProperty("before");
                var banks = row.GetProperty("beforeBanks");
                JsonElement Pair(JsonElement state, JsonElement bankImage) => context == 0
                    ? state.GetProperty("inventory") : bankImage[context == 4 ? 0 : 3];
                var pair = Pair(before, banks);
                var a = Tuple(pair[0]); var b = Tuple(pair[1]);
                var mouse = Tuple(row.GetProperty("beforeMouse"));
                Assert.Equal(a.Type, b.Type);
                Assert.Equal(a.Stack, row.GetProperty("maxStack").GetInt32());
                Assert.Equal(b.Stack, a.Stack);
                Assert.True(a.Favorite != b.Favorite);
                Assert.Equal(0, row.GetProperty("beforeOrigins").GetProperty("pair0").GetInt32());
                Assert.Equal(1, row.GetProperty("beforeOrigins").GetProperty("pair1").GetInt32());
                int stepIndex = 0;
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    int slot = step.GetProperty("slot").GetInt32();
                    var source = slot == 0 ? a : b;
                    bool planned = VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in mouse,
                        out var nextSlot, out var nextMouse, context);
                    if (stepIndex == 1 && context is 0 or 32) Assert.True(!planned);
                    var expectedPair = Pair(step.GetProperty("state"), step.GetProperty("banks"));
                    Assert.Equal(Tuple(expectedPair[slot]), nextSlot);
                    Assert.Equal(Tuple(step.GetProperty("mouse")), nextMouse);
                    if (slot == 0) a = nextSlot; else b = nextSlot;
                    mouse = nextMouse;
                    EqualJson(row.GetProperty("beforeRandom"), step.GetProperty("random"));
                    Assert.Equal(row.GetProperty("beforeNext").GetInt32(), step.GetProperty("next").GetInt32());
                    stepIndex++;
                }
                Assert.Equal(3, stepIndex);
                Assert.Equal(1, row.GetProperty("afterCycleOrigins").GetProperty("pair0").GetInt32());
                Assert.Equal(0, row.GetProperty("afterCycleOrigins").GetProperty("pair1").GetInt32());
                Assert.Equal(-1, row.GetProperty("afterCycleOrigins").GetProperty("mouse").GetInt32());
                Assert.Equal(0, row.GetProperty("operationFrames").GetArrayLength());
                EqualJson(row.GetProperty("beforeRandom"), row.GetProperty("afterCycleRandom"));
                EqualJson(row.GetProperty("beforeRandom"), row.GetProperty("afterSyncRandom"));
                Assert.Equal(row.GetProperty("beforeNext").GetInt32(), row.GetProperty("afterCycleNext").GetInt32());
                Assert.Equal(row.GetProperty("beforeNext").GetInt32(), row.GetProperty("afterSyncNext").GetInt32());
                if (context is 0 or 32)
                {
                    EqualJson(before, row.GetProperty("afterCycle"));
                    EqualJson(banks, row.GetProperty("afterCycleBanks"));
                    EqualJson(before, row.GetProperty("afterSync"));
                    EqualJson(banks, row.GetProperty("afterSyncBanks"));
                    Assert.Equal(0, row.GetProperty("syncFrames").GetArrayLength());
                }
                else
                {
                    Assert.True(!JsonElement.DeepEquals(banks, row.GetProperty("afterCycleBanks")));
                    Assert.True(!a.Favorite && !b.Favorite);
                    var frames = row.GetProperty("syncFrames");
                    Assert.Equal(2, frames.GetArrayLength());
                    byte[] frame = Convert.FromHexString(frames[0].GetString()!);
                    Assert.Equal(12, frame.Length); Assert.Equal((byte)5, frame[2]);
                    Assert.Equal(row.GetProperty("aFavorite").GetBoolean() ? 99 : 100,
                        System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(4)));
                    Assert.Equal((byte)0, frame[11]);
                    Assert.Equal("03008A", frames[1].GetString());
                }
            }
        }
        Assert.Equal(24, rows);

        static WorldItemPickupSlot1458 Tuple(JsonElement value) => value.GetProperty("type").GetInt32() == 0
            ? default : new(value.GetProperty("type").GetInt32(), value.GetProperty("stack").GetInt32(),
                value.GetProperty("prefix").GetInt32(), value.GetProperty("favorite").GetBoolean());
        static void EqualJson(JsonElement expected, JsonElement actual) => Assert.True(JsonElement.DeepEquals(expected, actual));
    }

    private static void VerifyOriginalReservedCycles()
    {
        int rows = 0;
        foreach (string name in new[] { "ReservedCycleLinuxfirst1458", "ReservedCycleLinuxrepeat1458",
            "ReservedCycleWindowsfirst1458", "ReservedCycleWindowsrepeat1458" })
        {
            using var resource = typeof(OwnedBankClosedExchange1458Tests).Assembly.GetManifestResourceStream(name);
            Assert.NotNull(resource);
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                rows++;
                int context = row.GetProperty("context").GetInt32();
                Assert.True(context is 0 or 1 or 2);
                int type = row.GetProperty("itemType").GetInt32();
                Assert.True(VanillaWorldItemPickupCatalog1458.TryGet(type, out var facts));
                Assert.Equal(row.GetProperty("fitsAmmoSlot").GetBoolean(), facts.FitsAmmoSlot);
                var inventory = row.GetProperty("before").GetProperty("inventory").EnumerateArray().Select(Tuple).ToArray();
                var mouse = Tuple(row.GetProperty("beforeMouse"));
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    int slot = step.GetProperty("slot").GetInt32();
                    bool right = step.GetProperty("right").GetBoolean();
                    var source = inventory[slot];
                    var nextSlot = source; var nextMouse = mouse;
                    // Context2's actual incoming FitsAmmoSlot gate is separate
                    // from its ordinary tuple transform. Right only withdraws.
                    bool allowed = context != 2 || right || mouse.Type == 0 ||
                        VanillaWorldItemPickupCatalog1458.TryGet(mouse.Type, out var incoming) && incoming.FitsAmmoSlot;
                    if (allowed)
                    {
                        int primitiveContext = context == 2 ? 0 : context;
                        bool planned = right
                            ? VanillaInventoryCursorMove1458.TryPlanToCursor(in source, in mouse, 1, out nextSlot, out nextMouse, primitiveContext)
                            : VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in mouse, out nextSlot, out nextMouse, primitiveContext);
                        if (!planned) { nextSlot = source; nextMouse = mouse; }
                    }
                    inventory[slot] = nextSlot; mouse = nextMouse;
                    var expected = step.GetProperty("state").GetProperty("inventory");
                    for (int i = 0; i < 59; i++) Assert.Equal(Tuple(expected[i]), inventory[i]);
                    Assert.Equal(Tuple(step.GetProperty("mouse")), mouse);
                    Assert.True(JsonElement.DeepEquals(row.GetProperty("beforeBanks"), step.GetProperty("banks")));
                    Assert.True(JsonElement.DeepEquals(row.GetProperty("beforeRandom"), step.GetProperty("random")));
                    Assert.Equal(row.GetProperty("beforeNext").GetInt32(), step.GetProperty("next").GetInt32());
                    foreach (var field in row.GetProperty("before").EnumerateObject())
                        if (field.Name != "inventory") Assert.True(JsonElement.DeepEquals(field.Value, step.GetProperty("state").GetProperty(field.Name)));
                }
                bool closed = context == 0 || context == 1 && type is 71 or 72 || context == 2 && facts.FitsAmmoSlot;
                Assert.Equal(closed, JsonElement.DeepEquals(row.GetProperty("before"), row.GetProperty("afterCycle")) &&
                    JsonElement.DeepEquals(row.GetProperty("beforeMouse"), row.GetProperty("afterCycleMouse")));
                Assert.Equal(0, row.GetProperty("operationFrames").GetArrayLength());
                var sync = row.GetProperty("afterSync").GetProperty("inventory");
                for (int i = 0; i < 59; i++) Assert.Equal(i == 58 ? mouse : inventory[i], Tuple(sync[i]));
                Assert.True(JsonElement.DeepEquals(row.GetProperty("beforeRandom"), row.GetProperty("afterSyncRandom")));
                Assert.Equal(row.GetProperty("beforeNext").GetInt32(), row.GetProperty("afterSyncNext").GetInt32());
                var frames = row.GetProperty("syncFrames");
                Assert.Equal(closed ? 0 : 4, frames.GetArrayLength());
                if (!closed)
                {
                    foreach (var frame in frames.EnumerateArray())
                    {
                        byte[] bytes = Convert.FromHexString(frame.GetString()!);
                        if (bytes[2] == 138) { Assert.Equal("03008A", frame.GetString()); continue; }
                        Assert.Equal((byte)5, bytes[2]); Assert.Equal(12, bytes.Length);
                        int slot = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4));
                        var item = Tuple(sync[slot]);
                        Assert.Equal(item.Stack, (int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(6)));
                        Assert.Equal(item.Type, (int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(9)));
                        Assert.Equal((byte)item.Prefix, bytes[8]); Assert.Equal((byte)0, bytes[11]);
                    }
                }
            }
        }
        Assert.Equal(40, rows);
        static WorldItemPickupSlot1458 Tuple(JsonElement value) => value.GetProperty("type").GetInt32() == 0 ? default :
            new(value.GetProperty("type").GetInt32(), value.GetProperty("stack").GetInt32(), value.GetProperty("prefix").GetInt32(), value.GetProperty("favorite").GetBoolean());
    }

    private static void Verify(int bank, int funding, int storedType, bool mainPair, bool differingFavorites = false, bool favoriteA = false, int pairSlot = 1, bool expectAmbiguity = true)
    {
        // Source Left: take A, exchange full identical B, return A. The
        // middle public tuples remain identical while object custody swaps.
        // Currency cases use bank4/32; ordinary DoCoins is not assumed inert.
        var sessions = new List<PlayerJoinSession>();
        try
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++)
                tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var state = new ServerRuntimeState(worldTiles: tiles, projectiles: new RuntimeProjectileStore(),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
                playerUpdateRandomSeed: new(0));
            var players = Composition(state).Players;
            var pool = new PlayerSlotPool(3);
            for (int slot = 0; slot < 3; slot++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new PlayerJoinSession(lease!));
            }
            var session = sessions[2];
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99002), session.Handle);
            void Apply(RuntimeCommand command) => state.Apply(command);
            void Drain()
            {
                int ticks = 0;
                while (!players.CanApplyShopSource(connection.Source) && ticks++ < 2000)
                {
                    state.Tick();
                    Assert.True(players.LastShopWorkSteps is >= 0 and <= 64);
                }
                Assert.True(ticks < 2000);
            }
            Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Apply(new PlayerMovementRuntimeCommand(connection, new(session.Slot, 0, 16, 0, 64,
                0, 1680, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            short[] wireStarts = [99, 299, 500, 700];
            int bankStart = 59 + bank * 40;
            int target = bankStart + (mainPair ? 0 : 2);
            short targetWire = (short)(wireStarts[bank] + (mainPair ? 0 : 2));
            Assert.True(TerraRuntime.Gameplay.Items.VanillaWorldItemAllocationCatalog1458.TryGet(storedType, out var facts));
            short full = pairSlot == 1 ? checked((short)facts.MaximumStack) : (short)2;
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 1, 0, 9, 0)));
            for (short slot = 0; slot < 2; slot++)
                Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot,
                    mainPair ? (short)(pairSlot + slot) : (short)(wireStarts[bank] + slot), full, 0, (short)storedType, differingFavorites && (slot == 0 ? favoriteA : !favoriteA) ? (byte)1 : (byte)0)));
            Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            Drain();
            var store = Inventory(players);
            var quantity = new short[59]; quantity[0] = 1;
            if (mainPair)
            {
                quantity[pairSlot] = funding == 2 ? full : funding == 0 ? (short)1 : (short)0;
                quantity[pairSlot + 1] = funding == 2 ? full : funding == 1 ? (short)1 : (short)0;
            }
            var mainCopper = new long[59];
            if (mainPair && storedType is 71 or 72)
            {
                mainCopper[pairSlot] = funding == 0 ? 1 : 0;
                mainCopper[pairSlot + 1] = funding == 1 ? 1 : 0;
                quantity[pairSlot] = quantity[pairSlot + 1] = 0;
            }
            store.AdoptFundedQuantities(connection, quantity, mainCopper);
            int offset = connection.Player.Slot.Value * 160 + bank * 40;
            if (!mainPair && storedType == 71)
            {
                BankCopper(store)[offset] = funding == 2 ? full : funding == 0 ? 1 : 0;
                BankCopper(store)[offset + 1] = funding == 2 ? full : funding == 1 ? 1 : 0;
            }
            else if (!mainPair)
            {
                BankQuantity(store)[offset] = funding == 2 ? full : funding == 0 ? (short)1 : (short)0;
                BankQuantity(store)[offset + 1] = funding == 2 ? full : funding == 1 ? (short)1 : (short)0;
            }
            Assert.True(store.TryCaptureShopJournal(connection, out var before));
            Assert.NotNull(before);
            if (mainPair && pairSlot == 1 && storedType == 23 && funding == 0 && !differingFavorites)
            {
                // Configured detached role guard, not a positive original UI
                // history: slots50..57 use coin/ammo contexts, never context0.
                foreach (int reserved in new[] { 50, 54 })
                {
                    var image = (RuntimePlayerInventoryItem[])before.Before.Clone();
                    var funded = before.Quantity;
                    image[reserved] = image[1]; image[reserved + 1] = image[2];
                    image[1] = image[2] = default;
                    funded[reserved] = funded[1]; funded[reserved + 1] = funded[2];
                    funded[1] = funded[2] = 0;
                    var resolution = new RuntimePlayerInventoryStore.BankBulkResolution();
                    foreach (int _ in ClosedWitnesses(store, before, image, before.Copper, funded,
                        before.Caps, 0, 0, resolution)) { }
                    Assert.True(!resolution.Ambiguous);
                }
            }
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, targetWire, 1, 0, 9, 0)));
            Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            Drain();
            Assert.True(store.TryCaptureShopJournal(connection, out var after));
            Assert.NotNull(after);
            bool ambiguous = expectAmbiguity && funding is 0 or 1;
            Assert.Equal(ambiguous ? 0 : 1, after.Quantity[target]);
            if (ambiguous) Assert.True(after.Before[target].IsEmpty);
            else
            {
                Assert.Equal(9, after.Before[target].ItemType.Value);
                Assert.Equal(1, after.Before[target].Stack);
            }
            Assert.True(after.Before[0].IsEmpty);
            Assert.True(after.Before[58].IsEmpty);
            for (int slot = 0; slot < 219; slot++)
            {
                if (slot != 0 && slot != target) Assert.Equal(before.Before[slot], after.Before[slot]);
                Assert.Equal(before.Copper[slot], after.Copper[slot]);
                if (slot != 0 && slot != target) Assert.Equal(before.Quantity[slot], after.Quantity[slot]);
            }
            // Caps remain the existing original->final lane policy. They are
            // never swapped with a source object or pooled across destinations.
            for (int lane = 0; lane < 219 * RuntimePlayerInventoryStore.QuantityLaneCount; lane++) Assert.Equal(before.Caps[lane], after.Caps[lane]);
        }
        finally
        {
            foreach (var session in sessions) session.Dispose();
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "PrepareClosedCursorWitnesses")]
    private static extern IEnumerable<int> ClosedWitnesses(RuntimePlayerInventoryStore store,
        RuntimePlayerInventoryStore.ShopJournalCapture capture, RuntimePlayerInventoryItem[] accepted,
        long[] copper, short[] quantity, short[] caps, int animation, int itemTime,
        RuntimePlayerInventoryStore.BankBulkResolution result);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "shopBankFundedCopper")]
    private static extern ref long[] BankCopper(RuntimePlayerInventoryStore owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "shopBankFundedQuantities")]
    private static extern ref short[] BankQuantity(RuntimePlayerInventoryStore owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState owner);
}
