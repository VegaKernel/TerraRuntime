using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class HumanChanneledMagicPrefix1458Tests
{
    [Fact]
    public void Original_stored_prefixes_advance_the_owned_phase_with_exact_clocks_and_random()
    {
        int phases = 0;
        foreach (bool windows in new[] { false, true })
        {
            using var document = ReadSource("MagicPrefixPhase1458");
            foreach (var row in document.RootElement.GetProperty(windows ? "WindowsStats" : "LinuxStats").EnumerateArray())
            {
                Assert.True(VanillaChanneledMagicWeaponStats1458.TryResolve(new(row.GetProperty("type").GetInt32()),
                    new(row.GetProperty("requested").GetInt32()), windows, out var weapon, out int crit));
                var item = row.GetProperty("item");
                Assert.Equal(item.GetProperty("damage").GetInt32(), weapon.BaseDamage);
                Assert.Equal(item.GetProperty("mana").GetInt32(), weapon.ManaCost);
                Assert.Equal(item.GetProperty("useTime").GetInt32(), weapon.UseTimeTicks);
                Assert.Equal(item.GetProperty("useAnimation").GetInt32(), weapon.AnimationTicks);
                Assert.Equal(item.GetProperty("crit").GetInt32(), crit);
                Assert.Equal(item.GetProperty("knockBack").GetSingle(), weapon.BaseKnockBack);
                Assert.Equal(item.GetProperty("shootSpeed").GetSingle(), weapon.BaseShootSpeed);
            }
            foreach (int type in new[] { 113, 218, 495 })
            foreach (byte prefix in new byte[] { 1, 62, 84, 255 })
                Assert.False(VanillaChanneledMagicWeaponStats1458.TryResolve(new(type), new(prefix), windows, out _, out _));
            foreach (var row in document.RootElement.GetProperty(windows ? "Windows" : "Linux").EnumerateArray())
            {
                using var f = new HumanManaFlowerAuthority1458Tests.Harness.Fixture(row, windows);
                foreach (var item in row.GetProperty("initialInventory").EnumerateArray())
                    f.Equipment(item.GetProperty("index").GetInt16(), item.GetProperty("type").GetInt16(), item.GetProperty("stack").GetInt16(), item.GetProperty("prefix").GetByte());
                f.FundSourceManaPotions();
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    f.PrepareStep(step);
                    var before = step.GetProperty("beforePlayer");
                    int tick = step.GetProperty("tick").GetInt32();
                    bool use = row.GetProperty("spec").GetProperty("branch").GetString() == "living" && tick > 0 && tick != 5;
                    f.Move(use, before.GetProperty("selectedItem").GetByte(), before.GetProperty("lastItemUseAttemptSuccess").GetBoolean());
                    if (row.GetProperty("spec").GetProperty("branch").GetString() == "ghost" && tick > 0) f.Member.MovementFlags |= 64;
                    f.State.Tick();
                    Assert.True(f.Member.ItemPhase is not null, row.GetProperty("spec").GetProperty("name").GetString() + "/windows=" + windows + "/tick=" + tick);
                    var actual = f.Member.ItemPhase!.Value;
                    var expected = step.GetProperty("player");
                    Assert.Equal(expected.GetProperty("statMana").GetInt32(), f.Member.Mana);
                    Assert.Equal(expected.GetProperty("statManaMax2").GetInt32(), actual.Mana.Maximum);
                    Assert.Equal(expected.GetProperty("manaRegenCount").GetInt32(), actual.Mana.Count);
                    Assert.Equal(expected.GetProperty("manaRegenDelay").GetSingle(), actual.Mana.Delay);
                    Assert.Equal(expected.GetProperty("manaHeat").GetSingle(), actual.ManaHeat);
                    Assert.Equal(expected.GetProperty("itemAnimation").GetInt32(), actual.Selected.Animation);
                    Assert.Equal(expected.GetProperty("itemAnimationMax").GetInt32(), actual.Selected.AnimationMax);
                    Assert.Equal(expected.GetProperty("itemTime").GetInt32(), actual.Selected.ItemTime);
                    Assert.Equal(expected.GetProperty("itemTimeMax").GetInt32(), actual.Selected.ItemTimeMax);
                    Assert.Equal(expected.GetProperty("channel").GetBoolean(), actual.Channel);
                    Assert.Equal(expected.GetProperty("slowMagicUse").GetBoolean(), actual.SlowMagicUse);
                    Assert.Equal(expected.GetProperty("releaseUseItem").GetBoolean(), actual.Selected.ReleaseUseItem);
                    Assert.Equal(expected.GetProperty("pendingItemReuse").GetBoolean(), actual.PendingItemReuse);
                    Assert.NotNull(actual.DerivedCombat);
                    Assert.Equal(expected.GetProperty("magicCrit").GetInt32(), actual.DerivedCombat!.Value.MagicCrit);
                    Assert.Equal(expected.GetProperty("magicDamage").GetSingle(), actual.DerivedCombat.Value.MagicDamage);
                    Assert.Equal(expected.GetProperty("manaCost").GetSingle(), actual.DerivedCombat.Value.ManaCostMultiplier);
                    var rng = step.GetProperty("after");
                    Assert.Equal(rng.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Random)!);
                    Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Random)!);
                    Assert.Equal(rng.GetProperty("publicNext").GetInt32(), f.Random.Clone().Next());
                    phases++;
                }
            }
        }
        Assert.Equal(2688, phases);
    }

    [Fact]
    public void Source_prefixed_births_pay_once_in_both_orders_with_optional_flower_refill()
    {
        int births = 0;
        foreach (bool windows in new[] { false, true })
        {
            using var source = ReadSource("MagicPrefixShoot1458");
            using var phases = ReadSource("MagicPrefixPhase1458");
            foreach (var row in source.RootElement.GetProperty(windows ? "Windows" : "Linux").EnumerateArray())
            foreach (bool phaseFirst in new[] { false, true })
            {
                int weapon = row.GetProperty("weapon").GetInt32(), prefix = row.GetProperty("prefix").GetInt32();
                bool flower = row.GetProperty("flower").GetBoolean();
                string profileName = $"{weapon}-p{prefix}-living";
                var profile = phases.RootElement.GetProperty(windows ? "Windows" : "Linux").EnumerateArray().Single(r => r.GetProperty("spec").GetProperty("name").GetString() == profileName);
                using var f = new HumanManaFlowerAuthority1458Tests.Harness.Fixture(profile, windows);
                if (flower) f.Equipment(62, 555, 1);
                f.Equipment(2, 110, 3);
                f.FundSourceManaPotions();
                int mana = row.GetProperty("initialMana").GetInt32();
                f.State.Apply(new PlayerManaRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, (short)mana, 200)));
                f.Move(false, 0, true); f.State.Tick(); f.Move(true, 0, true);
                if (phaseFirst) f.State.Tick();
                var shot = row.GetProperty("shots")[0]; var position = shot.GetProperty("position"); var velocity = shot.GetProperty("velocity");
                var packet = new TerrariaProjectileUpdateState(new(0, 901, 1), shot.GetProperty("type").GetInt32(),
                    VectorComponent(position, 0) + 1200, VectorComponent(position, 1) + 1206,
                    VectorComponent(velocity, 0), VectorComponent(velocity, 1), 0, 0, 0, 0,
                    shot.GetProperty("damage").GetInt16(), shot.GetProperty("knockBack").GetSingle(), 0);
                f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet));
                Assert.True(f.Projectiles.ActiveCount == 1, $"birth {weapon}/{prefix}/flower={flower}/mana={mana}/windows={windows}/phaseFirst={phaseFirst}");
                var live = new ProjectileSnapshot[f.Projectiles.Capacity]; Assert.Equal(1, f.Projectiles.CopyActive(live));
                Assert.True(f.Projectiles.IsCombatTrusted(live[0].Handle));
                Assert.Equal(packet.Damage, live[0].Damage); Assert.Equal(packet.KnockBack, live[0].KnockBack);
                Assert.Equal(packet.VelocityX, live[0].VelocityX); Assert.Equal(packet.VelocityY, live[0].VelocityY);
                int paid = row.GetProperty("afterPayment").GetProperty("statMana").GetInt32();
                Assert.Equal(paid, f.Member.Mana); Assert.True(f.Member.ItemPhase!.Value.MagicPayment!.Value.BirthConsumed);
                var saved = f.Member.ItemPhase.Value.Mana;
                f.State.Apply(new PlayerManaRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, (short)paid, 200)));
                Assert.Equal(saved, f.Member.ItemPhase.Value.Mana);
                f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet with { Key = new(0, 902, 1) }));
                Assert.True(f.Projectiles.ActiveCount == 1, $"birth {weapon}/{prefix}/flower={flower}/mana={mana}/windows={windows}/phaseFirst={phaseFirst}"); Assert.Equal(paid, f.Member.Mana);
                int remaining = row.GetProperty("afterInventory")[2].GetProperty("stack").GetInt32();
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out var potion)); Assert.Equal(remaining, potion.Stack);
                births++;
            }
        }
        Assert.Equal(432, births);
        using var phaseSource = ReadSource("MagicPrefixPhase1458");
        var providerProfile = phaseSource.RootElement.GetProperty("Linux").EnumerateArray().Single(r =>
            r.GetProperty("spec").GetProperty("name").GetString() == "113-p26-living");
        foreach (int mutation in new[] { 0, 1 })
        {
            using var f = new HumanManaFlowerAuthority1458Tests.Harness.Fixture(providerProfile, false);
            f.Move(false, 0, true); f.State.Tick(); f.Move(true, 0, true);
            var replication = new RuntimeProjectileReplicationRegistry();
            var store = new RuntimeProjectileStore(commitSink: replication); bool once = true;
            var projectileRandom = new VanillaUnifiedRandom1458(0);
            var randomBefore = projectileRandom.Clone(); var phaseBefore = f.Member.ItemPhase; short manaBefore = f.Member.Mana;
            var authority = new ProjectileAuthority(store, f.Players, new RuntimeNpcStore(),
                new RuntimePlayerSnapshotLookup(f.Players, null), null, replication, () =>
                {
                    if (once)
                    {
                        once = false;
                        if (mutation == 0) f.Equipment(0, 113, 1, 27);
                        else f.Move(true, 1, true);
                    }
                    return 0;
                }, projectileRandom: projectileRandom);
            var packet = new TerrariaProjectileUpdateState(new(0, 993, 1), 16, 1594, 1611, 6, 0, 0, 0, 0, 0, 38, 7.5f, 0);
            Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet)));
            Assert.False(once); Assert.Equal(0, store.ActiveCount);
            Assert.Equal(manaBefore, f.Member.Mana); Assert.Equal(phaseBefore, f.Member.ItemPhase);
            Assert.True(projectileRandom.HasSameState(randomBefore));
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var held));
            Assert.Equal(mutation == 0 ? 27 : 26, held.Prefix.Value);
            Assert.Equal(mutation == 1 ? 1 : 0, f.Member.SelectedItem);
        }
    }
    private static float VectorComponent(JsonElement value, int index)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return value.GetProperty(index == 0 ? "X" : "Y").GetSingle();
        return float.Parse(value.GetString()!.Split(',')[index], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static JsonDocument ReadSource(string logicalName)
    {
        using var stream = typeof(HumanChanneledMagicPrefix1458Tests).Assembly.GetManifestResourceStream(logicalName);
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

}
