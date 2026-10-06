using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class AmmoConservationBuffs1458Tests
{
    [Fact]
    public void Remote_buff_offers_match_original_consumption_and_random_cursor()
    {
        using JsonDocument source = Load();
        int compared = 0;
        foreach (JsonElement row in source.RootElement.GetProperty("shots").EnumerateArray())
        {
            if (!row.GetProperty("canShoot").GetBoolean()) continue;
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(
                new ItemTypeId(row.GetProperty("weapon").GetInt32()), out var weapon));
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetAmmo(weapon.AmmoFamily,
                new ItemTypeId(row.GetProperty("ammo").GetInt32()), out var ammo));
            var attacker = VanillaPlayerCombatSnapshot.Baseline with
            {
                MagicQuiver = row.GetProperty("magicQuiver").GetBoolean()
            };
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            bool conserve = ProjectileAuthority.PrepareAmmoConservation(in weapon, in ammo, in attacker, random,
                row.GetProperty("ammoBox").GetBoolean(), row.GetProperty("ammoPotion").GetBoolean());
            Assert.Equal(row.GetProperty("afterStack").GetInt32(),
                row.GetProperty("initialStack").GetInt32() - (conserve ? 0 : 1));
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
            compared++;
        }
        Assert.Equal(30, compared);
    }

    [Fact]
    public void Tungsten_defaults_and_gun_launch_math_match_actual_original_PickAmmo()
    {
        using JsonDocument source = Load();
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetBulletAmmo(VanillaItemIds.TungstenBullet, out var ammo));
        Assert.True(VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(VanillaPrefixIds.None, out var prefix));
        var attacker = VanillaPlayerCombatSnapshot.Baseline;
        int compared = 0;
        foreach (JsonElement row in source.RootElement.GetProperty("shots").EnumerateArray())
        {
            if (row.GetProperty("ammo").GetInt32() != VanillaItemIds.TungstenBullet.Value) continue;
            Assert.Equal(row.GetProperty("ammoDamage").GetInt32(), ammo.Damage);
            Assert.Equal(row.GetProperty("ammoKnockBack").GetSingle(), ammo.KnockBack);
            Assert.Equal(row.GetProperty("ammoSpeed").GetSingle(), ammo.ShootSpeed);
            Assert.Equal(row.GetProperty("ammoShoot").GetInt32(), ammo.ProjectileType.Value);
            Assert.Equal(row.GetProperty("consumable").GetBoolean(), ammo.Consumable);
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(
                new ItemTypeId(row.GetProperty("weapon").GetInt32()), out var weapon));
            Assert.Equal(row.GetProperty("damage").GetInt32(),
                VanillaProjectileWeaponCombatCatalog.ResolveDamage(in weapon, in ammo, in prefix, in attacker));
            Assert.Equal(row.GetProperty("speed").GetSingle(),
                VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(in weapon, in ammo, in prefix, in attacker).CanonicalMagnitude);
            Assert.Equal(row.GetProperty("knockBack").GetSingle(),
                VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(in weapon, in ammo, in prefix, in attacker));
            compared++;
        }
        Assert.Equal(5, compared);
    }

    [Fact]
    public void Owned_remote_buff_slots_match_source_report_and_death_clear()
    {
        using JsonDocument source = Load();
        JsonElement lifecycle = source.RootElement.GetProperty("lifecycle");
        JsonElement reported = lifecycle[0];
        var buffs = new PlayerBuffState();
        buffs.ReplaceNetworkSnapshot(reported.GetProperty("buffTypes").EnumerateArray()
            .Where(static value => value.GetInt32() != 0)
            .Select(static value => new BuffTypeId(value.GetInt32())).ToArray());
        Assert.Equal(reported.GetProperty("ammoBox").GetBoolean(), buffs.CountActive(new BuffTypeId(93)) > 0);
        Assert.Equal(reported.GetProperty("ammoPotion").GetBoolean(), buffs.CountActive(new BuffTypeId(112)) > 0);
        Assert.Equal(reported.GetProperty("buffTimes")[0].GetInt32(), buffs.GetDuration(new BuffTypeId(93)));
        Assert.Equal(reported.GetProperty("buffTimes")[1].GetInt32(), buffs.GetDuration(new BuffTypeId(112)));
        Assert.True(buffs.ClearNonPersistentOnDeath());
        Assert.Equal(lifecycle[1].GetProperty("buffTypes").EnumerateArray()
            .Where(static value => value.GetInt32() != 0).Select(static value => value.GetInt32()),
            buffs.CaptureTypes().Select(static value => value.Value));
        // UpdateDead itself retains the old effect fields; firing remains dead-gated.
        // The next ResetEffects/UpdateBuffs phase projects the now-empty slots.
        Assert.Equal(lifecycle[2].GetProperty("ammoBox").GetBoolean(), buffs.CountActive(new BuffTypeId(93)) > 0);
        Assert.Equal(lifecycle[2].GetProperty("ammoPotion").GetBoolean(), buffs.CountActive(new BuffTypeId(112)) > 0);
    }

    private static JsonDocument Load()
    {
        using Stream source = typeof(AmmoConservationBuffs1458Tests).Assembly.GetManifestResourceStream("AmmoConservationBuffs1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
