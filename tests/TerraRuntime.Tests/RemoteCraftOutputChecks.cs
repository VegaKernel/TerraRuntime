using TerraRuntime.Core.Worlds;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests.WorldChestCrafting;

internal static class RemoteCraftOutputChecks
{
    internal static int Run()
    {
        int checks = 0, cases = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        RuntimePlayerInventoryItem Item(JsonElement value) => new(new ItemTypeId(value.GetProperty("type").GetInt32()),
            (short)value.GetProperty("stack").GetInt32(), new PrefixId(value.GetProperty("prefix").GetInt32()),
            value.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = WorldChestCraftSource.Read("RemoteCraftEarly" + platform + "1458");
            foreach (var row in source.RootElement.EnumerateArray().Where(x => x.GetProperty("mode").GetString()!.StartsWith("remote-")))
                foreach (bool runtimeTick in new[] { false, true })
                {
                    string name = platform + "/" + row.GetProperty("mode").GetString() + (runtimeTick ? "/State.Tick" : "/owned-work");
                    var before = row.GetProperty("before");
                    WorldChestItem[] Stock(int index) => before.GetProperty("worldChests")[index].EnumerateArray().Select(x =>
                        new WorldChestItem(x.GetProperty("stack").GetInt32(), x.GetProperty("type").GetInt32(), x.GetProperty("prefix").GetByte())).ToArray();
                    using var wrapper = JsonDocument.Parse("{\"name\":\"owner-open\",\"before\":" + before.GetProperty("worldChests")[0].GetRawText() + "}");
                    using var fixture = new Fixture(wrapper.RootElement, new[] { new WorldChest(0, 20, 20, "", Stock(0)), new WorldChest(1, 24, 20, "", Stock(1)) });
                    var items = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
                    items[58] = Item(before.GetProperty("mouse"));
                    Check(fixture.ActorInventory.TryApplyAtomic(fixture.Connection, items.Select((item, index) => new RuntimePlayerInventoryMutation((byte)index, item)).ToArray()), name + " trusted main setup");
                    for (int i = 0; i < 59; i++) Fixture.Funding(fixture.ActorInventory)[i] = items[i].Stack;
                    int bankIndex = 0;
                    foreach (var bank in before.GetProperty("banks").EnumerateArray())
                    {
                        int bankSlot = 0;
                        foreach (var value in bank.EnumerateArray())
                        {
                            var item = Item(value);
                            if (!item.IsEmpty)
                            {
                                short[] starts = [99, 299, 500, 700];
                                Check(fixture.ActorInventory.TrySetShopBankReport(fixture.Connection, (short)(starts[bankIndex] + bankSlot), in item), name + " trusted bank image");
                                Fixture.BankFunding(fixture.ActorInventory)[bankIndex * 40 + bankSlot] = item.Stack;
                            }
                            bankSlot++;
                        }
                        bankIndex++;
                    }
                    if (before.GetProperty("chest").GetInt32() is >= -5 and <= -2)
                    {
                        var context = new TerrariaActiveChestState((short)before.GetProperty("chest").GetInt32(), 0, 0, 0, "");
                        Check(fixture.Store.TryApplyActiveState(fixture.Connection, in context, out _, out _, true), name + " genuine source current bank context");
                    }
                    fixture.Member.HasHealth = true; fixture.Member.Life = 100; fixture.Member.MaxLife = 100;
                    void Dispatch(IEnumerable<JsonElement> frames)
                    {
                        foreach (var encoded in frames)
                        {
                            var frame = Fixture.Decode(Convert.FromHexString(encoded.GetString()!)); fixture.Bootstrap.OnFrame(in frame);
                            while (fixture.Commands.Commands.Count > 0)
                            {
                                var command = fixture.Commands.Commands[0];
                                while (!fixture.Processor.CanApplyCraftingCommand(command))
                                {
                                    int work; if (runtimeTick) { fixture.State.Tick(); work = fixture.Processor.LastCraftingWorkSteps; } else { work = fixture.Processor.TickCraftingWork(32); fixture.Players.TickShopJournalWork(64 - work); }
                                    Check(work + fixture.ShopSteps <= 64, name + " existing mailbox craft throttle");
                                }
                                fixture.Commands.Commands.RemoveAt(0);
                                if (!fixture.Processor.TryApply(command)) fixture.State.Apply(command);
                                if (name.Contains("deny") && command is ClientChestCraftingRuntimeCommand)
                                {
                                    fixture.Store.TryOpen(fixture.Connection, 20, 20, out _);
                                    Check(fixture.Store.TryClose(fixture.Connection, out _), name + " configured authoritative late denial");
                                }
                            }
                        }
                    }
                    Dispatch(row.GetProperty("craftFrames").EnumerateArray().Concat(row.GetProperty("preResponseSyncFrames").EnumerateArray()));
                    int expectedRequests = row.GetProperty("craftFrames").GetArrayLength();
                    for (int tick = 0; tick < 2048 && fixture.Processor.AppliedCraftingRequests + fixture.Processor.RejectedCraftingRequests < expectedRequests; tick++)
                    {
                        int remote; if (runtimeTick) { fixture.State.Tick(); remote = fixture.Processor.LastCraftingWorkSteps; } else { remote = fixture.Processor.TickCraftingWork(32); fixture.Players.TickShopJournalWork(64 - remote); }
                        Check(remote + fixture.ShopSteps <= 64, name + " shared work");
                    }
                    var paidCosts = fixture.Member.CursorMove?.Shop?.RemoteCosts;
                    var beforeResult = fixture.CapturePlayer();
                    Check(beforeResult.Items[58].IsEmpty, name + " remote approval alone never grants output");
                    int callbacks = 0;
                    fixture.Events.OnEquipment = () =>
                    {
                        callbacks++;
                        var image = fixture.CapturePlayer();
                        var sourceFinal = row.GetProperty("afterSync");
                        var expectedMouse = Item(sourceFinal.GetProperty("mouse"));
                        Check(image.Items[58] == expectedMouse, name + " first observer sees final cursor");
                        if (paidCosts is not null) Check(Fixture.CostConsumed(paidCosts) && !paidCosts.IsCurrent && !paidCosts.TryConsumeUnpublished(fixture.Connection), name + " certificate consumed before first observer");
                        for (int i = 0; i < 59; i++) Check(image.Quantity[i] == image.Items[i].Stack, name + " observer private quantity" + i);
                    };
                    Dispatch(row.GetProperty("syncFrames").EnumerateArray());
                    for (int tick = 0; tick < 2048 && fixture.Member.CursorMove?.Shop is not null; tick++) { if (runtimeTick) fixture.State.Tick(); else { int remote = fixture.Processor.TickCraftingWork(32); fixture.Players.TickShopJournalWork(64 - remote); } Check(fixture.Processor.LastCraftingWorkSteps + fixture.ShopSteps <= 64, name + " final shared budget"); }
                    var actual = fixture.CapturePlayer(); var expected = row.GetProperty("afterSync");
                    Console.WriteLine("RESULT " + name + " applied=" + fixture.Processor.AppliedCraftingRequests + " rejected=" + fixture.Processor.RejectedCraftingRequests + " actorRejected=" + fixture.Players.RejectedEquipmentUpdates + " shop=" + (fixture.Member.CursorMove?.Shop is not null) + " mouse=" + actual.Items[58]);
                    int slot = 0;
                    foreach (var value in expected.GetProperty("inventory").EnumerateArray())
                    {
                        var wanted = slot == 58 ? Item(expected.GetProperty("mouse")) : Item(value);
                        Check(actual.Items[slot] == wanted, name + " source main" + slot);
                        Check(actual.Quantity[slot] == wanted.Stack, name + " exact main funding" + slot);
                        slot++;
                    }
                    int finalBank = 0;
                    foreach (var values in expected.GetProperty("banks").EnumerateArray())
                    {
                        int index = 0;
                        foreach (var value in values.EnumerateArray())
                        {
                            var wanted = Item(value); int offset = 59 + finalBank * 40 + index++;
                            Check(actual.Items[offset] == wanted && actual.Quantity[offset] == wanted.Stack && actual.Copper[offset] == 0, name + " full bank custody" + offset);
                        }
                        finalBank++;
                    }
                    var worldAfter = fixture.Store.CaptureSnapshot().ToDictionary(chest => chest.SlotId);
                    var worldExpected = new[] { Stock(0), Stock(1) };
                    if (paidCosts is not null) foreach (var material in paidCosts.Materials) worldExpected[material.ChestId][material.Slot] = material.After;
                    for (short chest = 0; chest < 2; chest++) Check(worldAfter[chest].Items.AsSpan().SequenceEqual(worldExpected[chest]), name + " complete authoritative world image" + chest);
                    Check(actual.Caps.All(static cap => cap <= 0), name + " fixed quantity ABI untouched");
                    cases++;
                }
        }
        Console.WriteLine("REMOTE_OUTPUT cases=" + cases + " checks=" + checks);

        return checks;
    }
}
