using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class BulletPrefixedLaunch1458Tests
{
    [Fact]
    public void Actual_source_prefixes_preserve_platform_item_stats_and_raw_selected_crit()
    {
        foreach (var (resource, arithmetic) in Profiles())
        {
            using var source = Read(resource);
            Assert.Equal(256, source.RootElement.GetProperty("profiles").GetArrayLength());
            foreach (var row in source.RootElement.GetProperty("profiles").EnumerateArray())
            {
                var item = new ItemTypeId(row.GetProperty("weapon").GetInt32());
                var prefix = new PrefixId(row.GetProperty("prefix").GetInt32());
                Assert.True(VanillaBulletWeaponStats1458.TryResolve(item, prefix, arithmetic, out var stats));
                var actual = row.GetProperty("weaponStats");
                Assert.Equal(actual.GetProperty("damage").GetInt32(), stats.Damage);
                Assert.Equal(actual.GetProperty("crit").GetInt32(), stats.ItemCrit);
                Assert.Equal(actual.GetProperty("useTime").GetInt32(), stats.UseTime);
                Assert.Equal(actual.GetProperty("useAnimation").GetInt32(), stats.Animation);
                EqualBits(actual.GetProperty("knockBack").GetSingle(), stats.KnockBack);
                EqualBits(actual.GetProperty("shootSpeed").GetSingle(), stats.ShootSpeed);
                Assert.True(VanillaSelectedItemCrit1458.TryResolve(item, prefix, arithmetic, out int crit));
                Assert.Equal(stats.ItemCrit, crit);
            }
        }
    }

    [Fact]
    public void Source_raw_crit_excludes_class_baseline_and_preserves_Lucy_exception()
    {
        foreach (var arithmetic in new[] { VanillaBulletSourceArithmetic1458.CoreClrSingle,
            VanillaBulletSourceArithmetic1458.WindowsClr4X86 })
        {
            // Actual SetDefaults metadata: Lucy has raw ten; ordinary pickaxe and consumables have zero.
            foreach (var (item, expected) in new[] { (5095, 10), (1, 0), (28, 0), (0, 0) })
            {
                Assert.True(VanillaSelectedItemCrit1458.TryResolve(new(item), default, arithmetic, out int crit));
                Assert.Equal(expected, crit);
            }
            Assert.False(VanillaSelectedItemCrit1458.TryResolve(new(32767), default, arithmetic, out _));
            Assert.False(VanillaSelectedItemCrit1458.TryResolve(new(28), new(82), arithmetic, out _));
            // Broken on Minishark is a genuine numeric rejection, even though it belongs to its rollable family.
            Assert.False(VanillaBulletWeaponStats1458.TryResolve(new(98), new(39), arithmetic, out _));
        }
        Assert.Equal(new(4, 4, 4), PlayerDerivedCritState1458.SourceBaseline);
    }

    [Fact]
    public void Configured_actual_Shoot_preserves_all_ordered_velocities_debit_and_complete_rng_state()
    {
        foreach (var (resource, arithmetic) in Profiles())
        {
            using var source = Read(resource);
            foreach (var row in source.RootElement.GetProperty("profiles").EnumerateArray())
            {
                Prepare(row, arithmetic, out var random, out var weapon, out var ammo, out var prefix, out float speed);
                var shots = new VanillaBulletLaunchShot1458[8];
                Assert.True(VanillaBulletWeaponLaunch1458.TryPlanFromAim(weapon.Type, 390, 0, 1, speed,
                    ammo.ProjectileType, random.Next, random.NextDouble, shots, out int count, arithmetic));
                var expected = row.GetProperty("shots");
                Assert.Equal(expected.GetArrayLength(), count);
                for (int index = 0; index < count; index++)
                {
                    var shot = expected[index];
                    EqualBits(shot.GetProperty("velocity").GetProperty("X").GetSingle(), shots[index].VelocityX);
                    EqualBits(shot.GetProperty("velocity").GetProperty("Y").GetSingle(), shots[index].VelocityY);
                    Assert.Equal(shot.GetProperty("damage").GetInt32(),
                        VanillaProjectileWeaponCombatCatalog.ResolveDamage(weapon, ammo, prefix,
                            VanillaPlayerCombatSnapshot.Baseline, arithmetic));
                    EqualBits(shot.GetProperty("knockBack").GetSingle(),
                        VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(weapon, ammo, prefix,
                            VanillaPlayerCombatSnapshot.Baseline));
                }
                EqualRandom(row.GetProperty("after"), random);
                Assert.Equal(row.GetProperty("nextAfter").GetInt32(), random.Clone().Next());
            }
        }
    }

    [Fact]
    public void First_actual_child_recovers_one_platform_volley_with_bounded_representation_error()
    {
        foreach (var (resource, arithmetic) in Profiles())
        {
            using var source = Read(resource);
            foreach (var row in source.RootElement.GetProperty("profiles").EnumerateArray())
            {
                Prepare(row, arithmetic, out var random, out var weapon, out var ammo, out _, out float speed);
                var expected = row.GetProperty("shots");
                var first = expected[0].GetProperty("velocity");
                var shots = new VanillaBulletLaunchShot1458[8];
                Assert.True(VanillaBulletWeaponLaunch1458.TryResolveFromFirstVelocity(weapon.Type,
                    first.GetProperty("X").GetSingle(), first.GetProperty("Y").GetSingle(), speed,
                    ammo.ProjectileType, random.Next, random.NextDouble, shots, out int count, arithmetic));
                Assert.Equal(expected.GetArrayLength(), count);
                for (int index = 0; index < count; index++)
                {
                    var velocity = expected[index].GetProperty("velocity");
                    Assert.InRange(MathF.Abs(velocity.GetProperty("X").GetSingle() - shots[index].VelocityX), 0, 0.0005f);
                    Assert.InRange(MathF.Abs(velocity.GetProperty("Y").GetSingle() - shots[index].VelocityY), 0, 0.0005f);
                }
                EqualRandom(row.GetProperty("after"), random);
            }
        }
    }

    private static void Prepare(JsonElement row, VanillaBulletSourceArithmetic1458 arithmetic,
        out VanillaUnifiedRandom1458 random, out VanillaProjectileWeaponCombatDefinition weapon,
        out VanillaProjectileAmmoCombatDefinition ammo, out VanillaCombatPrefixModifiers prefix, out float speed)
    {
        var type = new ItemTypeId(row.GetProperty("weapon").GetInt32());
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(type, out weapon));
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetBulletAmmo(new(97), out ammo));
        Assert.True(VanillaBulletWeaponStats1458.TryGetPrefixModifiers(type,
            new(row.GetProperty("prefix").GetInt32()), arithmetic, out prefix));
        random = new(row.GetProperty("seed").GetInt32());
        EqualRandom(row.GetProperty("before"), random);
        bool conserve = VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(weapon, ammo,
            VanillaPlayerCombatSnapshot.Baseline, random.Next,
            row.GetProperty("ammoBox").GetBoolean(), row.GetProperty("ammoPotion").GetBoolean());
        int stack = row.GetProperty("ammoBefore").GetProperty("stack").GetInt32();
        Assert.Equal(row.GetProperty("ammoAfter").GetProperty("stack").GetInt32(), conserve ? stack : stack - 1);
        speed = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(weapon, ammo, prefix,
            VanillaPlayerCombatSnapshot.Baseline).CanonicalMagnitude;
    }

    private static void EqualRandom(JsonElement source, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var state = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
        var cursor = (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!;
        Assert.Equal(source.GetProperty("state").EnumerateArray().Select(value => value.GetInt32()), state);
        Assert.Equal(source.GetProperty("cursor").GetUInt32(), cursor);
    }

    private static IEnumerable<(string Resource, VanillaBulletSourceArithmetic1458 Arithmetic)> Profiles()
    {
        yield return ("BulletPrefixedShootLinux1458", VanillaBulletSourceArithmetic1458.CoreClrSingle);
        yield return ("BulletPrefixedShootWindows1458", VanillaBulletSourceArithmetic1458.WindowsClr4X86);
    }

    private static void EqualBits(float expected, float actual) =>
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));

    private static JsonDocument Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
