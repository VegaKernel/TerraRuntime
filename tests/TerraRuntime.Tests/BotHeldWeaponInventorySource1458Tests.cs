using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using Xunit;
using Fixture = TerraRuntime.Tests.BotAmmoIndependentSource1458Tests.Fixture;

namespace TerraRuntime.Tests;

// Source selected-item facts are independent references. Exact preset matching,
// positive canonical stacks and predictive single-child attacks remain BOT policy.
public sealed class BotHeldWeaponInventorySource1458Tests
{
    [Fact]
    public void Actual_policy_slot_and_represented_prefix_own_the_accepted_weapon()
    {
        using var stream = typeof(BotHeldWeaponInventorySource1458Tests).Assembly.GetManifestResourceStream("BotHeldWeaponSourceCells1458");
        Assert.NotNull(stream);
        using var decoded = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(decoded);
        Assert.Equal("ba6a8185de4944e8dd9818d772ec902d3af94cb5d7d5cc62c01bde119fac6394", source.RootElement.GetProperty("sourceManifestSha256").GetString());
        Assert.Equal("3af581613951dc71a93091edf0ae1426d466096f056c9d503b3fc5430f51066e", source.RootElement.GetProperty("sourceFixtureSha256").GetString());
        // Source nonconsumable98 stack0 still shoots; canonical BOT positive-stack admission is a separate policy.
        Assert.Equal(1, source.RootElement.GetProperty("cells")[8].GetProperty("childCount").GetInt32());
        Assert.Equal(0, source.RootElement.GetProperty("cells")[8].GetProperty("spec").GetProperty("WeaponStack").GetInt32());
        Assert.Equal(14, source.RootElement.GetProperty("cells").GetArrayLength());
        foreach (var row in source.RootElement.GetProperty("cells").EnumerateArray())
        foreach (var arithmetic in new[] { VanillaBulletSourceArithmetic1458.CoreClrSingle, VanillaBulletSourceArithmetic1458.WindowsClr4X86 })
        {
            var spec = row.GetProperty("spec");
            int type = spec.GetProperty("Weapon").GetInt32();
            if (row.GetProperty("childCount").GetInt32() != 1 || spec.GetProperty("WeaponStack").GetInt32() <= 0) continue;
            int ammo = spec.GetProperty("Ammo").GetInt32();
            byte prefix = spec.GetProperty("Prefix").GetByte();
            var f = new Fixture(type, 20, ammo, itemPrefixArithmetic: arithmetic);
            Assert.True(f.Players.SetItem(f.Id, new(0, new(type), 1, new(prefix), 0)));
            Assert.Equal(RuntimeBotActionResult.Success.Status, f.Attack().Status);
            Assert.True(f.Shots.TryGetActive(0, out var shot));
            var child = row.GetProperty("children")[0];
            Assert.Equal(child.GetProperty("type").GetInt32(), shot.Type.Value);
            Assert.Equal(child.GetProperty("damage").GetInt32(), shot.Damage);
            Assert.Equal(child.GetProperty("knockBack").GetSingle(), shot.KnockBack);
            Assert.Equal(row.GetProperty("itemTime").GetInt32(), f.Bot.NextAttackTick);
        }
        var presets = Enumerable.Range(0, 100).Select(seed => RuntimePlayerBotLoadoutCatalog1458.Pick(new Random(seed))).DistinctBy(x => x.Name).ToArray();
        Assert.Equal(4, presets.Length);
        foreach (var preset in presets)
        foreach (bool gun in new[] { false, true })
        foreach (bool automatic in new[] { false, true })
        {
            int type = (gun ? preset.GunWeapon : preset.BowWeapon).Value;
            int ammo = (gun ? preset.BulletAmmo : preset.ArrowAmmo).Value;
            var f = new Fixture(type, 20, ammo);
            var policy = automatic ? RuntimeBotWeaponPolicy.Automatic : gun ? RuntimeBotWeaponPolicy.Gun : RuntimeBotWeaponPolicy.Bow;
            var config = f.Bot.Configuration with { WeaponPolicy = policy };
            var bot = new BotState(f.Bot.Id, f.Id, "preset", config, preset, default, default, 0) { Player = f.Bot.Player, ObservationRevision = 1 };
            byte slot = automatic ? (byte)(gun ? 2 : 1) : (byte)0;
            if (automatic) Assert.True(f.Players.SetItem(f.Id, new(0, new(3), 1, default, 0)));
            Assert.True(f.Players.SetItem(f.Id, new(slot, new(type), 1, default, 0)));
            if (automatic && gun) Assert.True(f.Players.TryTeleport(f.TargetId, 900, 160));
            var observation = f.Observation() with { Configuration = config, GuardTarget = new BotGuardTarget(default, f.Target, automatic && gun ? 910 : 310, 181, 0, 0, 20, 42) };
            var combat = new RuntimeBotCombat(bot, f.Players, null!, f.Projectiles, f.Tiles, f.Inventory, [bot], f.World);
            Assert.Equal(RuntimeBotActionResult.Success.Status, combat.Attack(observation).Status);
            Assert.True(f.Players.TryGet(bot.Player, out var self));
            Assert.Equal(slot, self.SelectedItem);
            Assert.Equal(1, f.Shots.ActiveCount);
            Assert.True(f.Players.TryGetItem(f.Id, 54, out var remaining));
            Assert.Equal(19, remaining.Stack);
        }
        // Original local ItemCheck may shoot actual219 despite an external98 preference;
        // this producer deliberately requires the configured identity in its policy slot.
        foreach (int retained in new[] { 0, 3, 39, 219 })
        {
            var f = new Fixture(98, 20);
            Assert.True(f.Players.SetItem(f.Id, new(0, new(retained), retained == 0 ? (short)0 : (short)1, default, 0)));
            f.Events.Seen.Clear();
            Assert.Equal(RuntimeBotActionResult.Pending().Status, f.Attack().Status);
            Assert.Empty(f.Events.Seen);
            Assert.Equal(0, f.Shots.ActiveCount);
            Assert.Equal(0, f.Bot.NextAttackTick);
            Assert.True(f.Players.TryGetItem(f.Id, 54, out var ammo));
            Assert.Equal(20, ammo.Stack);
        }
        foreach (var pair in new[] { (98, 40), (39, 97) })
        {
            var f = new Fixture(pair.Item1, 20, pair.Item2);
            Assert.Equal(RuntimeBotActionResult.Pending().Status, f.Attack().Status);
            Assert.Equal(0, f.Shots.ActiveCount);
        }
    }

    [Fact]
    public void Held_tuple_census_refuses_late_changes_and_preserves_newer_accepted_state()
    {
        foreach (var mutation in new[] { (0, 0), (219, 0), (98, 36) })
        {
            var f = new Fixture(98, 20);
            f.Events.Seen.Clear();
            f.OnTick = () => { f.OnTick = null; Assert.True(f.Players.SetItem(f.Id, new(0, new(mutation.Item1), mutation.Item1 == 0 ? (short)0 : (short)1, new((byte)mutation.Item2), 0))); };
            Assert.Equal(RuntimeBotActionResult.Pending().Status, f.Attack().Status);
            Assert.Equal(0, f.Shots.ActiveCount);
            Assert.Equal(0, f.Projectiles.AppliedSpawns);
            Assert.Equal(0, f.Bot.NextAttackTick);
            Assert.Equal(0, f.Bot.UseItemUntilTick);
            Assert.DoesNotContain("presentation", f.Events.Seen);
            Assert.DoesNotContain("spawn", f.Events.Seen);
            Assert.True(f.Players.TryGetItem(f.Id, 54, out var ammo));
            Assert.Equal(20, ammo.Stack);
        }
        foreach (var mutation in new[] { (219, 0), (98, 36) })
        {
            var f = new Fixture(98, 20, replication: true);
            bool first = true;
            f.Events.Observe = kind =>
            {
                if (!first) return;
                first = false;
                f.AssertAccepted(20);
                Assert.True(f.Players.SetItem(f.Id, new(0, new(mutation.Item1), 1, new((byte)mutation.Item2), 0)));
                Assert.Equal(RuntimeBotActionResult.Pending().Status, f.Attack().Status);
            };
            Assert.Equal(RuntimeBotActionResult.Success.Status, f.Attack().Status);
            Assert.False(first);
            Assert.True(f.Players.TryGetItem(f.Id, 0, out var held));
            Assert.Equal(mutation.Item1, held.ItemType.Value);
            Assert.Equal(mutation.Item2, held.Prefix.Value);
            Assert.Equal(1, f.Shots.ActiveCount);
            Assert.Equal(1, f.Projectiles.AppliedSpawns);
            // The still-current presentation component may publish; it does not rewrite the newer tuple.
        }
    }
}
