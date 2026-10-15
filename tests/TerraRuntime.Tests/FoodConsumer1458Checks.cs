using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
namespace TerraRuntime.Tests;
internal static class FoodConsumer1458Checks
{
    internal static int Run()
    {
        int checks = 0, rows = 0, healthRows = 0, legacyWindowsBoundaries = 0;
        void Check(bool okay, string label) { if (!okay) throw new InvalidOperationException($"Food consumer {label}: row={rows}, health={healthRows}"); checks++; }
        void Compare(in VanillaPlayerCombatSnapshot model, JsonElement source)
        {
            Check(model.Defense == I(source, "statDefense"), "defense");
            Check(model.MeleeCrit == I(source, "meleeCrit") && model.RangedCrit == I(source, "rangedCrit") && model.MagicCrit == I(source, "magicCrit"), "all three crit lanes");
            Check(model.WellFed == source.GetProperty("wellFed").GetBoolean(), "wellFed");
            var bits = source.GetProperty("combatBits");
            foreach (var field in new (string Name, float Value)[]
            {
                ("meleeDamage", model.MeleeDamage), ("rangedDamage", model.RangedDamage),
                ("magicDamage", model.MagicDamage), ("minionDamage", model.MinionDamage),
                ("meleeSpeed", model.MeleeAttackSpeed), ("minionKB", model.MinionKnockback),
                ("moveSpeed", model.MoveSpeed), ("pickSpeed", model.PickSpeed)
            }) Check(BitConverter.SingleToInt32Bits(field.Value) == I(bits, field.Name), "exact Single " + field.Name);
        }
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using (var input = typeof(FoodConsumer1458Checks).Assembly.GetManifestResourceStream($"FoodConsumers{platform}1458")!)
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var data = JsonDocument.Parse(gzip))
            foreach (var row in data.RootElement.EnumerateArray())
            {
                if (row.GetProperty("kind").GetString() != "capped-mixed-consumer") continue;
                var step = row.GetProperty("steps").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "UpdateBuffs");
                var before = step.GetProperty("before");
                var initial = From(before);
                var active = before.GetProperty("buffType").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32())).ToArray();
                Check(VanillaPlayerCombatBuffs1458.TryApply(in initial, active, out var buffed), "indexed source buff admission");
                Compare(in buffed, step.GetProperty("after"));
                int gear = I(row, "gear");
                PlayerEquipmentCommitRequest[] equipment = gear == 0 ? [] :
                    [new(new PlayerSlotId(0), 60, 1, 0, (short)gear, 0)];
                var context = new VanillaPlayerCombatEquipmentContext(null, false, false);
                Check(VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, in context, in buffed, out var final, out int mana), "source buff-before-equipment");
                Check(mana == 0, "no mana projection");
                if (gear != 0)
                {
                    var grant = row.GetProperty("steps").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "GrantArmorBenefits");
                    Compare(in final, grant.GetProperty("after"));
                }
                var capped = row.GetProperty("steps").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "CapAttackSpeeds").GetProperty("after");
                Check(BitConverter.SingleToInt32Bits(final.MeleeAnimationMultiplier) == I(capped.GetProperty("combatBits"), "meleeSpeed"), "source cap and reciprocal");
                foreach (var weapon in row.GetProperty("weapons").EnumerateArray())
                {
                    int id = I(weapon, "id");
                    if (!VanillaRemoteMeleeItemCheck1458.IsSupported(new(id))) continue;
                    var facts = new PlayerRemoteMeleeItemFacts1458(new(id), true, true, false, false, false);
                    var random = new TerraRuntime.Core.VanillaUnifiedRandom1458(0);
                    Check(VanillaRemoteMeleeItemCheck1458.TryStep(VanillaSelectedConsumable1458.ConstructorState,
                        in facts, random.Next, out var use, final.MeleeAnimationMultiplier), "existing remote melee caller");
                    Check(use.State.AnimationMax == I(weapon, "animationMax"), "actual ApplyItemAnimation endpoint");
                }
                rows++;
            }
            using (var input = typeof(FoodConsumer1458Checks).Assembly.GetManifestResourceStream($"FoodBehavior{platform}1458")!)
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var data = JsonDocument.Parse(gzip))
            foreach (var row in data.RootElement.EnumerateArray())
            {
                if (row.GetProperty("kind").GetString() != "consumer") continue;
                var step = row.GetProperty("steps").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "UpdateLifeRegen");
                var before = step.GetProperty("before");
                var after = step.GetProperty("after");
                var prior = new PlayerNpcHealthState1458(I(before, "statLife"), I(before, "lifeRegenCount"), F(before, "lifeRegenTime"));
                bool fed = before.GetProperty("wellFed").GetBoolean();
                var actual = VanillaRemotePlayerHealth1458.Step(prior, I(before, "statLifeMax2"), false, false, false, true,
                    row.GetProperty("expert").GetBoolean(), false, 0, false, false, false, false, fed);
                Check(actual is not null, "represented regen");
                bool knownOldBoundary = platform == "Windows" && I(row, "tier") == 0 && row.GetProperty("expert").GetBoolean() && I(row, "regenTime") == 1200;
                if (knownOldBoundary)
                {
                    // Original x86 retains an existing un-fed midpoint wider than CoreCLR.
                    // This source diagnostic predates food and is not rewritten as a food oracle.
                    Check(!fed && actual!.Value.RegenCount == 2 && I(after, "lifeRegenCount") == 3,
                        "explicit existing Windows no-food rounding boundary");
                    legacyWindowsBoundaries++;
                }
                else Check(actual!.Value.RegenCount == I(after, "lifeRegenCount"), "exact source regen count");
                Check(actual!.Value.Life == I(after, "statLife"), "source regenerated life");
                Check(BitConverter.SingleToInt32Bits(actual.Value.RegenTime!.Value) == I(after.GetProperty("combatBits"), "lifeRegenTime"), "exact source regen-time bits");
                healthRows++;
            }
        }
        var nonFiniteSpeed = VanillaPlayerCombatSnapshot.Baseline with { MeleeAttackSpeed = float.NaN };
        Check(!VanillaPlayerCombatBuffs1458.TryApply(in nonFiniteSpeed, [VanillaBuffIds.WellFed], out _),
            "non-finite imported attack speed refuses the represented projection");
        Check(rows == 64 && healthRows == 64 && legacyWindowsBoundaries == 2, "complete scoped consumer matrix");
        Console.WriteLine($"FOOD_CONSUMERS rows={rows} regenRows={healthRows} legacyWindowsNoFoodBoundaries={legacyWindowsBoundaries} checks={checks}");
        return checks;
    }
    private static VanillaPlayerCombatSnapshot From(JsonElement e) => VanillaPlayerCombatSnapshot.Baseline with
    {
        Defense = I(e, "statDefense"), MeleeCrit = I(e, "meleeCrit"), RangedCrit = I(e, "rangedCrit"), MagicCrit = I(e, "magicCrit"),
        MeleeDamage = F(e, "meleeDamage"), RangedDamage = F(e, "rangedDamage"), MagicDamage = F(e, "magicDamage"), MinionDamage = F(e, "minionDamage"),
        MeleeAttackSpeed = F(e, "meleeSpeed"), MinionKnockback = F(e, "minionKB"), MoveSpeed = F(e, "moveSpeed"), PickSpeed = F(e, "pickSpeed"),
        WellFed = e.GetProperty("wellFed").GetBoolean()
    };
    private static int I(JsonElement e, string n) => e.GetProperty(n).GetInt32();
    private static float F(JsonElement e, string n) => e.GetProperty(n).GetSingle();
}
