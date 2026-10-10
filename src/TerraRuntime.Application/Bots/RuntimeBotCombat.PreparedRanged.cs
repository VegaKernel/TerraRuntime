using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Bots;

namespace TerraRuntime.Application.Bots;

internal sealed partial class RuntimeBotCombat
{
    private readonly record struct RangedCommand(PlayerHandle Player, RuntimeBotConfiguration Configuration,
        ulong Goal, ulong Observation, long Tick, long NextAttack, long UseUntil,
        Dictionary<BuffTypeId, long> Buffs, KeyValuePair<BuffTypeId, long>[] Effects);

    private RangedCommand CaptureRangedCommand() => new(bot.Player, bot.Configuration, bot.GoalGeneration,
        bot.ObservationRevision, bot.CurrentTick, bot.NextAttackTick, bot.UseItemUntilTick,
        bot.ActiveBuffs, bot.ActiveBuffs.ToArray());

    private bool IsCurrentRangedCommand(in RangedCommand command)
    {
        if (bot.Player != command.Player || !bot.OwnsCurrentActor(serverPlayers) ||
            bot.Configuration != command.Configuration || bot.GoalGeneration != command.Goal ||
            bot.ObservationRevision != command.Observation || bot.CurrentTick != command.Tick ||
            bot.NextAttackTick != command.NextAttack || bot.UseItemUntilTick != command.UseUntil ||
            !ReferenceEquals(bot.ActiveBuffs, command.Buffs) || bot.ActiveBuffs.Count != command.Effects.Length)
            return false;
        foreach (var pair in command.Effects)
        {
            if (!bot.ActiveBuffs.TryGetValue(pair.Key, out var value) || value != pair.Value)
                return false;
        }
        return true;
    }

    private static void PublishRangedUse(ServerPlayerAuthority.PreparedItem item,
        ProjectileAuthority.TrustedServerPlayerSpawnPreparation spawn)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        try
        {
            item.TryPublishRangedUse();
        }
        catch (Exception exception)
        {
            failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
        }
        try
        {
            spawn.TryPublish();
        }
        catch (Exception exception)
        {
            failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
        }
        try
        {
            item.TryPublishAcceptedItemComponent();
        }
        catch (Exception exception)
        {
            failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
        }
        failure?.Throw();
    }
}
