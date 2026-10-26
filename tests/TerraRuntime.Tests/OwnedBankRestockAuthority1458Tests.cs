using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Text.Json;
using System.Buffers;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class OwnedBankRestockAuthority1458Tests
{
    [Xunit.Fact]
    public void Original_restock_reports_preserve_full_custody_through_owned_work()
    {
        VerifyAmbiguousAmmoFundingIsRejected();
        VerifyPartialFundingAndCaps();
        // Original numeric component effects, translated through actual5/138 and
        // direct owned work. Initial funding is configured runtime policy.
        int checks = 0, rows = 0, slices = 0, maximumWork = 0; var failures = new List<string>();
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
        int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
        RuntimePlayerInventoryItem Item(JsonElement x) => x[0].GetInt32() == 0 || x[1].GetInt32() <= 0 ? default : new(new(x[0].GetInt32()), checked((short)x[1].GetInt32()), new(x[2].GetInt32()), x[3].GetBoolean() ? (byte)1 : (byte)0);
        for (int platform = 0; platform < 2; platform++)
        {
            using var input = typeof(OwnedBankRestockAuthority1458Tests).Assembly.GetManifestResourceStream("BankRestockSource1458")!;
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gz);
            var document = source.RootElement.GetProperty(platform == 0 ? "linux" : "windows");
            foreach (var row in document.GetProperty("rows").EnumerateArray())
            {
                if (row.GetProperty("spec").GetProperty("op").GetString() != "restock") continue;
                rows++;
                try
                {
                    string name = row.GetProperty("spec").GetProperty("name").GetString()!;
                    var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
                    var spec = row.GetProperty("spec");
                    var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
                    players.SetPlayerUpdateRandom(new(0));
                    players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
                    players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
                    var memberOwner = Membership(players);
                    var items = Inventory(players);
                    var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
                    var member = new RuntimePlayerMember
                    {
                        Connection = connection,
                        Slot = connection.Player.Slot,
                        Revision = 1,
                        HasHealth = true,
                        Life = 400,
                        MaxLife = 400,
                        ItemAnimation = I(before, "animation")
                    };
                    member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = I(before, "itemTime") } };
                    memberOwner.Commit(member); Check(items.TryAttach(connection), name + " attach");
                    var image = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
                    image[58] = Item(before.GetProperty("mouse"));
                    var q = new short[59]; var money = new long[59];
                    for (short i = 0; i < 59; i++)
                    {
                        Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot, i)), name + " initial inventory");
                        if (VanillaCoinFacts.TryGetValue(image[i].ItemType, out long unit)) money[i] = unit * Math.Max(0, (int)image[i].Stack);
                        else q[i] = Math.Max((short)0, image[i].Stack);
                    }
                    items.AdoptFundedQuantities(connection, q, money);
                    var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(Item).ToArray();
                    var bankCopper = new long[160]; var bankQuantity = new short[160]; var bankCaps = new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount]; Array.Fill(bankCaps, (short)-1);
                    for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType, out long unit)) bankCopper[i] = unit * Math.Max(0, (int)bankImage[i].Stack);
                    for (int i = 0; i < 160; i++) if (bankCopper[i] == 0) bankQuantity[i] = Math.Max((short)0, bankImage[i].Stack);
                    Check(items.TryAdoptShopBanks(connection, bankImage, bankCopper, bankQuantity, bankCaps), name + " configured banks");
                    // Another actor advances the GLOBAL serial after player0's last own stamp.
                    // The first cursor report must still retain the source-valid bank alternative.
                    var other = new ConnectionHandle(GameCommandSourceId.FromConnection(90002), new(new(1), new(1)));
                    memberOwner.Commit(new RuntimePlayerMember
                    {
                        Connection = other,
                        Slot = other.Player.Slot,
                        Revision = 1,
                        HasHealth = true,
                        Life = 400,
                        MaxLife = 400
                    });
                    Check(items.TryAttach(other), name + " disjoint actor attach");
                    Check(players.TryApply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 2, 1, 0, 9, 0))),
                        name + " actual disjoint5 before bank cursor report");
                    int reports = 0;
                    foreach (var hex in row.GetProperty("syncFrames").EnumerateArray())
                    {
                        var bytes = Convert.FromHexString(hex.GetString()!);
                        if (bytes[2] == 5)
                        {
                            var frame = new TerrariaFrame((ushort)bytes.Length, 5, new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
                            Check(TerrariaPlayerEquipmentCodec.TryDecode(in frame, out var decoded) == TerrariaPlayerEquipmentDecodeResult.Decoded, name + " original frame decode");
                            Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, decoded.SlotId, decoded.Stack, decoded.Prefix, decoded.ItemNetId, decoded.ItemFlags))), name + " actual5"); reports++;
                        }
                        else if (bytes[2] == 138)
                        {
                            Check(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(connection)), name + " actual138");
                            // A harness timeout catches a stuck resolver; this is not a production
                            // gesture/search limit. The existing source readiness owns completion.
                            int rowSlices = 0;
                            while (!players.CanApplyShopSource(connection.Source))
                            {
                                Check(rowSlices++ < 100_000, name + " finite journal retirement");
                                players.TickShopJournalWork();
                                Check(players.LastShopWorkSteps is >= 0 and <= PlayerAuthority.ShopWorkStepsPerTick, name + " global owned work bound");
                                maximumWork = Math.Max(maximumWork, players.LastShopWorkSteps); slices++;
                            }
                        }
                    }
                    var expected = after.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
                    for (short i = 0; i < 59; i++) Check(items.TryGet(connection, i, out var current) && current == expected[i], name + " source inventory slot" + i);
                    var banks = new RuntimePlayerInventoryItem[160]; var funds = new long[160];
                    Check(items.TryCaptureShopBanks(connection, banks, funds), name + " capture final banks");
                    Check(banks.AsSpan().SequenceEqual(after.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(Item).ToArray()), name + " full160 source banks");
                    Check(items.TryCaptureShopBankOwnership(connection, out var actualQuantity, out var actualCaps), name + " ownership capture");
                    var expectedBanks = after.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(Item).ToArray();
                    var mainQuantity = items.CaptureFundedQuantities(connection); var mainCopper = items.CaptureFundedCoinValues(connection);
                    for (int i = 0; i < 59; i++)
                    {
                        long value = VanillaCoinFacts.TryGetValue(expected[i].ItemType, out var unit) ? unit * Math.Max(0, (int)expected[i].Stack) : 0;
                        Check(mainCopper[i] == value, name + " funded main copper" + i);
                        Check(mainQuantity[i] == (value == 0 ? Math.Max(0, (int)expected[i].Stack) : 0), name + " funded main quantity" + i);
                    }
                    for (int i = 0; i < 160; i++)
                    {
                        long value = VanillaCoinFacts.TryGetValue(expectedBanks[i].ItemType, out var unit) ? unit * Math.Max(0, (int)expectedBanks[i].Stack) : 0;
                        Check(funds[i] == value, name + " funded bank copper" + i);
                        Check(actualQuantity[i] == (value == 0 ? Math.Max(0, (int)expectedBanks[i].Stack) : 0), name + " funded bank quantity" + i);
                    }
                    Check(memberOwner.TryGet(connection, out var acceptedMember) && acceptedMember.ItemPhase is { ShopOpen: false }, name + " bank move does not open shop");
                }
                catch (Exception ex) { failures.Add(row.GetProperty("spec").GetProperty("name").GetString() + ": " + ex.Message); }
            }
        }

        Xunit.Assert.Equal(64, rows);
        Xunit.Assert.True(checks > 10000);
        Xunit.Assert.InRange(maximumWork, 1, 64);
        Xunit.Assert.True(slices > 0);
        Xunit.Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void VerifyPartialFundingAndCaps()
    {
        // Configured budgets are not original private funding facts. This same
        // source image validates cap relocation without minting unknown quantity.
        int checks = 0, rows = 0, slices = 0, maximumWork = 0; var failures = new List<string>();
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
        int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
        RuntimePlayerInventoryItem Item(JsonElement x) => x[0].GetInt32() == 0 || x[1].GetInt32() <= 0 ? default : new(new(x[0].GetInt32()), checked((short)x[1].GetInt32()), new(x[2].GetInt32()), x[3].GetBoolean() ? (byte)1 : (byte)0);
        for (int fundingMode = 0; fundingMode <= 3; fundingMode++)
            for (int platform = 0; platform < 2; platform++)
            {
                using var input = typeof(OwnedBankRestockAuthority1458Tests).Assembly.GetManifestResourceStream("BankRestockSource1458")!;
                using var gz = new GZipStream(input, CompressionMode.Decompress);
                using var source = JsonDocument.Parse(gz);
                var document = source.RootElement.GetProperty(platform == 0 ? "linux" : "windows");
                foreach (var row in document.GetProperty("rows").EnumerateArray())
                {
                    if (row.GetProperty("spec").GetProperty("op").GetString() != "restock" || !row.GetProperty("spec").GetProperty("name").GetString()!.EndsWith("favorite_prefix")) continue;
                    rows++;
                    try
                    {
                        string name = row.GetProperty("spec").GetProperty("name").GetString()!;
                        var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
                        var spec = row.GetProperty("spec");
                        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
                        players.SetPlayerUpdateRandom(new(0));
                        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
                        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
                        var memberOwner = Membership(players);
                        var items = Inventory(players);
                        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
                        var member = new RuntimePlayerMember
                        {
                            Connection = connection,
                            Slot = connection.Player.Slot,
                            Revision = 1,
                            HasHealth = true,
                            Life = 400,
                            MaxLife = 400,
                            ItemAnimation = I(before, "animation")
                        };
                        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = I(before, "itemTime") } };
                        memberOwner.Commit(member); Check(items.TryAttach(connection), name + " attach");
                        var image = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
                        image[58] = Item(before.GetProperty("mouse"));
                        var q = new short[59]; var money = new long[59];
                        for (short i = 0; i < 59; i++)
                        {
                            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot, i)), name + " initial inventory");
                            if (VanillaCoinFacts.TryGetValue(image[i].ItemType, out long unit)) money[i] = unit * Math.Max(0, (int)image[i].Stack);
                            else q[i] = Math.Max((short)0, image[i].Stack);
                        }
                        for (int slot = 0; slot < 59; slot++) if (image[slot].ItemType.Value is 109 or 110) q[slot] = 0;
                        items.AdoptFundedQuantities(connection, q, money);
                        items.AdoptOwnedQuantities(connection, [new(10, new(110), 1), new(11, new(109), 1), new(2, new(500), 0)]);
                        var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(Item).ToArray();
                        var bankCopper = new long[160]; var bankQuantity = new short[160]; var bankCaps = new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount]; Array.Fill(bankCaps, (short)-1);
                        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType, out long unit)) bankCopper[i] = unit * Math.Max(0, (int)bankImage[i].Stack);
                        for (int i = 0; i < 160; i++) if (bankCopper[i] == 0) bankQuantity[i] = Math.Max((short)0, bankImage[i].Stack);
                        for (int slot = 0; slot < 160; slot++) if (bankImage[slot].ItemType.Value is 109 or 110) bankQuantity[slot] = (short)fundingMode;
                        int selectedBank = spec.GetProperty("bank").GetInt32(); bankCaps[(selectedBank * 40) * RuntimePlayerInventoryStore.QuantityLaneCount + 1] = 3; bankCaps[(selectedBank * 40 + 1) * RuntimePlayerInventoryStore.QuantityLaneCount] = 3;
                        Check(items.TryAdoptShopBanks(connection, bankImage, bankCopper, bankQuantity, bankCaps), name + " configured banks");
                        // Another actor advances the GLOBAL serial after player0's last own stamp.
                        // The first cursor report must still retain the source-valid bank alternative.
                        var other = new ConnectionHandle(GameCommandSourceId.FromConnection(90002), new(new(1), new(1)));
                        memberOwner.Commit(new RuntimePlayerMember
                        {
                            Connection = other,
                            Slot = other.Player.Slot,
                            Revision = 1,
                            HasHealth = true,
                            Life = 400,
                            MaxLife = 400
                        });
                        Check(items.TryAttach(other), name + " disjoint actor attach");
                        Check(players.TryApply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 2, 1, 0, 9, 0))),
                            name + " actual disjoint5 before bank cursor report");
                        int reports = 0;
                        foreach (var hex in row.GetProperty("syncFrames").EnumerateArray())
                        {
                            var bytes = Convert.FromHexString(hex.GetString()!);
                            if (bytes[2] == 5)
                            {
                                var frame = new TerrariaFrame((ushort)bytes.Length, 5, new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
                                Check(TerrariaPlayerEquipmentCodec.TryDecode(in frame, out var decoded) == TerrariaPlayerEquipmentDecodeResult.Decoded, name + " original frame decode");
                                Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, decoded.SlotId, decoded.Stack, decoded.Prefix, decoded.ItemNetId, decoded.ItemFlags))), name + " actual5"); reports++;
                            }
                            else if (bytes[2] == 138)
                            {
                                Check(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(connection)), name + " actual138");
                                // A harness timeout catches a stuck resolver; this is not a production
                                // gesture/search limit. The existing source readiness owns completion.
                                int rowSlices = 0;
                                while (!players.CanApplyShopSource(connection.Source))
                                {
                                    Check(rowSlices++ < 100_000, name + " finite journal retirement");
                                    players.TickShopJournalWork();
                                    Check(players.LastShopWorkSteps is >= 0 and <= PlayerAuthority.ShopWorkStepsPerTick, name + " global owned work bound");
                                    maximumWork = Math.Max(maximumWork, players.LastShopWorkSteps); slices++;
                                }
                            }
                        }
                        var expected = after.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
                        for (short i = 0; i < 59; i++) Check(items.TryGet(connection, i, out var current) && current == expected[i], name + " source inventory slot" + i);
                        var banks = new RuntimePlayerInventoryItem[160]; var funds = new long[160];
                        Check(items.TryCaptureShopBanks(connection, banks, funds), name + " capture final banks");
                        Check(banks.AsSpan().SequenceEqual(after.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(Item).ToArray()), name + " full160 source banks");
                        Check(items.TryCaptureShopBankOwnership(connection, out var actualQuantity, out var actualCaps), name + " ownership capture");
                        var expectedBanks = after.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(Item).ToArray();
                        var mainQuantity = items.CaptureFundedQuantities(connection); var mainCopper = items.CaptureFundedCoinValues(connection);
                        for (int i = 0; i < 59; i++)
                        {
                            long value = VanillaCoinFacts.TryGetValue(expected[i].ItemType, out var unit) ? unit * Math.Max(0, (int)expected[i].Stack) : 0;
                            Check(mainCopper[i] == value, name + " funded main copper" + i);
                            Check(mainQuantity[i] == (expected[i].ItemType.Value is 109 or 110 ? fundingMode : value == 0 ? Math.Max(0, (int)expected[i].Stack) : 0), name + " funded main quantity" + i);
                        }
                        for (int i = 0; i < 160; i++)
                        {
                            long value = VanillaCoinFacts.TryGetValue(expectedBanks[i].ItemType, out var unit) ? unit * Math.Max(0, (int)expectedBanks[i].Stack) : 0;
                            Check(funds[i] == value, name + " funded bank copper" + i);
                            Check(actualQuantity[i] == (value == 0 ? Math.Max(0, (int)expectedBanks[i].Stack) : 0), name + " funded bank quantity" + i);
                        }
                        var finalCaps = items.CaptureOwnedQuantities(connection);
                        Check(finalCaps.Contains(new(10, new(110), 4)) && finalCaps.Contains(new(11, new(109), 4)) && finalCaps.Contains(new(2, new(500), 0)), name + " exact main transferred and untouched historical caps");
                        Check(actualCaps[(selectedBank * 40) * RuntimePlayerInventoryStore.QuantityLaneCount + 1] == 0 && actualCaps[(selectedBank * 40 + 1) * RuntimePlayerInventoryStore.QuantityLaneCount] == 0, name + " exact depleted bank caps");
                        Check(memberOwner.TryGet(connection, out var acceptedMember) && acceptedMember.ItemPhase is { ShopOpen: false }, name + " bank move does not open shop");
                    }
                    catch (Exception ex) { failures.Add(row.GetProperty("spec").GetProperty("name").GetString() + ": " + ex.Message); }
                }
            }

        Xunit.Assert.Equal(32, rows);
        Xunit.Assert.True(checks > 18000);
        Xunit.Assert.InRange(maximumWork, 1, 64);
        Xunit.Assert.True(slices > 0);
        Xunit.Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void VerifyAmbiguousAmmoFundingIsRejected()
    {
        // Source Restock and a source-certified Right29 then Right1 path have
        // identical final219 but different private budgets. Neither is UI intent.
        int checks = 0, rows = 0, slices = 0, maximumWork = 0; var failures = new List<string>();
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
        int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
        RuntimePlayerInventoryItem Item(JsonElement x) => x[0].GetInt32() == 0 || x[1].GetInt32() <= 0 ? default : new(new(x[0].GetInt32()), checked((short)x[1].GetInt32()), new(x[2].GetInt32()), x[3].GetBoolean() ? (byte)1 : (byte)0);
        for (int corpus = 0; corpus < 2; corpus++)
            for (int platform = 0; platform < 2; platform++)
            {
                using var input = typeof(OwnedBankRestockAuthority1458Tests).Assembly.GetManifestResourceStream("BankRestockSource1458")!;
                using var gz = new GZipStream(input, CompressionMode.Decompress);
                using var source = JsonDocument.Parse(gz);
                var document = source.RootElement.GetProperty(platform == 0 ? "linux" : "windows");
                foreach (var row in document.GetProperty("rows").EnumerateArray())
                {
                    if (!row.GetProperty("spec").GetProperty("name").GetString()!.EndsWith("ammo_spill", StringComparison.Ordinal)) continue;
                    rows++;
                    try
                    {
                        string name = row.GetProperty("spec").GetProperty("name").GetString()!;
                        var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
                        var spec = row.GetProperty("spec");
                        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
                        players.SetPlayerUpdateRandom(new(0));
                        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
                        players.BindReceiveEquipmentRandom(new(0), platform != 0, new(false, false, false));
                        var memberOwner = Membership(players);
                        var items = Inventory(players);
                        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(90001), new(new(0), new(1)));
                        var member = new RuntimePlayerMember
                        {
                            Connection = connection,
                            Slot = connection.Player.Slot,
                            Revision = 1,
                            HasHealth = true,
                            Life = 400,
                            MaxLife = 400,
                            ItemAnimation = I(before, "animation")
                        };
                        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ItemTime = I(before, "itemTime") } };
                        memberOwner.Commit(member); Check(items.TryAttach(connection), name + " attach");
                        var image = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
                        image[58] = Item(before.GetProperty("mouse"));
                        var q = new short[59]; var money = new long[59];
                        for (short i = 0; i < 59; i++)
                        {
                            Check(items.TrySet(connection, image[i].ToCommitRequest(connection.Player.Slot, i)), name + " initial inventory");
                            if (VanillaCoinFacts.TryGetValue(image[i].ItemType, out long unit)) money[i] = unit * Math.Max(0, (int)image[i].Stack);
                            else q[i] = corpus == 0 ? Math.Max((short)0, image[i].Stack) : (short)Math.Min(1, Math.Max(0, (int)image[i].Stack));
                        }
                        items.AdoptFundedQuantities(connection, q, money);
                        var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(Item).ToArray();
                        var bankCopper = new long[160]; var bankQuantity = new short[160]; var bankCaps = new short[160 * RuntimePlayerInventoryStore.QuantityLaneCount]; Array.Fill(bankCaps, (short)-1);
                        for (int i = 0; i < 160; i++) if (VanillaCoinFacts.TryGetValue(bankImage[i].ItemType, out long unit)) bankCopper[i] = unit * Math.Max(0, (int)bankImage[i].Stack);
                        for (int i = 0; i < 160; i++) if (bankCopper[i] == 0) bankQuantity[i] = (short)Math.Min(1, Math.Max(0, (int)bankImage[i].Stack));
                        Check(items.TryAdoptShopBanks(connection, bankImage, bankCopper, bankQuantity, bankCaps), name + " configured banks");
                        var originalBudgets = new Dictionary<(int Type, int Prefix), long>();
                        void AddBudget(Dictionary<(int Type, int Prefix), long> values, RuntimePlayerInventoryItem item, int budget)
                        { if (budget == 0) return; var key = (item.ItemType.Value, item.Prefix.Value); values.TryGetValue(key, out var previous); values[key] = previous + budget; }
                        for (int i = 0; i < 59; i++) AddBudget(originalBudgets, image[i], q[i]);
                        for (int i = 0; i < 160; i++) AddBudget(originalBudgets, bankImage[i], bankQuantity[i]);
                        long originalCopper = money.Sum() + bankCopper.Sum();
                        // Another actor advances the GLOBAL serial after player0's last own stamp.
                        // The first cursor report must still retain the source-valid bank alternative.
                        var other = new ConnectionHandle(GameCommandSourceId.FromConnection(90002), new(new(1), new(1)));
                        memberOwner.Commit(new RuntimePlayerMember
                        {
                            Connection = other,
                            Slot = other.Player.Slot,
                            Revision = 1,
                            HasHealth = true,
                            Life = 400,
                            MaxLife = 400
                        });
                        Check(items.TryAttach(other), name + " disjoint actor attach");
                        Check(players.TryApply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 2, 1, 0, 9, 0))),
                            name + " actual disjoint5 before bank cursor report");
                        int reports = 0;
                        foreach (var hex in row.GetProperty("syncFrames").EnumerateArray())
                        {
                            var bytes = Convert.FromHexString(hex.GetString()!);
                            if (bytes[2] == 5)
                            {
                                var frame = new TerrariaFrame((ushort)bytes.Length, 5, new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
                                Check(TerrariaPlayerEquipmentCodec.TryDecode(in frame, out var decoded) == TerrariaPlayerEquipmentDecodeResult.Decoded, name + " original frame decode");
                                Check(players.TryApply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, decoded.SlotId, decoded.Stack, decoded.Prefix, decoded.ItemNetId, decoded.ItemFlags))), name + " actual5"); reports++;
                            }
                            else if (bytes[2] == 138)
                            {
                                Check(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(connection)), name + " actual138");
                                // A harness timeout catches a stuck resolver; this is not a production
                                // gesture/search limit. The existing source readiness owns completion.
                                int rowSlices = 0;
                                while (!players.CanApplyShopSource(connection.Source))
                                {
                                    Check(rowSlices++ < 100_000, name + " finite journal retirement");
                                    players.TickShopJournalWork();
                                    Check(players.LastShopWorkSteps is >= 0 and <= PlayerAuthority.ShopWorkStepsPerTick, name + " global owned work bound");
                                    maximumWork = Math.Max(maximumWork, players.LastShopWorkSteps); slices++;
                                }
                            }
                        }
                        var finalMain = new RuntimePlayerInventoryItem[59];
                        for (short i = 0; i < 59; i++) Check(items.TryGet(connection, i, out finalMain[i]), name + " final main capture");
                        var banks = new RuntimePlayerInventoryItem[160]; var funds = new long[160];
                        Check(items.TryCaptureShopBanks(connection, banks, funds), name + " final banks");
                        Check(items.TryCaptureShopBankOwnership(connection, out var actualQuantity, out var actualCaps), name + " final bank ownership");
                        var finalQ = items.CaptureFundedQuantities(connection); var finalMoney = items.CaptureFundedCoinValues(connection);
                        var finalBudgets = new Dictionary<(int Type, int Prefix), long>();
                        for (int i = 0; i < 59; i++) { Check(finalQ[i] >= 0 && finalQ[i] <= finalMain[i].Stack, name + " main physical ceiling"); AddBudget(finalBudgets, finalMain[i], finalQ[i]); }
                        for (int i = 0; i < 160; i++) { Check(actualQuantity[i] >= 0 && actualQuantity[i] <= banks[i].Stack, name + " bank physical ceiling"); AddBudget(finalBudgets, banks[i], actualQuantity[i]); }
                        foreach (var pair in finalBudgets) { originalBudgets.TryGetValue(pair.Key, out var original); Check(pair.Value <= original, name + " no per-identity grant"); }
                        Check(finalMoney.Sum() + funds.Sum() <= originalCopper, name + " no copper grant");
                        Check(players.CanApplyShopSource(connection.Source), name + " ambiguous receipt terminal");
                        Check(players.RejectedEquipmentUpdates == 1, name + " terminal ambiguous custody rejection");
                        Check(finalQ[54] == q[54] && finalQ[57] == 0, name + " no guessed Restock or cursor funding allocation");
                        Check(memberOwner.TryGet(connection, out var acceptedMember) && acceptedMember.ItemPhase is { ShopOpen: false }, name + " bank move does not open shop");
                    }
                    catch (Exception ex) { failures.Add(row.GetProperty("spec").GetProperty("name").GetString() + ": " + ex.Message); }
                }
            }

        Xunit.Assert.Equal(16, rows);
        Xunit.Assert.InRange(maximumWork, 1, 64);
        Xunit.Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority authority);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority authority);
}
