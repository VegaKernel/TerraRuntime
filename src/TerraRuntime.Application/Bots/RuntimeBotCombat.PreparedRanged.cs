using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Bots;

namespace TerraRuntime.Application.Bots;

internal sealed partial class RuntimeBotCombat
{
    private bool TryResolveHeldWeapon(
        ItemTypeId configuredWeapon,
        in ServerPlayerItemState held,
        in VanillaProjectileWeaponCombatDefinition weapon,
        out VanillaProjectileWeaponCombatDefinition resolved)
    {
        resolved = default;
        // The trusted BOT preset is intended equipment, not an authority to synthesize a missing item.
        // Positive stack is the canonical BOT inventory policy; original retained nonconsumable stack0 may shoot.
        if (held.IsEmpty || held.ItemType != configuredWeapon || weapon.Type != configuredWeapon)
            return false;
        // Existing neutral catalog loadouts include guns outside the bounded eight-prefix stat owner.
        if (held.Prefix == VanillaPrefixIds.None)
        {
            resolved = weapon;
            return true;
        }
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Bullet)
        {
            if (!VanillaBulletWeaponStats1458.TryResolve(held.ItemType, held.Prefix, itemPrefixArithmetic, out var stats))
                return false;
            resolved = weapon with
            {
                BaseDamage = stats.Damage,
                BaseKnockBack = stats.KnockBack,
                BaseShootSpeed = stats.ShootSpeed,
                UseTimeTicks = stats.UseTime,
                AnimationTicks = stats.Animation
            };
            return true;
        }
        if (weapon.AmmoFamily != VanillaProjectileAmmoFamily.Arrow ||
            !VanillaOrdinaryBowLaunch1458.TryGetPrefixModifiers(held.ItemType, held.Prefix, itemPrefixArithmetic, out var prefix))
            return false;
        bool windows = itemPrefixArithmetic == VanillaBulletSourceArithmetic1458.WindowsClr4X86;
        resolved = weapon with
        {
            BaseDamage = RoundStoredItemStat(weapon.BaseDamage, prefix.DamageMultiplier, windows),
            BaseKnockBack = weapon.BaseKnockBack * prefix.KnockBackMultiplier,
            BaseShootSpeed = weapon.BaseShootSpeed * prefix.ShootSpeedMultiplier,
            UseTimeTicks = RoundStoredItemStat(weapon.UseTimeTicks, prefix.SpeedMultiplier, windows),
            AnimationTicks = RoundStoredItemStat(weapon.AnimationTicks, prefix.SpeedMultiplier, windows)
        };
        return true;
    }

    private static int RoundStoredItemStat(int value, float multiplier, bool windows) =>
        (int)Math.Round(windows ? value * (double)multiplier : value * multiplier);

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
