using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
namespace TerraRuntime.Tests;
internal static class FoodSelectedConsumable1458Checks
{
    internal static int RunMetadata()
    {
        int checks = 0;
        void Check(bool value) { if (!value) throw new InvalidOperationException($"Food source metadata predicate {checks} failed."); checks++; }
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream($"FoodMetadata{platform}1458")!;
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var data = JsonDocument.Parse(gzip);
            Check(data.RootElement.GetArrayLength() == 6196);
            int foods = 0;
            foreach (var item in data.RootElement.EnumerateArray())
            {
                int id = item.GetProperty("type").GetInt32();
                int buff = item.GetProperty("buffType").GetInt32();
                bool expected = buff == VanillaBuffIds.WellFed.Value || buff == VanillaBuffIds.WellFed2.Value || buff == VanillaBuffIds.WellFed3.Value;
                bool supported = VanillaSelectedConsumableCatalog1458.TryGet(new ItemTypeId(id), out var definition);
                bool actual = supported && (definition.BuffType == VanillaBuffIds.WellFed.Value || definition.BuffType == VanillaBuffIds.WellFed2.Value || definition.BuffType == VanillaBuffIds.WellFed3.Value);
                Check(expected == actual);
                if (!expected) continue;
                foods++;
                Check(definition.BuffType == buff);
                Check(definition.BuffTime == item.GetProperty("buffTime").GetInt32());
                Check(definition.UseTime == item.GetProperty("useTime").GetInt32());
                Check(definition.UseAnimation == item.GetProperty("useAnimation").GetInt32());
                Check(definition.FoodColourCount == item.GetProperty("foodColourCount").GetInt32());
                Check(definition.DrinkColourCount == item.GetProperty("drinkColourCount").GetInt32());
                Check(definition.HealLife == item.GetProperty("healLife").GetInt32());
                Check(definition.HealMana == item.GetProperty("healMana").GetInt32());
                Check(item.GetProperty("consumable").GetBoolean() && !definition.HealingDelay);
                Check(VanillaSelectedConsumableCatalog1458.MayParticipateInQuickBuff(new ItemTypeId(id)));
            }
            Check(foods == 82);
        }
        Console.WriteLine($"FOOD_METADATA checks={checks}");
        return checks;
    }
}
