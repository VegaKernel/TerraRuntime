using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class ProjectileAmmoConservationSource1458Tests
{
    [Fact]
    public void Accepted_conservation_matches_original_PickAmmo_consumption_and_next_random()
    {
        using Stream source = typeof(ProjectileAmmoConservationSource1458Tests).Assembly
            .GetManifestResourceStream("ProjectileAmmoConservation1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        int compared = 0;
        foreach (JsonElement row in document.RootElement.EnumerateArray())
        {
            // The fixture also preserves actual no-ammo, dontConsume and active buff captures.
            // Those are resolver/unsupported-context boundaries, not inputs of this helper.
            if (!row.GetProperty("canShoot").GetBoolean() ||
                row.GetProperty("dontConsume").GetBoolean() || row.GetProperty("buff").GetInt32() != 0)
                continue;

            var weaponType = new ItemTypeId(row.GetProperty("weapon").GetInt32());
            Assert.Equal(weaponType.Value, row.GetProperty("weaponDefaultType").GetInt32());
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(weaponType, out var weapon));
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetAmmo(weapon.AmmoFamily,
                new ItemTypeId(row.GetProperty("ammo").GetInt32()), out var ammo));
            Assert.Equal(ammo.Consumable, row.GetProperty("consumable").GetBoolean());
            var attacker = VanillaPlayerCombatSnapshot.Baseline with
            {
                MagicQuiver = row.GetProperty("magicQuiver").GetBoolean()
            };
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            bool conserve = ProjectileAuthority.PrepareAmmoConservation(in weapon, in ammo, in attacker, random);
            int expectedStack = row.GetProperty("initialStack").GetInt32() - (conserve ? 0 : 1);
            Assert.Equal(row.GetProperty("afterStack").GetInt32(), expectedStack);
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
            compared++;
        }
        Assert.Equal(29, compared);
    }
}
