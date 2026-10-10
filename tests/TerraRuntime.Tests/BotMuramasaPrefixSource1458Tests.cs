using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using Xunit;
using System.Text.Json;
using System.IO.Compression;
using RangedFixture = TerraRuntime.Tests.BotAmmoIndependentSource1458Tests.Fixture;

namespace TerraRuntime.Tests;

public sealed class BotMuramasaPrefixSource1458Tests
{
    [Fact]
    public void Original_stored_prefix_stats_feed_actual_bot_use_once()
    {
        using var stream = typeof(BotMuramasaPrefixSource1458Tests).Assembly.GetManifestResourceStream("BotMuramasaPrefix1458");
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream!, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (string platform in new[] { "linux", "windows" })
        {
            var arithmetic = platform == "windows" ? VanillaBulletSourceArithmetic1458.WindowsClr4X86 : VanillaBulletSourceArithmetic1458.CoreClrSingle;
            var rows = document.RootElement.GetProperty(platform);
            Assert.Equal(41, rows.GetArrayLength());
            foreach (var row in rows.EnumerateArray())
            {
                byte prefix = row.GetProperty("spec").GetProperty("Prefix").GetByte();
                var held = row.GetProperty("held");
                Assert.True(VanillaMuramasaItemStats1458.TryResolve(new(155), new(prefix), arithmetic, out var weapon));
                Assert.Equal(held.GetProperty("damage").GetInt32(), weapon.BaseDamage);
                Assert.Equal(held.GetProperty("knockBack").GetSingle(), weapon.BaseKnockBack);
                Assert.Equal(held.GetProperty("crit").GetInt32(), weapon.BaseCrit - 4);
                Assert.Equal(held.GetProperty("useTime").GetInt32(), weapon.UseTimeTicks);
                Assert.Equal(held.GetProperty("useAnimation").GetInt32(), weapon.AnimationTicks);
                int sourceCrit = row.GetProperty("weaponQueries").GetProperty("weaponCrit").GetInt32();
                var atCrit = VanillaDirectMeleeCombatMath.Resolve(weapon, VanillaCombatPrefixModifiers.Identity,
                    VanillaPlayerCombatSnapshot.Baseline, 0, sourceCrit, pvp: false);
                var aboveCrit = VanillaDirectMeleeCombatMath.Resolve(weapon, VanillaCombatPrefixModifiers.Identity,
                    VanillaPlayerCombatSnapshot.Baseline, 0, sourceCrit + 1, pvp: false);
                Assert.Equal(sourceCrit, atCrit.CritChance);
                Assert.True(atCrit.Critical);
                Assert.False(aboveCrit.Critical);
                Assert.Equal(row.GetProperty("weaponQueries").GetProperty("originalDamage").GetInt32(), atCrit.Damage);
                // Gear-derived component crit and selected item crit have distinct owners; raw item bonus is added once.
                var equipmentCrit = VanillaDirectMeleeCombatMath.Resolve(weapon, VanillaCombatPrefixModifiers.Identity,
                    VanillaPlayerCombatSnapshot.Baseline with { MeleeCrit = 14 }, 0, 1, pvp: false);
                Assert.Equal(14 + held.GetProperty("crit").GetInt32(), equipmentCrit.CritChance);
                var (f, bot, npcs, target, combat, observation, sink) = Create(3, prefix, arithmetic);
                int calls = 0;
                f.Events.Observe = kind => {
                    if (kind != "move") return;
                    calls++;
                    Assert.True(npcs.TryGet(target.Handle, out var adopted) && adopted.Simulation.Life < 1000);
                    Assert.Equal(weapon.UseTimeTicks, bot.NextAttackTick);
                    Assert.Equal(weapon.AnimationTicks, bot.UseItemUntilTick);
                };
                Assert.Equal(RuntimeBotActionResult.Success, combat.Attack(observation));
                Assert.Equal(1, calls);
                Assert.True(f.Players.TryGetItem(f.Id, 0, out var retained));
                Assert.Equal(new PrefixId(prefix), retained.Prefix);
                Assert.Equal(weapon.UseTimeTicks, bot.NextAttackTick);
            }
        }
        // Custom neutral loadouts were already represented by the direct catalog; the155 prefix gate must not retire them.
        var (woodF, woodBot, woodNpcs, woodTarget, woodCombat, woodObservation, _) = Create(3, weaponType: 24);
        Assert.Equal(RuntimeBotActionResult.Success, woodCombat.Attack(woodObservation));
        Assert.Equal(20, woodBot.NextAttackTick);
        Assert.True(woodF.Players.TryGetItem(woodF.Id, 0, out var woodenSword));
        Assert.Equal(new ItemTypeId(24), woodenSword.ItemType);
        // Initial lethal/unsupported capability still delegates through the same represented item stats.
        var (legacyF, legacyBot, legacyNpcs, legacyTarget, legacyCombat, legacyObservation, _) = Create(3, 81);
        var lethal = new NpcStateUpdate(legacyTarget.Type, legacyTarget.NetId, legacyTarget.PositionX,
            legacyTarget.PositionY, 0, 0, legacyTarget.Target, legacyTarget.Ai,
            legacyTarget.Simulation with { Life = 1 });
        Assert.True(legacyNpcs.TryUpdate(legacyTarget.Handle, lethal, out _));
        Assert.Equal(RuntimeBotActionResult.Success, legacyCombat.Attack(legacyObservation));
        Assert.Equal(16, legacyBot.NextAttackTick);
    }

    [Fact]
    public void Held_prefix_census_and_newer_callback_tuple_preserve_atomic_use()
    {
        foreach (byte prefix in new byte[] { 82, 255 })
            Assert.False(VanillaMuramasaItemStats1458.TryResolve(new(155), new(prefix), VanillaBulletSourceArithmetic1458.CoreClrSingle, out _));
        Assert.False(VanillaMuramasaItemStats1458.TryResolve(new(3), default, VanillaBulletSourceArithmetic1458.CoreClrSingle, out _));
        foreach (int item in new[] { 0, 3, 155 })
        {
            var (f, bot, npcs, target, combat, observation, _) = Create(3);
            Assert.True(f.Players.SetItem(f.Id, new(0, new(item), (short)(item == 0 ? 0 : 1),
                new(item == 155 ? (byte)82 : (byte)0), 0)));
            RuntimeBotActionResult? result = null;
            Exception? unexpected = null;
            try { result = combat.Attack(observation); }
            catch (Exception exception) { unexpected = exception; }
            Assert.Null(unexpected);
            Assert.NotEqual(RuntimeBotActionResult.Success, result);
            Assert.True(npcs.TryGet(target.Handle, out var unchanged));
            Assert.Equal(target, unchanged);
            Assert.Equal(0, bot.NextAttackTick);
            Assert.DoesNotContain("move", f.Events.Seen);
        }
        foreach (string mode in new[] { "identity", "prefix" })
        {
            var (f, bot, npcs, target, combat, observation, _) = Create(3, 81);
            f.OnTick = () => Assert.True(f.Players.SetItem(f.Id,
                new(0, new(mode == "identity" ? 3 : 155), 1, default, 0)));
            RuntimeBotActionResult? result = null;
            Exception? unexpected = null;
            try { result = combat.Attack(observation); }
            catch (Exception exception) { unexpected = exception; }
            Assert.Null(unexpected);
            Assert.NotEqual(RuntimeBotActionResult.Success, result);
            Assert.True(npcs.TryGet(target.Handle, out var unchanged));
            Assert.Equal(target, unchanged);
            Assert.Equal(0, bot.NextAttackTick);
            Assert.DoesNotContain("move", f.Events.Seen);
        }
        foreach (int nextType in new[] { 155, 3 })
        {
            var (f, bot, npcs, target, combat, observation, sink) = Create(3, 81);
            int damageCalls = 0; bool mutated = false;
            sink.Observe = (kind, _) => { if (kind == NpcStateCommitKind.Update) damageCalls++; };
            f.Events.Observe = kind => {
                if (kind != "move" || mutated) return;
                mutated = true;
                Assert.Equal(16, bot.NextAttackTick);
                Assert.True(f.Players.SetItem(f.Id, new(0, new(nextType), 1, default, 0)));
                Assert.NotEqual(RuntimeBotActionResult.Success, combat.Attack(observation));
            };
            Assert.Equal(RuntimeBotActionResult.Success, combat.Attack(observation));
            Assert.True(f.Players.TryGetItem(f.Id, 0, out var retained));
            Assert.Equal(new ItemTypeId(nextType), retained.ItemType);
            Assert.Equal(default, retained.Prefix);
            Assert.Equal(1, damageCalls);
            Assert.Equal(16, bot.NextAttackTick);
        }
    }
    private static (RangedFixture F, BotState Bot, RuntimeNpcStore Npcs, NpcSnapshot Target,
        RuntimeBotCombat Combat, RuntimeBotObservationSnapshot Observation, Sink Sink) Create(int type, byte prefix = 0, VanillaBulletSourceArithmetic1458 arithmetic = VanillaBulletSourceArithmetic1458.CoreClrSingle, int weaponType = 155)
    {
        int retained = weaponType;
        var loadout = RuntimePlayerBotLoadoutCatalog1458.Pick(new Random(42)) with { MeleeWeapon = new(weaponType) };
        var f = new RangedFixture(98, 20);
        var configuration = f.Bot.Configuration with { WeaponPolicy = RuntimeBotWeaponPolicy.Melee };
        var bot = new BotState(f.Bot.Id, f.Id, "melee", configuration, loadout,
            default, default, 0) { Player = f.Bot.Player, ObservationRevision = 1 };
        Assert.True(f.Players.SetItem(f.Id, new(0, new(retained), (short)(retained == 0 ? 0 : 1), new(prefix), 0)));
        var sink = new Sink();
        var npcs = new RuntimeNpcStore(commitSink: sink);
        var human = new PlayerAuthority(null, f.Tiles);
        var lookup = new RuntimePlayerSnapshotLookup(human, f.Players);
        var items = new RuntimeWorldItemStore();
        var authority = new NpcAuthority(lookup, () => 0, human, npcs, f.Shots, items,
            new SystemWorldItemSpawnRandom(0), new RuntimeWorldItemInstancedLeaseStore(items),
            f.Tiles, null, new RuntimeWorldProgressionMutations(), null, null, null,
            null, null, null, null, null, false, false, null, f.Players,
            null, null, null, null, false, false, false, true, false);
        var body = new NpcStateUpdate(type, (short)(type),
            180, 160, 0, 0, 255, default, NpcSimulationState.Initial with { Life = 1000, LifeMax = 1000, MoneyValue = 0 });
        Assert.True(npcs.TrySpawnVanilla(in body, out var target));
        var inventory = new RuntimeBotInventory(bot, f.Players,
            new WorldItemAuthority(human, items, new SystemWorldItemSpawnRandom(0), null), new(), f.World);
        var combat = new RuntimeBotCombat(bot, f.Players, authority, f.Projectiles,
            f.Tiles, inventory, [bot], f.World, arithmetic);
        Assert.True(f.Players.TryGet(bot.Player, out var before));
        f.Events.Seen.Clear();
        var observation = new RuntimeBotObservationSnapshot(bot.Id, f.World, 1, bot.GoalGeneration,
            0, before, configuration, null,
            new BotGuardTarget(target.Handle, default, 189, 180, 0, 0, 18, 40),
            null, false, null, null);

        return (f,bot,npcs,target,combat,observation,sink);
    }

    private sealed class Sink : INpcStateCommitSink
    {
        internal Action<NpcStateCommitKind,NpcSnapshot>? Observe;
        public void NpcStateCommitted(NpcStateCommitKind kind,in NpcSnapshot npc)=>Observe?.Invoke(kind,npc);
    }
}
