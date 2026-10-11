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

public sealed class OwnedBankSortAuthority1458Tests
{
    [Xunit.Fact]
    public void Original_localized_sort_reports_preserve_custody_through_owned_work()
    {
        VerifyMainSortPreparationCurrentness();
        VerifyMainSortCreditFirstCursorOverlap();
        ClosedSortCycleAuthority1458Checks.Run();
        BankChainMemoAuthority1458Checks.Run();
        // Actual original5/138, configured initial funded219, direct owned work.
        // Locale/class alternatives are economic explanations, not client intent.
        int checks = 0, rows = 0, slices = 0, maximumWork = 0; var failures = new List<string>();
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
        int I(JsonElement x, string key) => x.GetProperty(key).GetInt32();
        RuntimePlayerInventoryItem Item(JsonElement x) => x[0].GetInt32() == 0 || x[1].GetInt32() <= 0 ? default : new(new(x[0].GetInt32()), checked((short)x[1].GetInt32()), new(x[2].GetInt32()), x[3].GetBoolean() ? (byte)1 : (byte)0);
        for (int platform = 0; platform < 2; platform++)
        {
            using var input = typeof(OwnedBankSortAuthority1458Tests).Assembly.GetManifestResourceStream("OriginalLocalizedItemSorting1458")!;
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gz);
            var document = source.RootElement.GetProperty(platform == 0 ? "Linux" : "Windows");
            foreach (var row in document.GetProperty("rows").EnumerateArray())
            {

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
                    bool mainCaps = name.Contains("sortmain_rank_1423", StringComparison.Ordinal);
                    if (mainCaps)
                        items.AdoptOwnedQuantities(connection, [new(18, new(110), 2), new(15, new(110), 0), new(2, new(500), 0)]);
                    var bankImage = before.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(Item).ToArray();
                    var bankCopper = new long[160]; var bankQuantity = new short[160]; var bankCaps = new short[800]; Array.Fill(bankCaps, (short)-1);
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
                    if (mainCaps)
                    {
                        var finalCaps = items.CaptureOwnedQuantities(connection);
                        Check(finalCaps.Contains(new(15, new(110), 2)), name + " owned main-sort cap reaches exact source destination");
                        Check(finalCaps.Contains(new(18, new(110), 0)), name + " owned main-sort source cap depleted");
                        Check(finalCaps.Contains(new(2, new(500), 0)), name + " untouched historical cap preserved");
                    }
                    Check(memberOwner.TryGet(connection, out var acceptedMember) && acceptedMember.ItemPhase is { ShopOpen: false }, name + " bank move does not open shop");
                }
                catch (Exception ex) { failures.Add(row.GetProperty("spec").GetProperty("name").GetString() + ": " + ex.Message); }
            }
        }

        Xunit.Assert.Equal(96, rows);
        Xunit.Assert.True(checks > 56000);
        Xunit.Assert.InRange(maximumWork, 1, 64);
        Xunit.Assert.True(slices > 0);
        Xunit.Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void VerifyMainSortCreditFirstCursorOverlap()
    {
        // Configured source-backed planner overlap, not a new original UI capture.
        // The first gain matches both Sort and a context-0 cursor deposit; only
        // later images establish which retained explanation can complete.
        var players = new PlayerAuthority(null, new WorldTileStore(new WorldDimensions(400, 300)));
        players.SetPlayerUpdateRandom(new(0));
        players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
        players.BindReceiveEquipmentRandom(new(0), false, new(false, false, false));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99003), new(new(0), new(1)));
        var members = Membership(players);
        var inventory = Inventory(players);
        var member = new RuntimePlayerMember
        {
            Connection = connection,
            Slot = connection.Player.Slot,
            Revision = 1,
            HasHealth = true,
            Life = 400,
            MaxLife = 400
        };
        members.Commit(member);
        Xunit.Assert.True(inventory.TryAttach(connection));
        var before = new RuntimePlayerInventoryItem[59];
        before[10] = new(new(109), 9, new(0), 0);
        before[11] = before[58] = new(new(109), 3, new(0), 0);
        for (short slot = 0; slot < before.Length; slot++)
            Xunit.Assert.True(inventory.TrySet(connection, before[slot].ToCommitRequest(connection.Player.Slot, slot)));
        inventory.AdoptFundedQuantities(connection, before.Select(item => item.Stack).ToArray());
        inventory.AdoptOwnedQuantities(connection,
            [new(2, new(500), 0), new(10, new(109), 9), new(11, new(109), 3), new(58, new(109), 3)]);
        var bankCaps = new short[800];
        Array.Fill(bankCaps, (short)-1);
        Xunit.Assert.True(inventory.TryAdoptShopBanks(connection, new RuntimePlayerInventoryItem[160],
            new long[160], new short[160], bankCaps));
        var capsBefore = inventory.CaptureOwnedQuantities(connection);
        var sourceBefore = before.Select(item => new WorldItemPickupSlot1458(
            item.ItemType.Value, item.Stack, item.Prefix.Value, item.ItemFlags != 0)).ToArray();
        Xunit.Assert.True(VanillaWorldItemOwner1458.TryPlanSort(sourceBefore, true,
            0, 0xE4, false, false, new(false, false, false), out var sort));
        Xunit.Assert.NotNull(sort);
        var target = sort.Slots.Select(item => item.Type == 0 ? default :
            new RuntimePlayerInventoryItem(new(item.Type), checked((short)item.Stack),
                new(item.Prefix), item.Favorite ? (byte)1 : (byte)0)).ToArray();
        Xunit.Assert.Equal(before[10] with { Stack = 12 }, target[10]);
        Xunit.Assert.True(target[11].IsEmpty);
        Xunit.Assert.Equal(before[58], target[58]);
        Xunit.Assert.True(VanillaInventoryCursorMove1458.TryPlanLeftClick(sourceBefore[10], sourceBefore[58],
            out var pairSlot, out var pairCursor));
        Xunit.Assert.Equal(sort.Slots[10], pairSlot);
        Xunit.Assert.Equal(default, pairCursor);
        var firstReport = target[10].ToCommitRequest(connection.Player.Slot, 10);
        Xunit.Assert.False(inventory.CanAcceptOwnedQuantityReport(connection, in firstReport));
        long rejectedBefore = players.RejectedEquipmentUpdates;
        Xunit.Assert.True(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,
            target[10].ToCommitRequest(connection.Player.Slot, 10))));
        Xunit.Assert.NotNull(member.CursorMove?.Inventory);
        Xunit.Assert.NotNull(member.CursorMove?.Shop);
        Xunit.Assert.True(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,
            target[11].ToCommitRequest(connection.Player.Slot, 11))));
        Xunit.Assert.NotNull(member.CursorMove?.Shop);
        Xunit.Assert.True(players.TryApply(new PlayerInventorySyncCompleteRuntimeCommand(connection)));
        int slices = 0;
        while (!players.CanApplyShopSource(connection.Source))
        {
            Xunit.Assert.True(++slices <= 10000);
            players.TickShopJournalWork();
            Xunit.Assert.InRange(players.LastShopWorkSteps, 0, PlayerAuthority.ShopWorkStepsPerTick);
        }
        Xunit.Assert.Equal(rejectedBefore, players.RejectedEquipmentUpdates);
        var actual = new RuntimePlayerInventoryItem[59];
        Xunit.Assert.True(inventory.TryCopyInventory(connection, actual));
        Xunit.Assert.Equal(target, actual);
        Xunit.Assert.Equal(target.Select(item => item.Stack).ToArray(), inventory.CaptureFundedQuantities(connection));
        Xunit.Assert.Equal(new long[59], inventory.CaptureFundedCoinValues(connection));
        var actualBanks = new RuntimePlayerInventoryItem[160];
        var actualBankCopper = new long[160];
        Xunit.Assert.True(inventory.TryCaptureShopBanks(connection, actualBanks, actualBankCopper));
        Xunit.Assert.True(inventory.TryCaptureShopBankOwnership(connection, out var actualBankQuantity, out var actualBankCaps));
        Xunit.Assert.Equal(new RuntimePlayerInventoryItem[160], actualBanks);
        Xunit.Assert.Equal(new long[160], actualBankCopper);
        Xunit.Assert.Equal(new short[160], actualBankQuantity);
        Xunit.Assert.Equal(bankCaps, actualBankCaps);
        Xunit.Assert.Equal(capsBefore.Select(cap => cap.ItemType.Value == 109 && cap.Slot is 10 or 11
            ? cap with { Maximum = target[cap.Slot].Stack } : cap).ToArray(), inventory.CaptureOwnedQuantities(connection));
    }

    private static void VerifyMainSortPreparationCurrentness()
    {
        using var input = typeof(OwnedBankSortAuthority1458Tests).Assembly.GetManifestResourceStream("OriginalLocalizedItemSorting1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var row = source.RootElement.GetProperty("Linux").GetProperty("rows").EnumerateArray()
            .Single(value => value.GetProperty("spec").GetProperty("name").GetString() == "sortmain_rank_1423_locale1");
        RuntimePlayerInventoryItem Read(JsonElement item) => item[0].GetInt32() == 0 || item[1].GetInt32() <= 0
            ? default : new(new(item[0].GetInt32()), checked((short)item[1].GetInt32()),
                new(item[2].GetInt32()), item[3].GetBoolean() ? (byte)1 : (byte)0);
        var before = row.GetProperty("before");
        var after = row.GetProperty("afterSync");
        var main = before.GetProperty("inventory").EnumerateArray().Select(Read).ToArray();
        var banks = before.GetProperty("banks").EnumerateArray().SelectMany(bank => bank.EnumerateArray()).Select(Read).ToArray();
        var target = after.GetProperty("inventory").EnumerateArray().Select(Read)
            .Concat(after.GetProperty("banks").EnumerateArray().SelectMany(bank => bank.EnumerateArray()).Select(Read)).ToArray();

        for (int mode = 0; mode < 4; mode++)
        {
            var owner = new RuntimePlayerInventoryStore();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99001), new(new(0), new(1)));
            Xunit.Assert.True(owner.TryAttach(connection));
            for (short slot = 0; slot < 59; slot++)
                Xunit.Assert.True(owner.TrySet(connection, main[slot].ToCommitRequest(connection.Player.Slot, slot)));
            var quantity = main.Select(item => item.Stack).ToArray();
            owner.AdoptFundedQuantities(connection, quantity);
            var bankCaps = new short[800];
            Array.Fill(bankCaps, (short)-1);
            Xunit.Assert.True(owner.TryAdoptShopBanks(connection, banks, new long[160],
                banks.Select(item => item.Stack).ToArray(), bankCaps));
            Xunit.Assert.True(owner.TryCaptureShopJournal(connection, out var capture));
            Xunit.Assert.NotNull(capture);
            var seen = target.Select((item, index) => item != capture.Before[index]).ToArray();
            var resolution = new RuntimePlayerInventoryStore.BankBulkResolution();
            int candidates = 0;
            foreach (int ignored in owner.PrepareShopSortWork(connection, capture, target, seen,
                new(false, false, false), resolution, true))
                Xunit.Assert.True(++candidates <= 576);
            Xunit.Assert.False(resolution.Ambiguous);
            var prepared = resolution.Preparation;
            Xunit.Assert.NotNull(prepared);
            Xunit.Assert.True(prepared.IsCurrent);

            if (mode == 0)
            {
                Xunit.Assert.True(prepared.TryAdoptUnpublished());
                Xunit.Assert.False(prepared.TryAdoptUnpublished());
                for (short slot = 0; slot < 59; slot++)
                {
                    Xunit.Assert.True(owner.TryGet(connection, slot, out var actual));
                    Xunit.Assert.Equal(target[slot], actual);
                }
                continue;
            }
            if (mode == 1)
                Xunit.Assert.True(owner.TrySet(connection, new(connection.Player.Slot, 3, 1, 0, 3, 0)));
            else if (mode == 2)
            {
                quantity[18]--;
                owner.AdoptFundedQuantities(connection, quantity);
            }
            else
            {
                owner.Clear(connection);
                Xunit.Assert.True(owner.TryAttach(new(connection.Source, new(connection.Player.Slot, new(2)))));
            }
            var retainedMain = new RuntimePlayerInventoryItem[59];
            bool retained = owner.TryCopyInventory(connection, retainedMain);
            Xunit.Assert.False(prepared.IsCurrent);
            Xunit.Assert.False(prepared.TryAdoptUnpublished());
            if (retained)
            {
                var currentMain = new RuntimePlayerInventoryItem[59];
                Xunit.Assert.True(owner.TryCopyInventory(connection, currentMain));
                Xunit.Assert.Equal(retainedMain, currentMain);
            }
        }
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority authority);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority authority);
}
