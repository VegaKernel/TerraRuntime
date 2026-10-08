using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class RangedBulletLaunch1458Tests
{
    [Fact]
    public void Actual_ItemCheck_Shoot_matches_all_source_shots_ammo_and_final_cursor()
    {
        using var source = Read();
        Span<VanillaBulletLaunchShot1458> shots = stackalloc VanillaBulletLaunchShot1458[8];
        foreach (var row in source.RootElement.GetProperty("launches").EnumerateArray())
        {
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(new(row.GetProperty("weapon").GetInt32()), out var weapon));
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetAmmo(weapon.AmmoFamily, new(row.GetProperty("ammo").GetInt32()), out var ammo));
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            var combat = VanillaPlayerCombatSnapshot.Baseline;
            bool conserve = VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(weapon, ammo, combat,
                random.Next, row.GetProperty("ammoBox").GetBoolean(), row.GetProperty("ammoPotion").GetBoolean());
            int stack = row.GetProperty("initialAmmo").GetInt32();
            Assert.Equal(row.GetProperty("afterAmmo").GetInt32(), conserve ? stack : stack - 1);
            Assert.True(VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(
                new(row.TryGetProperty("prefix", out var prefixValue) ? prefixValue.GetInt32() : 0), out var prefix));
            float speed = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(weapon, ammo, prefix, combat).CanonicalMagnitude;
            Assert.True(VanillaBulletWeaponLaunch1458.TryPlanFromAim(weapon.Type,
                row.TryGetProperty("aimDeltaX", out var aimX) ? aimX.GetSingle() :
                    row.GetProperty("aimDirection").GetInt32() == 1 ? 390f : -410f,
                row.TryGetProperty("aimDeltaY", out var aimY) ? aimY.GetSingle() : 0f,
                row.GetProperty("direction").GetInt32(), speed, ammo.ProjectileType,
                random.Next, random.NextDouble, shots, out int count));
            var expected = row.GetProperty("shots");
            Assert.Equal(expected.GetArrayLength(), count);
            for (int index = 0; index < count; index++)
            {
                var shot = expected[index];
                Assert.Equal(shot.GetProperty("velocity").GetProperty("X").GetSingle(), shots[index].VelocityX);
                Assert.Equal(shot.GetProperty("velocity").GetProperty("Y").GetSingle(), shots[index].VelocityY);
                Assert.Equal(shot.GetProperty("damage").GetInt32(), VanillaProjectileWeaponCombatCatalog.ResolveDamage(weapon, ammo, prefix, combat));
                Assert.Equal(shot.GetProperty("knockBack").GetSingle(), VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(weapon, ammo, prefix, combat));
            }
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        }
    }

    [Fact]
    public void First_source_child_recovers_bounded_ordered_volley_and_same_cursor()
    {
        using var source = Read();
        Span<VanillaBulletLaunchShot1458> shots = stackalloc VanillaBulletLaunchShot1458[8];
        foreach (var row in source.RootElement.GetProperty("launches").EnumerateArray())
        {
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(new(row.GetProperty("weapon").GetInt32()), out var weapon));
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetAmmo(weapon.AmmoFamily, new(row.GetProperty("ammo").GetInt32()), out var ammo));
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            var combat = VanillaPlayerCombatSnapshot.Baseline;
            _ = VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(weapon, ammo, combat,
                random.Next, row.GetProperty("ammoBox").GetBoolean(), row.GetProperty("ammoPotion").GetBoolean());
            Assert.True(VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(
                new(row.TryGetProperty("prefix", out var prefixValue) ? prefixValue.GetInt32() : 0), out var prefix));
            float speed = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(weapon, ammo, prefix, combat).CanonicalMagnitude;
            var expected = row.GetProperty("shots");
            var first = expected[0].GetProperty("velocity");
            Assert.True(VanillaBulletWeaponLaunch1458.TryResolveFromFirstVelocity(weapon.Type,
                first.GetProperty("X").GetSingle(), first.GetProperty("Y").GetSingle(), speed, ammo.ProjectileType,
                random.Next, random.NextDouble, shots, out int count));
            Assert.Equal(expected.GetArrayLength(), count);
            for (int index = 0; index < count; index++)
            {
                Assert.InRange(MathF.Abs(expected[index].GetProperty("velocity").GetProperty("X").GetSingle() - shots[index].VelocityX), 0f, 0.0005f);
                Assert.InRange(MathF.Abs(expected[index].GetProperty("velocity").GetProperty("Y").GetSingle() - shots[index].VelocityY), 0f, 0.0005f);
            }
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        }
    }

    [Fact]
    public void Sparse_weapon_defaults_and_item_specific_prefix_rounding_match_original_items()
    {
        using var source = Read();
        foreach (var row in source.RootElement.GetProperty("defaults").EnumerateArray())
        {
            var type = new ItemTypeId(row.GetProperty("item").GetInt32());
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(type, out var weapon));
            var requested = new PrefixId(row.GetProperty("prefix").GetInt32());
            Assert.Equal(row.GetProperty("actualPrefix").GetInt32() == requested.Value,
                VanillaProjectileWeaponCombatCatalog.IsPrefixSupported(type, requested));
            if (requested.Value != 0) continue;
            Assert.Equal(row.GetProperty("damage").GetInt32(), weapon.BaseDamage);
            Assert.Equal(row.GetProperty("knockBack").GetSingle(), weapon.BaseKnockBack);
            Assert.Equal(row.GetProperty("shootSpeed").GetSingle(), weapon.BaseShootSpeed);
            Assert.Equal(row.GetProperty("shoot").GetInt32(), weapon.BaseProjectileType.Value);
            Assert.Equal(row.GetProperty("useTime").GetInt32(), weapon.UseTimeTicks);
            Assert.Equal(row.GetProperty("useAnimation").GetInt32(), weapon.AnimationTicks);
            Assert.Equal(VanillaProjectileAmmoFamily.Bullet, weapon.AmmoFamily);
        }
    }

    [Fact]
    public void Unsupported_or_nonfinite_aim_rejects_before_any_draw_and_inverse_cannot_invent_damping()
    {
        Span<VanillaBulletLaunchShot1458> shots = stackalloc VanillaBulletLaunchShot1458[8];
        var random = new VanillaUnifiedRandom1458(0);
        var before = random.Clone();
        Assert.False(VanillaBulletWeaponLaunch1458.TryPlanFromAim(new(1254), 1, 0, 1, 10, new(14), random.Next, random.NextDouble, shots, out _));
        Assert.False(VanillaBulletWeaponLaunch1458.TryPlanFromAim(new(98), float.NaN, 0, 1, 11, new(14), random.Next, random.NextDouble, shots, out _));
        Assert.False(VanillaBulletWeaponLaunch1458.TryPlanFromAim(new(98), 1, 0, 1, 11, new(242), random.Next, random.NextDouble, shots, out _));
        Assert.False(VanillaBulletWeaponLaunch1458.TryPlanFromAim(new(98), 1, 0, 1, 11, new(14), random.Next, random.NextDouble, shots[..7], out _));
        Assert.True(random.HasSameState(before));
        Assert.False(VanillaBulletWeaponLaunch1458.TryResolveFromFirstVelocity(new(219), 2, 0, 17, new(14), random.Next, random.NextDouble, shots, out _));
        Assert.True(random.HasSameState(before));

        // PickAmmo evaluates the later potion and weapon offers even when Ammo Box already conserved.
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(VanillaItemIds.ChainGun, out var chain));
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetAmmo(chain.AmmoFamily, new(97), out var bullet));
        var offers = new List<int>();
        Assert.True(VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(chain, bullet,
            VanillaPlayerCombatSnapshot.Baseline, (minimum, maximum) =>
            {
                Assert.Equal(0, minimum);
                offers.Add(maximum);
                return 0;
            }, ammoBox: true, ammoPotion: true));
        Assert.Equal(new[] { 5, 5, 3 }, offers);
    }

    private static JsonDocument Read()
    {
        using var stream = typeof(RangedBulletLaunch1458Tests).Assembly.GetManifestResourceStream("RangedBulletLaunch1458");
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
