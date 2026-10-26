namespace TerraRuntime.Gameplay.Players;

/// <summary>Source ItemCheck fields shared by owned weapons, placement items and tools; no target or payment authority.</summary>
public readonly record struct LocalItemClockDefinition1458(int UseTime, int UseAnimation,
    byte UseStyle, bool AutoReuse, bool Channel, int Shoot)
{
    public bool ShootsEveryUse { get; init; }
    public bool Style5AnimationBonus { get; init; }
    public bool ResetsToolTimeOnStart { get; init; }
    public int ReuseDelay { get; init; }
}

public static class VanillaLocalItemUse1458
{
    // Replays the already consumed common Next(3) value; never owns a second stream.
    public static bool TryAdvanceFrame(in LocalItemClockDefinition1458 definition,
        in ToolClockState1458 previous, in ToolFrameFacts1458 facts, int critRoll,
        out ToolClockTransition1458 transition)
    {
        transition = default;
        if (!Valid(definition) || !previous.IsValid || !float.IsFinite(facts.AnimationMultiplier) ||
            facts.AnimationMultiplier < 0f) return false;
        if (facts.CrowdControlled)
        {
            transition = new(previous with
            {
                Selected = previous.Selected with { Animation = 0, AnimationMax = 0 },
                Channel = false
            }, false, false, false, false, false, false)
            { LastUseSuccess = facts.LastUseSuccess };
            return true;
        }
        if ((uint)critRoll >= 3) return false;
        double animationProduct = facts.WindowsArithmetic ? definition.UseAnimation * (double)facts.AnimationMultiplier : definition.UseAnimation * facts.AnimationMultiplier;
        if (!double.IsFinite(animationProduct) || animationProduct >= short.MaxValue + 1f) return false;
        int animationTicks = Math.Max(1, (int)animationProduct);
        if (definition.Style5AnimationBonus) animationTicks++;
        var selected = previous.Selected;
        int animation = selected.Animation;
        int maximum = selected.AnimationMax;
        bool release = selected.ReleaseUseItem;
        bool autoLogic = !facts.SelectionBuffered || definition.Shoot > 0 && selected.ItemTime != 0;
        if (autoLogic && definition.AutoReuse && !facts.NoItems)
        {
            release = true;
            if (animation == 1)
            {
                if (!facts.LocalOwner && facts.ControlUseItem && definition.UseStyle == 5 &&
                    definition.Shoot > 0 && facts.LastUseSuccess)
                    animation = maximum = animationTicks;
                else animation = 0;
            }
        }
        if (autoLogic && facts.AutoReuseOverride) release = true;
        if (animation == 0) maximum = 0;
        int reuseDelay = previous.ReuseDelay;
        if (animation == 0 && reuseDelay > 0)
        {
            animation = reuseDelay;
            selected = selected with { ItemTime = reuseDelay };
            reuseDelay = 0;
        }
        int crit = selected.RevolverCritBonus - (critRoll == 0 ? 2 : 0);
        bool attempt = facts.ControlUseItem && release && animation == 0 && !facts.SelectionBuffered;
        bool useSuccess = facts.LastUseSuccess;
        bool began = false;
        bool channel = previous.Channel;
        int toolTime = previous.ToolTime;
        if (attempt)
        {
            bool allowed = !facts.NoItems && facts.SourceUseAllowed;
            if (facts.LocalOwner) useSuccess = allowed;
            began = allowed && (facts.LocalOwner || facts.LastUseSuccess);
            if (began)
            {
                animation = maximum = animationTicks;
                if (definition.ResetsToolTimeOnStart) toolTime = 1;
                reuseDelay = definition.ReuseDelay;
                if (definition.Channel) channel = true;
            }
        }
        if (!facts.ControlUseItem || facts.SelectionBuffered ||
            facts.LocalOwner && facts.PendingInventoryActions) channel = false;
        bool wasAnimating = animation > 0;
        if (wasAnimating) animation--;
        bool pending = wasAnimating && animation == 0 && reuseDelay == 0 && facts.ControlUseItem && release;
        selected = selected with
        {
            Animation = animation,
            AnimationMax = maximum,
            ItemTime = Math.Max(0, selected.ItemTime - 1),
            ReleaseUseItem = !facts.ControlUseItem,
            RevolverCritBonus = crit
        };
        bool local = facts.LocalOwner && facts.AllowedToHoldItems && !facts.JustDroppedItem;
        bool shoot = local && definition.Shoot > 0 && animation > 0 && selected.ItemTime == 0 &&
            (!facts.SelectionBuffered || definition.UseTime <= animation) &&
            (!definition.ShootsEveryUse || animation == maximum - 1);
        var state = new ToolClockState1458(selected, toolTime, previous.PoundRelease, channel) { ReuseDelay = reuseDelay };
        int predictedToolTime = channel ? toolTime > 0 ? toolTime - 1 : definition.UseTime - 1 :
            shoot ? definition.UseTime : selected.ItemTime;
        bool action = local && animation > 0 && facts.ControlUseItem && predictedToolTime == 0;
        // This predicts the source boundary assuming the offered shot succeeds.
        // The caller must still adopt that shot and enter the tool stage before
        // using it; the flag alone is not an action certificate.
        transition = new(state, attempt, began, pending, shoot, action, true)
        { LastUseSuccess = useSuccess, LocalToolStage = local };
        return true;
    }

    public static bool TryEnterOwnerStage(in LocalItemClockDefinition1458 definition,
        in ToolClockState1458 previous, bool ownerStage, out ToolClockState1458 after)
    {
        after = previous;
        if (!Valid(definition) || !previous.IsValid) return false;
        if (!ownerStage) return true;
        int time = previous.Channel ? previous.ToolTime - 1 : previous.Selected.ItemTime;
        if (time < 0) time = definition.UseTime - 1;
        after = previous with { ToolTime = time };
        return true;
    }

    public static bool TryApplyShootTime(in LocalItemClockDefinition1458 definition,
        in ToolClockState1458 previous, float multiplier, bool windowsArithmetic,
        out ToolClockState1458 after)
    {
        after = previous;
        if (!Valid(definition) || !previous.IsValid || definition.Shoot <= 0 ||
            !float.IsFinite(multiplier) || multiplier <= 0f) return false;
        double product = windowsArithmetic ? definition.UseTime * (double)multiplier : definition.UseTime * multiplier;
        if (!double.IsFinite(product) || product >= short.MaxValue + 1d) return false;
        int time = Math.Max(1, (int)product);
        after = previous with { Selected = previous.Selected with { ItemTime = time, ItemTimeMax = time } };
        return true;
    }

    /// <summary>Canonical public SetDefaults metadata only, including empty0; deprecated-to-Air IDs refuse.</summary>
    public static bool TryGetSourceUseStyle(TerraRuntime.Contracts.Gameplay.ItemTypeId item,out byte useStyle) =>
        TerraRuntime.Gameplay.Items.VanillaItemUseStyleFacts1458.TryGet(item,out useStyle);

    private static bool Valid(in LocalItemClockDefinition1458 definition) =>
        definition.UseTime is > 0 and <= short.MaxValue &&
        definition.UseAnimation is > 0 and <= short.MaxValue &&
        definition.ReuseDelay is >= 0 and <= short.MaxValue &&
        definition.UseStyle is > 0 and <= 14 && definition.Shoot >= 0;
    public static bool TryResolveSourceMetadata(TerraRuntime.Contracts.Gameplay.ItemTypeId requested,
        out TerraRuntime.Contracts.Gameplay.ItemTypeId canonical,out byte useStyle) =>
        TerraRuntime.Gameplay.Items.VanillaItemUseStyleFacts1458.TryResolveSourceMetadata(requested,out canonical,out useStyle);

}
