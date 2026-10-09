namespace TerraRuntime.Gameplay.Players;

internal readonly record struct PlayerRemoteItemControls1458(bool ControlUseItem,
    bool LastUseSuccess, bool Cursed, bool CrowdControlled, bool SelectionBuffered);

/// <summary>Shared dedicated useStyle0/1 clocks; style5's renewal belongs to its ranged owner.</summary>
internal static class VanillaRemoteItemClocks1458
{
    internal static bool TryStep(PlayerSelectedConsumableState1458? previous,
        int useAnimation, bool autoReuse, bool permitsUse, in PlayerRemoteItemControls1458 facts,
        Func<int, int, int> nextInteger, out PlayerSelectedConsumableState1458 next,
        out bool pendingItemReuse, out bool beganActualUse, out bool resetItemRotation)
    {
        next = default;
        pendingItemReuse = false;
        beganActualUse = false;
        resetItemRotation = false;
        if (previous is not { } state ||
            state.ItemTime is < 0 or > short.MaxValue || state.ItemTimeMax is < 0 or > short.MaxValue ||
            state.Animation is < 0 or > short.MaxValue || state.AnimationMax is < 0 or > short.MaxValue ||
            state.PotionDelay is < 0 or > 3_600 || state.RevolverCritBonus < int.MinValue + 2 ||
            nextInteger is null)
            return false;
        if (facts.CrowdControlled)
        {
            next = state with { Animation = 0, AnimationMax = 0 };
            return true;
        }

        int animation = state.Animation;
        int animationMax = state.AnimationMax;
        bool release = state.ReleaseUseItem;
        if (autoReuse && !facts.Cursed && !facts.SelectionBuffered)
        {
            release = true;
            // Source style1 clears the last frame, then can run StartActualUse.
            // Remote style5's direct ApplyItemAnimation renewal is a separate transition.
            if (animation == 1)
                animation = 0;
        }
        if (animation == 0)
            animationMax = 0;
        // Source shoot0 rotation resets at the attempt gate, before CanUse/TryStartUse can refuse.
        resetItemRotation = permitsUse && facts.ControlUseItem && release && animation == 0 &&
            !facts.SelectionBuffered;
        beganActualUse = resetItemRotation && facts.LastUseSuccess && !facts.Cursed;

        int crit = state.RevolverCritBonus;
        if (nextInteger(0, 3) == 0)
            crit -= 2;
        if (beganActualUse)
            animation = animationMax = useAnimation;
        if (animation > 0)
        {
            animation--;
            pendingItemReuse = animation == 0 && facts.ControlUseItem && release;
        }
        next = new(Math.Max(0, state.ItemTime - 1), state.ItemTimeMax,
            animation, animationMax, !facts.ControlUseItem, state.PotionDelay, crit);
        return true;
    }
}
