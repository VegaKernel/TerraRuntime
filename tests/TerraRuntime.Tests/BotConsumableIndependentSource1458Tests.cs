using TerraRuntime.Application;
using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.World;
using Xunit;
using System.Text.Json;
using System.Security.Cryptography;
using System.IO.Compression;

namespace TerraRuntime.Tests;

// Trusted BOT producer policy. Source Quick* numeric/order evidence is independent;
// this does not admit void inventory, full animation, Mana Sickness or vanilla buff slots.
public sealed class BotConsumableIndependentSource1458Tests
{
    [Fact]
    public void Commands_adopt_item_effect_and_bot_state_before_observers_and_preserve_reentry()
    {
        AssertIndependentSourceEffectsAndPolicy();
        foreach (string kind in new[] { "heal", "mana", "buff" })
        foreach (string callback in new[] { "plain", "inventory", "vitals", "effect", "throw", "generation", "reentry" })
        {
            var f = new Fixture(kind);
            bool first = true;
            f.Events.OnItem = (player, item) =>
            {
                if (!first)
                    return;
                first = false;
                Assert.Equal(f.Bot.Player, player);
                Assert.True(f.Players.TryGet(player, out var accepted));
                Assert.Equal(kind == "heal" ? 300 : 100, accepted.Life);
                Assert.Equal(200, accepted.Mana);
                Assert.Equal(kind == "heal" ? 3600 : 0, f.Bot.PotionDelayUntilTick);
                Assert.Equal(kind == "buff" ? 14400 : 0, f.Bot.ActiveBuffs.GetValueOrDefault(VanillaBuffIds.Wrath));
                Assert.True(item.IsEmpty);
                Assert.True(f.Players.TryGetItem(f.Id, 1, out var acceptedItem));
                Assert.Equal(item, acceptedItem);
                if (callback == "inventory")
                    Assert.True(f.Players.SetItem(f.Id, new(2, VanillaItemIds.StoneBlock, 77, VanillaPrefixIds.None, 0)));
                if (callback == "vitals")
                    Assert.True(f.Players.SetVitals(f.Id, new(77, 400, 33, 200)));
                if (callback == "effect")
                {
                    f.Bot.PotionDelayUntilTick = 99;
                    f.Bot.ActiveBuffs[VanillaBuffIds.Wrath] = 5;
                }
                if (callback == "generation")
                {
                    Assert.True(f.Players.Despawn(f.Id));
                    Assert.True(f.Players.Create(f.Id, 160, 160).IsCreated);
                }
                if (callback == "reentry")
                    f.Apply();
                if (callback == "throw")
                    throw new InvalidOperationException("expected callback throw");
            };
            if (callback == "throw")
                Assert.Equal("expected callback throw", Assert.Throws<InvalidOperationException>(f.Apply).Message);
            else
                f.Apply();
            Assert.False(first);
            Assert.True(f.Players.TryGetPlayer(f.Id, out var finalHandle));
            Assert.True(f.Players.TryGet(finalHandle, out var final));
            if (callback == "generation")
            {
                Assert.NotEqual(f.Bot.Player, finalHandle);
                Assert.False(final.HasHealth);
                Assert.Empty(f.Events.Vitals);
            }
            else
            {
                Assert.Equal(callback == "vitals" ? 77 : kind == "heal" ? 300 : 100, final.Life);
                Assert.Equal(callback == "vitals" ? 33 : 200, final.Mana);
                Assert.Equal(400, final.BaseLifeMax);
                Assert.Equal(400, final.MaxLife);
                Assert.False(final.IsDead);
                Assert.Equal(callback == "effect" ? 99 : kind == "heal" ? 3600 : 0, f.Bot.PotionDelayUntilTick);
                if (callback == "effect")
                    Assert.Equal(5, f.Bot.ActiveBuffs[VanillaBuffIds.Wrath]);
                if (callback == "inventory")
                {
                    Assert.True(f.Players.TryGetItem(f.Id, 2, out var extra));
                    Assert.Equal(77, extra.Stack);
                }
                Assert.Equal(callback == "vitals" ? 1 : kind == "buff" ? 0 : 1, f.Events.Vitals.Count);
                if (f.Events.Vitals.Count != 0)
                    Assert.Equal(callback == "vitals" ? 77 : kind == "heal" ? 300 : 100, f.Events.Vitals[0].Life);
            }
        }
        foreach (string kind in new[] { "heal", "buff" })
        foreach (string mutation in new[] { "config", "goal", "observation", "tick", "generation" })
        {
            var f = new Fixture(kind);
            if (kind == "heal")
                Assert.True(f.Players.SetVitals(f.Id, new(100, 400, 0, 200)));
            var second = new ServerPlayerItemState(2, kind == "heal" ? VanillaItemIds.GreaterManaPotion
                : VanillaItemIds.ArcheryPotion, 1, VanillaPrefixIds.None, 0);
            Assert.True(f.Players.SetItem(f.Id, second));
            f.Events.ItemOffers = 0;
            f.Events.Vitals.Clear();
            f.Events.OnItem = (_, _) =>
            {
                switch (mutation)
                {
                    case "config": f.Bot.Configuration = f.Bot.Configuration with { Mode = RuntimeBotMode.Idle }; break;
                    case "goal": f.Bot.GoalGeneration++; break;
                    case "observation": f.Bot.ObservationRevision++; break;
                    case "tick": f.Bot.CurrentTick++; break;
                    case "generation":
                        Assert.True(f.Players.Despawn(f.Id));
                        var replacement = f.Players.Create(f.Id, 160, 160);
                        Assert.True(replacement.IsCreated);
                        f.Bot.Player = replacement.Player;
                        Assert.True(f.Players.SetVitals(f.Id, new(100, 400, 0, 200)));
                        // No callback recursion while installing the replacement's distinct item owner.
                        f.Events.OnItem = null;
                        Assert.True(f.Players.SetItem(f.Id, second));
                        break;
                }
            };
            f.Apply();
            Assert.True(f.Players.TryGetItem(f.Id, 2, out var intact));
            Assert.Equal(second, intact);
            Assert.False(f.Bot.ActiveBuffs.ContainsKey(VanillaBuffIds.Archery));
            Assert.True(f.Players.TryGetPlayer(f.Id, out var final));
            Assert.True(f.Players.TryGet(final, out var vitals));
            Assert.Equal(kind == "heal" || mutation == "generation" ? 0 : 200, vitals.Mana);
        }
    }

    [Fact]
    public void Prepared_owner_refuses_changed_actor_item_configuration_and_effects_and_publishes_once()
    {
        foreach (string mutation in new[] { "config", "goal", "observation", "tick", "delay", "buff", "buff-reference", "item-aba", "vitals", "generation" })
        {
            var f = new Fixture("heal");
            Assert.True(f.Prepare(out var plan));
            Assert.True(plan!.IsCurrent);
            switch (mutation)
            {
                case "config": f.Bot.Configuration = f.Bot.Configuration with { Mode = RuntimeBotMode.Idle }; break;
                case "goal": f.Bot.GoalGeneration++; break;
                case "observation": f.Bot.ObservationRevision++; break;
                case "tick": f.Bot.CurrentTick++; break;
                case "delay": f.Bot.PotionDelayUntilTick = 1; break;
                case "buff": f.Bot.ActiveBuffs[VanillaBuffIds.Wrath] = 5; break;
                case "buff-reference": f.Bot.ActiveBuffs = new(f.Bot.ActiveBuffs); break;
                case "item-aba": Assert.True(f.Players.SetItem(f.Id, f.Potion)); break;
                case "vitals": Assert.True(f.Players.SetVitals(f.Id, new(99, 400, 200, 200))); break;
                case "generation":
                    Assert.True(f.Players.Despawn(f.Id));
                    Assert.True(f.Players.Create(f.Id, 160, 160).IsCreated);
                    break;
            }
            int priorItems = f.Events.ItemOffers;
            int priorVitals = f.Events.Vitals.Count;
            Assert.False(plan.IsCurrent);
            Assert.False(plan.TryAdoptUnpublished());
            Assert.False(plan.TryPublish());
            Assert.Equal(priorItems, f.Events.ItemOffers);
            Assert.Equal(priorVitals, f.Events.Vitals.Count);
        }

        // The bot producer owns only Archery/Wrath. A retained non-owned effect must not be silently replaced.
        var unknownActor = new Fixture("buff", initializeVitals: false);
        Assert.True(unknownActor.Players.TryGet(unknownActor.Bot.Player, out var uninitialized));
        Assert.False(uninitialized.HasHealth);
        Assert.False(uninitialized.IsDead);
        var unknownEmpty = new ServerPlayerItemState(1, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0);
        var unknownBuffEffect = new RuntimeBotInventory.ConsumableEffect(null, null, VanillaBuffIds.Wrath, 14400);
        // The public observation scope already rejects unknown health; the typed producer guard also owns this invariant.
        Assert.False(unknownActor.Inventory.TryPrepareConsumable(uninitialized, unknownActor.Potion, unknownEmpty, unknownBuffEffect, 0, out _));
        unknownActor.Apply();
        Assert.Equal(0, unknownActor.Events.ItemOffers);
        Assert.Empty(unknownActor.Events.Vitals);
        Assert.Empty(unknownActor.Bot.ActiveBuffs);
        Assert.True(unknownActor.Players.TryGetItem(unknownActor.Id, 1, out var unknownPotion));
        Assert.Equal(unknownActor.Potion, unknownPotion);

        var unknownEffect = new Fixture("heal");
        unknownEffect.Bot.ActiveBuffs[VanillaBuffIds.Regeneration] = 100;
        Assert.False(unknownEffect.Prepare(out _));
        unknownEffect.Apply();
        Assert.Equal(0, unknownEffect.Events.ItemOffers);
        Assert.True(unknownEffect.Players.TryGetItem(unknownEffect.Id, 1, out var unknownIntact));
        Assert.Equal(unknownEffect.Potion, unknownIntact);
        Assert.Equal(100, unknownEffect.Bot.ActiveBuffs[VanillaBuffIds.Regeneration]);

        var once = new Fixture("heal");
        Assert.True(once.Prepare(out var accepted));
        Assert.True(accepted!.TryAdoptUnpublished());
        Assert.False(accepted.TryAdoptUnpublished());
        Assert.Equal(0, once.Events.ItemOffers);
        Assert.Empty(once.Events.Vitals);
        once.Events.OnItem = (_, _) => throw new InvalidOperationException("once throw");
        Assert.Equal("once throw", Assert.Throws<InvalidOperationException>(() => accepted.TryPublish()).Message);
        Assert.False(accepted.TryPublish());
        Assert.Equal(1, once.Events.ItemOffers);
        Assert.Single(once.Events.Vitals);

        foreach (string kind in new[] { "heal", "buff" })
        {
            var overflow = new Fixture(kind);
            overflow.Bot.CurrentTick = long.MaxValue;
            overflow.Observation = overflow.Observation with { Tick = long.MaxValue };
            overflow.Apply();
            Assert.Equal(0, overflow.Events.ItemOffers);
            Assert.True(overflow.Players.TryGetItem(overflow.Id, 1, out var intact));
            Assert.Equal(overflow.Potion, intact);
        }

        var death = new Fixture("heal");
        Assert.True(death.Players.TryGet(death.Bot.Player, out var before));
        var empty = new ServerPlayerItemState(1, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0);
        Assert.False(death.Players.TryPrepareItemMutation(death.Id, before, death.Potion, empty, out _, new(0, 400, 0, 200)));
        // Existing NormalizeHealth clamps only maximum to20; current life remains1, not death.
        Assert.True(death.Players.TryPrepareItemMutation(death.Id, before, death.Potion, empty, out var normalized, new(1, 0, 0, 0)));
        Assert.True(normalized!.TryAdoptUnpublished());
        Assert.True(death.Players.TryGet(death.Bot.Player, out var normalizedState));
        Assert.Equal(1, normalizedState.Life);
        Assert.Equal(20, normalizedState.MaxLife);
        Assert.Equal(20, normalizedState.BaseLifeMax);
        Assert.False(normalizedState.IsDead);
    }

    private static void AssertIndependentSourceEffectsAndPolicy()
    {
        using var compressed = typeof(BotConsumableIndependentSource1458Tests).Assembly.GetManifestResourceStream("BotQuickConsumableSource1458")!;
        using var decoded = new GZipStream(compressed, CompressionMode.Decompress);
        using var stream = new MemoryStream();
        decoded.CopyTo(stream);
        stream.Position = 0;
        Assert.Equal("11e5a36e067db093317481b4748a1cd206e5a62403e6a24dabefa64fc051b9ac", Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        stream.Position = 0;
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034", root.GetProperty("originalAssemblySha256").GetString());
        Assert.Equal("f0f22f877fa5545e756c3d5052e61126b15b591d8a0813e5fe28c9f58f7d683f", root.GetProperty("source42ManifestSha256").GetString());
        Assert.Equal(42, root.GetProperty("reference42").GetArrayLength());
        Assert.Equal(7, root.GetProperty("policyMatched7").GetArrayLength());
        var singles = root.GetProperty("reference42").EnumerateArray().Where(r => r.GetProperty("profile").GetString()!.StartsWith("heal-") && r.GetProperty("profile").GetString()!.EndsWith("-clamp") || r.GetProperty("profile").GetString()!.StartsWith("mana-") && r.GetProperty("profile").GetString()!.EndsWith("-clamp") || r.GetProperty("profile").GetString()!.StartsWith("buff-")).ToArray();
        Assert.Equal(14, singles.Length);
        foreach (var row in singles)
        {
            var input = row.GetProperty("before").GetProperty("inventory")[0];
            var type = new ItemTypeId(input.GetProperty("type").GetInt32());
            Assert.True(VanillaBotItemDefinitionCatalog1458.TryGet(type, out var metadata));
            Assert.Equal(input.GetProperty("healLife").GetInt32(), metadata.HealLife);
            Assert.Equal(input.GetProperty("healMana").GetInt32(), metadata.HealMana);
            Assert.Equal(input.GetProperty("buffType").GetInt32(), metadata.BuffType.Value);
            Assert.Equal(input.GetProperty("buffTime").GetInt32(), metadata.BuffTimeTicks);
            var before = row.GetProperty("before");
            var kind = metadata.Kind == VanillaBotItemKind.HealingPotion ? "heal" : metadata.Kind == VanillaBotItemKind.ManaPotion ? "mana" : "buff";
            var f = new Fixture(kind, type, (short)before.GetProperty("life").GetInt32(), (short)before.GetProperty("lifeMax").GetInt32(), (short)before.GetProperty("mana").GetInt32(), (short)before.GetProperty("manaMax").GetInt32());
            f.Apply();
            Assert.True(f.Players.TryGetItem(f.Id, 1, out var finalItem));
            if (kind == "buff" && (type == VanillaItemIds.ArcheryPotion || type == VanillaItemIds.WrathPotion))
            {
                Assert.True(finalItem.IsEmpty);
                Assert.Equal(input.GetProperty("buffTime").GetInt32(), f.Bot.ActiveBuffs[metadata.BuffType]);
                Assert.Equal(1, f.Events.ItemOffers);
            }
            else
            {
                // These source clamp inputs are intentionally inefficient for bot conservation, or provenance-only buffs.
                Assert.Equal(f.Potion, finalItem);
                Assert.Equal(0, f.Events.ItemOffers);
                Assert.True(f.Players.TryGet(f.Bot.Player, out var unchanged));
                Assert.Equal(before.GetProperty("life").GetInt32(), unchanged.Life);
                Assert.Equal(before.GetProperty("mana").GetInt32(), unchanged.Mana);
            }
        }
        foreach (var row in root.GetProperty("reference42").EnumerateArray().Where(r => r.GetProperty("profile").GetString()!.StartsWith("heal-order-")))
        {
            var before = row.GetProperty("before");
            var f = new Fixture("heal", life: (short)before.GetProperty("life").GetInt32());
            foreach (var slot in before.GetProperty("inventory").EnumerateArray().Where(i => i.GetProperty("type").GetInt32() != 0))
                Assert.True(f.Players.SetItem(f.Id, new((short)slot.GetProperty("slot").GetInt32(), new ItemTypeId(slot.GetProperty("type").GetInt32()), (short)slot.GetProperty("stack").GetInt32(), VanillaPrefixIds.None, 0)));
            f.Events.ItemOffers = 0; f.Events.Vitals.Clear();
            f.Apply();
            bool efficient = row.GetProperty("profile").GetString() == "heal-order-missing350";
            Assert.Equal(efficient ? 1 : 0, f.Events.ItemOffers);
            Assert.True(f.Players.TryGet(f.Bot.Player, out var final));
            Assert.Equal(efficient ? row.GetProperty("after").GetProperty("life").GetInt32() : before.GetProperty("life").GetInt32(), final.Life);
            foreach (var slot in (efficient ? row.GetProperty("after") : before).GetProperty("inventory").EnumerateArray().Where(i => i.GetProperty("type").GetInt32() != 0))
            {
                Assert.True(f.Players.TryGetItem(f.Id, (short)slot.GetProperty("slot").GetInt32(), out var retained));
                Assert.Equal(slot.GetProperty("stack").GetInt32(), retained.Stack);
            }
        }
        var fullManaSource = root.GetProperty("reference42").EnumerateArray().Single(r => r.GetProperty("profile").GetString() == "mana-full");
        Assert.True(fullManaSource.GetProperty("sourceDebug").GetProperty("ManaV2").GetBoolean());
        Assert.Equal(1, fullManaSource.GetProperty("after").GetProperty("inventory")[0].GetProperty("stack").GetInt32());
        var fullManaBot = new Fixture("mana", VanillaItemIds.LesserManaPotion, mana: 200);
        fullManaBot.Apply();
        Assert.Equal(0, fullManaBot.Events.ItemOffers);
        Assert.True(fullManaBot.Players.TryGetItem(fullManaBot.Id, 1, out var fullManaRetained));
        Assert.Equal(fullManaBot.Potion, fullManaRetained);

        foreach (var row in root.GetProperty("policyMatched7").EnumerateArray())
        {
            var before = row.GetProperty("before"); var after = row.GetProperty("after");
            var type = new ItemTypeId(before.GetProperty("inventory")[0].GetProperty("type").GetInt32());
            var kind = row.GetProperty("method").GetString() == "QuickHeal" ? "heal" : "mana";
            var f = new Fixture(kind, type, (short)before.GetProperty("life").GetInt32(), (short)before.GetProperty("lifeMax").GetInt32(), (short)before.GetProperty("mana").GetInt32(), (short)before.GetProperty("manaMax").GetInt32());
            bool observed = false;
            f.Events.OnItem = (_, item) =>
            {
                observed = true;
                Assert.True(f.Players.TryGet(f.Bot.Player, out var current));
                Assert.Equal(after.GetProperty("life").GetInt32(), current.Life);
                Assert.Equal(after.GetProperty("mana").GetInt32(), current.Mana);
                Assert.Equal(after.GetProperty("potionDelay").GetInt32(), f.Bot.PotionDelayUntilTick);
                Assert.True(item.IsEmpty);
            };
            f.Apply(); Assert.True(observed); Assert.Single(f.Events.Vitals);
        }
    }

    private sealed class Fixture
    {
        public readonly ServerPlayerId Id = new("probe");
        public readonly Events Events = new();
        public readonly ServerPlayerAuthority Players;
        public readonly BotState Bot;
        public readonly RuntimeBotInventory Inventory;
        public readonly ServerPlayerItemState Potion;
        public RuntimeBotObservationSnapshot Observation;
        private readonly string kind;

        public Fixture(string kind, ItemTypeId? itemType = null, short? life = null, short? lifeMax = null, short? mana = null, short? manaMax = null, bool initializeVitals = true)
        {
            this.kind = kind;
            var slots = new PlayerSlotPool(8);
            var identities = new ServerPlayerSlotRegistry(slots);
            Players = new(new ServerPlayerStateStore(identities, 8), identities, events: Events);
            var created = Players.Create(Id, 160, 160);
            Assert.True(created.IsCreated);
            if (initializeVitals)
                Assert.True(Players.SetVitals(Id, new(life ?? 100, lifeMax ?? 400, mana ?? (kind == "mana" ? (short)0 : (short)200), manaMax ?? 200)));
            Potion = new(1, itemType ?? (kind == "buff" ? VanillaItemIds.WrathPotion : kind == "mana"
                ? VanillaItemIds.GreaterManaPotion : VanillaItemIds.SuperHealingPotion), 1, VanillaPrefixIds.None, 0);
            Assert.True(Players.SetItem(Id, Potion));
            Events.ItemOffers = 0;
            Events.Vitals.Clear();
            var tiles = new WorldTileStore(new WorldDimensions(300, 120));
            var worldItems = new WorldItemAuthority(new PlayerAuthority(null, tiles), new RuntimeWorldItemStore(), new SystemWorldItemSpawnRandom(0), null);
            WorldRuntimeIdentity world = new(new(Guid.Parse("11111111-1111-1111-1111-111111111111")), new(Guid.Parse("22222222-2222-2222-2222-222222222222")));
            Bot = new(1, Id, "probe", new(RuntimeBotMode.Collect, default),
                RuntimePlayerBotLoadoutCatalog1458.Pick(new Random(42)), default, default, 0)
                { Player = created.Player, ObservationRevision = 1 };
            Inventory = new(Bot, Players, worldItems, new(), world);
            Assert.True(Players.TryGet(created.Player, out var self));
            Observation = new(Bot.Id, world, 1, 1, 0, self, Bot.Configuration, null, null, null, false, null, null);
        }

        public void Apply()
        {
            if (kind == "buff") Inventory.UseCombatBuffs(Observation);
            else Assert.Equal(RuntimeBotActionStatus.Success, Inventory.UseConsumables(Observation).Status);
        }

        public bool Prepare(out RuntimeBotInventory.ConsumablePreparation? plan)
        {
            Assert.True(Players.TryGet(Bot.Player, out var self));
            var empty = new ServerPlayerItemState(1, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0);
            var effect = new RuntimeBotInventory.ConsumableEffect(new(300, 400, 200, 200), 3600, default, 0);
            return Inventory.TryPrepareConsumable(self, Potion, empty, effect, 0, out plan);
        }
    }

    private sealed class Events : IRuntimeServerPlayerEventSink
    {
        public Action<PlayerHandle, ServerPlayerItemState>? OnItem;
        public int ItemOffers;
        public readonly List<ServerPlayerVitalsState> Vitals = [];
        public void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item) { ItemOffers++; OnItem?.Invoke(player, item); }
        public void ServerPlayerVitalsUpdated(PlayerHandle player, in ServerPlayerVitalsState vitals) => Vitals.Add(vitals);
        public void ServerPlayerCreated(in PlayerStateSnapshot player) { }
        public void ServerPlayerAppearanceUpdated(PlayerHandle player, in ServerPlayerAppearanceState appearance) { }
        public void ServerPlayerPvpUpdated(PlayerHandle player, bool value) { }
        public void ServerPlayerGodModeUpdated(PlayerHandle player, bool value) { }
        public void ServerPlayerDied(PlayerHandle player, DamageSource source, ProjectileTypeId type, int damage, int direction) { }
        public void ServerPlayerBuffTypesUpdated(PlayerHandle player, ReadOnlySpan<BuffTypeId> buffs) { }
        public void ServerPlayerMoved(in PlayerStateSnapshot player) { }
        public void ServerPlayerItemUsePresented(PlayerHandle player, float rotation, short ticks) { }
        public void ServerPlayerRecallPresented(in PlayerStateSnapshot player, short x, short y) { }
        public void ServerPlayerDespawned(PlayerHandle player) { }
    }
}
