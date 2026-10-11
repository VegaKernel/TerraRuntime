using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;
namespace TerraRuntime.Tests;

public sealed class VanillaBankRestock1458Tests
{
    [Xunit.Fact]
    public void Restock_preserves_original_physical_images_and_ordered_transfers()
    {
        int rows = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var input = typeof(VanillaBankRestock1458Tests).Assembly.GetManifestResourceStream("BankRestockSource1458")!;
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gz);
            foreach (var row in doc.RootElement.GetProperty(platform.ToLowerInvariant()).GetProperty("rows").EnumerateArray())
            {
                var spec = row.GetProperty("spec"); if (spec.GetProperty("op").GetString() != "restock") continue;
                int bank = spec.GetProperty("bank").GetInt32();
                var before = row.GetProperty("before"); var after = row.GetProperty("afterSync");
                var main = Read(before.GetProperty("inventory")); var storage = Read(before.GetProperty("banks")[bank]);
                Xunit.Assert.True(VanillaWorldItemOwner1458.TryPlanBankBulk(main, storage, bank, BankBulkOperation1458.Restock, 0, null, out var plan));
                Xunit.Assert.NotNull(plan); Xunit.Assert.Equal(Read(after.GetProperty("inventory")), plan.Inventory); Xunit.Assert.Equal(Read(after.GetProperty("banks")[bank]), plan.Bank);
                for (int other = 0; other < 4; other++)
                    if (other != bank) Xunit.Assert.Equal(Read(before.GetProperty("banks")[other]), Read(after.GetProperty("banks")[other]));
                Xunit.Assert.Equal(before.GetProperty("animation").GetInt32(), after.GetProperty("animation").GetInt32());
                Xunit.Assert.Equal(before.GetProperty("itemTime").GetInt32(), after.GetProperty("itemTime").GetInt32());
                Xunit.Assert.Equal(before.GetProperty("random").GetRawText(), after.GetProperty("random").GetRawText());
                Xunit.Assert.Equal(before.GetProperty("next").GetInt32(), after.GetProperty("next").GetInt32());
                Xunit.Assert.Equal(Read(before.GetProperty("inventory"))[58], plan.Inventory[58]);
                rows++;
                var image = main.Concat(storage).ToArray();
                foreach (var step in plan.Steps)
                {
                    Xunit.Assert.Null(step.Coins); Xunit.Assert.Equal(image[step.Source], step.SourceBefore); Xunit.Assert.Equal(image[step.Destination], step.DestinationBefore);
                    var source = image[step.Source]; var destination = image[step.Destination];
                    Xunit.Assert.InRange(step.Amount, 1, source.Stack);
                    image[step.Source] = source.Stack == step.Amount ? default : source with { Stack = source.Stack - step.Amount };
                    image[step.Destination] = destination.Type == 0 ? source with { Stack = step.Amount } : destination with { Stack = destination.Stack + step.Amount };
                }
                Xunit.Assert.Equal(plan.Inventory.Concat(plan.Bank).ToArray(), image);
            }
        }
        Xunit.Assert.Equal(64, rows);
    }
    private static WorldItemPickupSlot1458[] Read(JsonElement array) => array.EnumerateArray().Select(i => i[0].GetInt32() == 0 ? default : new WorldItemPickupSlot1458(i[0].GetInt32(), i[1].GetInt32(), i[2].GetInt32(), i[3].GetBoolean())).ToArray();
}
