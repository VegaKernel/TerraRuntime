using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerSelectedConsumableState1458(int ItemTime, int ItemTimeMax,
    int Animation, int AnimationMax, bool ReleaseUseItem, int PotionDelay, int RevolverCritBonus);

public readonly record struct PlayerSelectedConsumableFacts1458(ItemTypeId Item, int Life,
    int LifeMaximum, int Mana, int ManaMaximum, bool ControlUseItem, bool LastUseSuccess,
    bool Cursed, bool CrowdControlled, bool AllowedToHoldItems, bool SelectionBuffered);

public readonly record struct PlayerSelectedConsumableTransition1458(
    PlayerSelectedConsumableState1458 State, int Life, int Mana,
    bool BeganUse, int PotionSicknessOffer, int ManaSicknessOffer);

/// <summary>Selected ordinary remote ItemCheck. Offers do not debit inventory or publish client reports.</summary>
public static class VanillaSelectedConsumable1458
{
    public const float ConstructorHeat = 0f;
    public static PlayerSelectedConsumableState1458 ConstructorState => default;

    public static bool TryStep(PlayerSelectedConsumableState1458? previous,
        in PlayerSelectedConsumableFacts1458 facts, Func<int, int, int> nextInteger,
        Func<double> nextDouble, out PlayerSelectedConsumableTransition1458 transition)
    {
        transition = default;
        if (previous is not { } state ||
            !VanillaSelectedConsumableCatalog1458.TryGet(facts.Item, out var item) ||
            state.ItemTime is < 0 or > short.MaxValue || state.ItemTimeMax is < 0 or > short.MaxValue ||
            state.Animation is < 0 or > short.MaxValue || state.AnimationMax is < 0 or > short.MaxValue ||
            state.PotionDelay is < 0 or > 3_600 || state.RevolverCritBonus < int.MinValue + 2 ||
            facts.LifeMaximum is < 20 or > VanillaRemotePlayerHealth1458.MaximumSupportedLifeMax ||
            facts.Life is < 0 or > VanillaRemotePlayerHealth1458.MaximumSupportedLifeMax ||
            facts.ManaMaximum is < 0 or > 400 || facts.Mana is < 0 or > 400 ||
            nextInteger is null || nextDouble is null) return false;

        if (facts.CrowdControlled)
        {
            transition = new(state with { Animation = 0, AnimationMax = 0 },
                facts.Life, facts.Mana, false, 0, 0);
            return true;
        }
        int crit = state.RevolverCritBonus;
        if (nextInteger(0, 3) == 0) crit -= 2;
        bool begin = item.UseAnimation > 0 && facts.ControlUseItem && state.ReleaseUseItem && state.Animation == 0 &&
            !facts.SelectionBuffered && facts.LastUseSuccess && !facts.Cursed &&
            (!item.HealingDelay || state.PotionDelay == 0);
        int animation = begin ? item.UseAnimation : state.Animation;
        int animationMax = begin ? item.UseAnimation : state.Animation == 0 ? 0 : state.AnimationMax;
        int delay = begin && item.HealingDelay ? 3_600 : state.PotionDelay;
        if (animation > 0) animation--;
        int time = Math.Max(0, state.ItemTime - 1);
        int timeMax = state.ItemTimeMax;
        int life = facts.Life;
        int mana = facts.Mana;
        int manaSickness = 0;
        if (facts.AllowedToHoldItems)
        {
            if (animation > 0 && item.DrinkColourCount > 0)
            {
                // Source drink positions, rotation, colour and scale survive the dedicated visual sink.
                nextDouble();
                nextDouble();
                nextDouble();
                nextInteger(0, item.DrinkColourCount);
                nextDouble();
            }
            if (time == 0 && animation > 0 && (item.HealLife > 0 || item.HealMana > 0))
            {
                life = Math.Min(facts.LifeMaximum, life + item.HealLife);
                mana = Math.Min(facts.ManaMaximum, mana + item.HealMana);
                manaSickness = item.HealMana > 0 ? 300 : 0;
                time = timeMax = item.UseTime;
            }
        }
        transition = new(new(time, timeMax, animation, animationMax, !facts.ControlUseItem,
            delay, crit), life, mana, begin, begin && item.HealingDelay ? delay : 0, manaSickness);
        return true;
    }

    /// <summary>Before ResetEffects, remote manaHeat visuals; returns the reset neutral heat.</summary>
    public static bool TryStepHeat(float? previousHeat, Func<double> nextDouble,
        Func<int, int, int> nextInteger, out float resetHeat)
    {
        resetHeat = 1f;
        if (previousHeat is not { } heat || !float.IsFinite(heat) || heat is < 0f or > 2f ||
            nextDouble is null || nextInteger is null) return false;
        if (heat != 1f)
        {
            for (int i = 0; i < 2; i++)
            {
                if ((float)nextDouble() * 6f < .3f)
                {
                    nextInteger(10, 26);
                    nextDouble();
                    nextDouble();
                }
            }
        }
        return true;
    }
}
