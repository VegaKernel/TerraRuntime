using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests.DurableWorldChestCustody;

internal static class PartialWorldTransferProof
{
    internal static int Run(string directory)
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        RuntimePlayerInventoryItem Item(JsonElement value) => new(new ItemTypeId(value.GetProperty("type").GetInt32()),
            (short)value.GetProperty("stack").GetInt32(), new PrefixId(value.GetProperty("prefix").GetInt32()), 0);
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = WorldChestCraftSource.Read("WorldChestCursor" + platform + "1458");
            foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray().Where(row => row.GetProperty("spec").GetProperty("Name").GetString() is "deposit" or "withdraw"))
            {
                string name = row.GetProperty("spec").GetProperty("Name").GetString()!;
                foreach (short paid in new short[] { 0, 1 })
                {
                    string caseName = platform + "/" + name + "/q" + paid;
                    var before = row.GetProperty("before");
                    var physical = before.GetProperty("chest").EnumerateArray().Select(value => new WorldChestItem(value.GetProperty("stack").GetInt32(), value.GetProperty("type").GetInt32(), value.GetProperty("prefix").GetByte())).ToArray();
                    var quantities = new short[physical.Length];
                    if (name == "withdraw") quantities[0] = paid;
                    var originalChest = new WorldChest(0, 20, 20, "", physical);
                    var custody = new RuntimeWorldChestCustodySnapshot(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), 77, 1,
                        [new(originalChest, quantities, new long[physical.Length])]);
                    using var wrapper = JsonDocument.Parse("{\"name\":\"owner-open\",\"before\":" + before.GetProperty("chest").GetRawText() + "}");
                    using var fixture = new Fixture(wrapper.RootElement, custody: custody);
                    var actor = before.GetProperty("inventory").EnumerateArray().Select(Item).ToArray();
                    actor[58] = Item(before.GetProperty("mouse"));
                    Check(fixture.ActorInventory.TryApplyAtomic(fixture.Connection, actor.Select((item, index) => new RuntimePlayerInventoryMutation((byte)index, item)).ToArray()), caseName + " configured source actor physical image");
                    Array.Clear(Fixture.Funding(fixture.ActorInventory));
                    if (name == "deposit") Fixture.Funding(fixture.ActorInventory)[58] = paid;
                    fixture.Member.HasHealth = true; fixture.Member.Life = 100; fixture.Member.MaxLife = 100;
                    var captured = fixture.CapturePlayer();
                    int callbacks = 0;
                    fixture.Events.OnEquipment = () =>
                    {
                        callbacks++;
                        var current = fixture.CapturePlayer();
                        var world = fixture.Store.CaptureCustodySnapshot(custody.WorldUniqueId, custody.WorldId, 2);
                        Check(current.Quantity[58] == (name == "deposit" ? 0 : paid), caseName + " first observer adopted actor custody");
                        Check(world.Chests[0].Quantity[0] == (name == "deposit" ? paid : 0), caseName + " first observer adopted world custody");
                    };
                    foreach (string hex in row.GetProperty("operationFrames").EnumerateArray().Concat(row.GetProperty("syncFrames").EnumerateArray()).Select(value => value.GetString()!))
                    {
                        var frame = Fixture.Decode(Convert.FromHexString(hex));
                        fixture.Bootstrap.OnFrame(in frame);
                        while (fixture.Commands.Commands.Count > 0)
                        {
                            var command = fixture.Commands.Commands[0]; fixture.Commands.Commands.RemoveAt(0);
                            if (!fixture.Processor.TryApply(command)) fixture.State.Apply(command);
                        }
                    }
                    for (int tick = 0; tick < 2048 && fixture.Member.CursorMove?.Shop is not null; tick++)
                    {
                        fixture.State.Tick();
                        Check(fixture.ShopSteps <= 64, caseName + " actual State.Tick work bound");
                    }
                    Check(fixture.Member.CursorMove?.Shop is null && callbacks == 1, caseName + " source transfer resolves once");
                    var after = fixture.CapturePlayer();
                    var expected = row.GetProperty("afterSync");
                    for (int slot = 0; slot < 219; slot++)
                    {
                        var expectedItem = slot < 59 ? Item(expected.GetProperty("inventory")[slot]) : default;
                        if (slot == 58) expectedItem = Item(expected.GetProperty("mouse"));
                        Check(after.Items[slot] == expectedItem, caseName + " full219 physical slot" + slot);
                        Check(after.Quantity[slot] == (slot == 58 && name == "withdraw" ? paid : 0), caseName + " exact actor quantity" + slot);
                        Check(after.Copper[slot] == captured.Copper[slot], caseName + " no copper mint" + slot);
                    }
                    var chest = fixture.Store.CaptureCustodySnapshot(custody.WorldUniqueId, custody.WorldId, 2);
                    Check(chest.Chests[0].Chest.Items[0].Stack == expected.GetProperty("chest")[0].GetProperty("stack").GetInt32(), caseName + " literal source chest image");
                    Check(chest.Chests[0].Quantity[0] == (name == "deposit" ? paid : 0), caseName + " exact partial world budget");
                    DurableChestCustodyChecks.PersistPartialStore(fixture.Store, fixture.Tiles, [originalChest],
                        Path.Combine(directory, platform + "-" + name + "-" + paid + ".wld"), name == "deposit" ? paid : 0, 0);
                }
            }
        }
        return checks;
    }
}
