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
using RangedFixture = TerraRuntime.Tests.BotAmmoIndependentSource1458Tests.Fixture;

namespace TerraRuntime.Tests;

public sealed class BotMeleeHeldInventory1458Tests
{
    [Fact]
    public void Neutral_melee_requires_the_configured_weapon_in_the_actual_policy_slot()
    {
        // Identity/positive-stack custody is BOT policy. Random.Shared variation, selected crit,
        // and damage/presentation callback atomicity retain their separate existing contracts.
        for (int preset = 0; preset < 4; preset++)
        {
            var loadout = RuntimePlayerBotLoadoutCatalog1458.Pick(new PresetRandom(preset));
            Assert.Equal(new ItemTypeId(155), loadout.MeleeWeapon);
            foreach (var policy in new[] { RuntimeBotWeaponPolicy.Melee, RuntimeBotWeaponPolicy.Automatic })
            foreach (int retained in new[] { 0, 3, 24, 155 })
            {
                var f = new RangedFixture(98, 20);
                var configuration = f.Bot.Configuration with { WeaponPolicy = policy };
                var bot = new BotState(f.Bot.Id, f.Id, "melee", configuration, loadout,
                    default, default, 0) { Player = f.Bot.Player, ObservationRevision = 1 };
                Assert.True(f.Players.SetItem(f.Id, new(0, new(retained), (short)(retained == 0 ? 0 : 1), default, 0)));
                var npcs = new RuntimeNpcStore();
                var human = new PlayerAuthority(null, f.Tiles);
                var lookup = new RuntimePlayerSnapshotLookup(human, f.Players);
                var items = new RuntimeWorldItemStore();
                var authority = new NpcAuthority(lookup, () => 0, human, npcs, f.Shots, items,
                    new SystemWorldItemSpawnRandom(0), new RuntimeWorldItemInstancedLeaseStore(items),
                    f.Tiles, null, new RuntimeWorldProgressionMutations(), null, null, null,
                    null, null, null, null, null, false, false, null, f.Players,
                    null, null, null, null, false, false, false, true, false);
                var body = new NpcStateUpdate(preset == 0 ? 3 : 6, (short)(preset == 0 ? 3 : 6),
                    180, 160, 0, 0, 255, default, NpcSimulationState.Initial with { Life = 1000, LifeMax = 1000, MoneyValue = 0 });
                Assert.True(npcs.TrySpawnVanilla(in body, out var target));
                var inventory = new RuntimeBotInventory(bot, f.Players,
                    new WorldItemAuthority(human, items, new SystemWorldItemSpawnRandom(0), null), new(), f.World);
                var combat = new RuntimeBotCombat(bot, f.Players, authority, f.Projectiles,
                    f.Tiles, inventory, [bot], f.World);
                Assert.True(f.Players.TryGet(bot.Player, out var before));
                f.Events.Seen.Clear();
                var observation = new RuntimeBotObservationSnapshot(bot.Id, f.World, 1, bot.GoalGeneration,
                    0, before, configuration, null,
                    new BotGuardTarget(target.Handle, default, 189, 180, 0, 0, 18, 40),
                    null, false, null, null);
                var result = combat.Attack(in observation);
                Assert.True(npcs.TryGet(target.Handle, out var after));
                Assert.True(f.Players.TryGetItem(f.Id, 0, out var held));
                Assert.Equal(new ItemTypeId(retained), held.ItemType);
                if (retained == 155)
                {
                    Assert.Equal(RuntimeBotActionResult.Success, result);
                    Assert.True(after.Simulation.Life < target.Simulation.Life);
                    Assert.True(bot.NextAttackTick > 0);
                    Assert.Contains("move", f.Events.Seen);
                }
                else
                {
                    Assert.NotEqual(RuntimeBotActionResult.Success, result);
                    Assert.Equal(target, after);
                    Assert.True(f.Players.TryGet(bot.Player, out var unchanged));
                    Assert.Equal(before, unchanged);
                    Assert.Equal(0, bot.NextAttackTick);
                    Assert.Equal(0, bot.UseItemUntilTick);
                    Assert.Empty(f.Events.Seen);
                }
                if (retained == 155)
                {
                    // A represented nonneutral prefix is selectively unowned by this neutral fix.
                    Assert.True(f.Players.SetItem(f.Id, held with { Prefix = new PrefixId(36) }));
                    bot.NextAttackTick = 0;
                    bot.UseItemUntilTick = 0;
                    Assert.True(f.Players.TryGet(bot.Player, out var prefixed));
                    var second = observation with { Self = prefixed };
                    f.Events.Seen.Clear();
                    var rejected = combat.Attack(in second);
                    Assert.NotEqual(RuntimeBotActionResult.Success, rejected);
                    Assert.True(npcs.TryGet(target.Handle, out var unchangedTarget));
                    Assert.Equal(after, unchangedTarget);
                    Assert.True(f.Players.TryGet(bot.Player, out var unchangedActor));
                    Assert.Equal(prefixed, unchangedActor);
                    Assert.Empty(f.Events.Seen);
                    Assert.Equal(0, bot.NextAttackTick);
                    Assert.Equal(0, bot.UseItemUntilTick);
                }
            }
        }
    }

    private sealed class PresetRandom(int preset) : Random
    {
        public override int Next(int maxValue) => preset;
    }
}
