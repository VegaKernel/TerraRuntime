using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Bots;

namespace TerraRuntime.Application.Bots;

internal sealed partial class RuntimeBotInventory
{
    private readonly record struct ConsumableCommand(
        PlayerHandle Player, RuntimeBotConfiguration Configuration, ulong Goal, ulong Observation, long Tick);

    private ConsumableCommand CaptureConsumableCommand() =>
        new(bot.Player, bot.Configuration, bot.GoalGeneration, bot.ObservationRevision, bot.CurrentTick);

    private bool IsCurrentConsumableCommand(in ConsumableCommand command) =>
        bot.Player == command.Player && bot.OwnsCurrentActor(serverPlayers) &&
        bot.Configuration == command.Configuration && bot.GoalGeneration == command.Goal &&
        bot.ObservationRevision == command.Observation && bot.CurrentTick == command.Tick;

    // This BOT policy owns only Archery and Wrath expiry entries, not the player buff inventory.
    private const int MaximumOwnedCombatBuffs = 2;

    internal readonly record struct ConsumableEffect(
        ServerPlayerVitalsState? Vitals, long? PotionDelayUntilTick, BuffTypeId Buff, long BuffUntilTick);

    internal bool TryPrepareConsumable(
        in PlayerStateSnapshot expected,
        in ServerPlayerItemState oldItem,
        in ServerPlayerItemState next,
        in ConsumableEffect effect,
        long tick,
        out ConsumablePreparation? plan)
    {
        plan = null;
        if (expected.Player != bot.Player || !expected.HasHealth || expected.Life <= 0 || expected.IsDead ||
            !bot.OwnsCurrentActor(serverPlayers) ||
            tick != bot.CurrentTick || tick < 0 || bot.ActiveBuffs.Count > MaximumOwnedCombatBuffs ||
            (effect.Buff != default && !VanillaBotItemDefinitionCatalog1458.IsSupportedCombatBuff(effect.Buff)) ||
            (effect.Vitals.HasValue && effect.Vitals.Value.Life <= 0) ||
            (effect.PotionDelayUntilTick.HasValue && effect.PotionDelayUntilTick.Value < tick) ||
            (effect.Buff != default && effect.BuffUntilTick <= tick) ||
            !serverPlayers.TryPrepareItemMutation(bot.ServerPlayerId, expected, oldItem, next,
                out var item, effect.Vitals))
            return false;
        foreach (var buff in bot.ActiveBuffs.Keys)
            if (!VanillaBotItemDefinitionCatalog1458.IsSupportedCombatBuff(buff))
                return false;
        plan = new(bot, serverPlayers, item!, effect);
        return true;
    }

    internal sealed class ConsumablePreparation
    {
        private readonly BotState bot;
        private readonly ServerPlayerAuthority players;
        private readonly ServerPlayerAuthority.PreparedItem item;
        private readonly ConsumableEffect effect;
        private readonly PlayerHandle player;
        private readonly RuntimeBotConfiguration configuration;
        private readonly ulong goal;
        private readonly ulong observation;
        private readonly long tick;
        private readonly long delay;
        private readonly Dictionary<BuffTypeId, long> originalBuffs;
        private readonly KeyValuePair<BuffTypeId, long>[] original;
        private readonly Dictionary<BuffTypeId, long> preparedBuffs;
        private bool adopted;
        private bool published;

        internal ConsumablePreparation(BotState bot, ServerPlayerAuthority players,
            ServerPlayerAuthority.PreparedItem item, in ConsumableEffect effect)
        {
            this.bot = bot;
            this.players = players;
            this.item = item;
            this.effect = effect;
            player = bot.Player;
            configuration = bot.Configuration;
            goal = bot.GoalGeneration;
            observation = bot.ObservationRevision;
            tick = bot.CurrentTick;
            delay = bot.PotionDelayUntilTick;
            originalBuffs = bot.ActiveBuffs;
            original = originalBuffs.ToArray();
            // Stage dictionary growth before the callback-free player/BOT adoption tail.
            preparedBuffs = effect.Buff == default
                ? originalBuffs
                : new Dictionary<BuffTypeId, long>(originalBuffs) { [effect.Buff] = effect.BuffUntilTick };
        }

        internal bool IsCurrent
        {
            get
            {
                if (adopted || !item.IsCurrent || bot.Player != player || !bot.OwnsCurrentActor(players) ||
                    bot.Configuration != configuration || bot.GoalGeneration != goal ||
                    bot.ObservationRevision != observation || bot.CurrentTick != tick ||
                    bot.PotionDelayUntilTick != delay || !ReferenceEquals(bot.ActiveBuffs, originalBuffs) ||
                    bot.ActiveBuffs.Count != original.Length)
                    return false;
                foreach (var pair in original)
                    if (!bot.ActiveBuffs.TryGetValue(pair.Key, out var until) || until != pair.Value)
                        return false;
                return true;
            }
        }

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent || !item.TryAdoptUnpublished())
                return false;
            // No callbacks, allocations or fallible writes occur between these owners.
            if (effect.PotionDelayUntilTick.HasValue)
                bot.PotionDelayUntilTick = effect.PotionDelayUntilTick.Value;
            bot.ActiveBuffs = preparedBuffs;
            adopted = true;
            return true;
        }

        internal bool TryPublish()
        {
            if (!adopted || published)
                return false;
            published = true;
            return item.TryPublishConsumable();
        }

        internal bool TryAdoptAndPublish() => TryAdoptUnpublished() && TryPublish();
    }
}
