using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Players;

/// <summary>Ordered tool clocks, with the target-free frame separated from local action admission.</summary>
public static class VanillaToolUse1458
{
    // The caller supplies the one common ItemCheck Next(3) result. This does not
    // draw another sample when the remote phase already owns that source draw.
    public static bool TryAdvanceFrame(in ToolDefinition1458 tool, in ToolClockState1458 previous,
        in ToolFrameFacts1458 facts, int critRoll, out ToolClockTransition1458 transition)
    {
        transition = default;
        if (!Valid(tool) || !previous.IsValid || !float.IsFinite(facts.AnimationMultiplier) ||
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
        float animationProduct = tool.UseAnimation * facts.AnimationMultiplier;
        if (!float.IsFinite(animationProduct) || animationProduct >= short.MaxValue + 1f) return false;
        int animationTicks = Math.Max(1, (int)animationProduct);
        var selected = previous.Selected;
        int animation = selected.Animation;
        int maximum = selected.AnimationMax;
        bool release = selected.ReleaseUseItem;
        bool autoLogic = !facts.SelectionBuffered || tool.Shoot > 0 && selected.ItemTime != 0;
        if (autoLogic && tool.AutoReuse && !facts.NoItems)
        {
            release = true;
            if (animation == 1)
            {
                if (!facts.LocalOwner && facts.ControlUseItem && tool.UseStyle == 5 &&
                    tool.Shoot > 0 && facts.LastUseSuccess)
                    animation = maximum = animationTicks;
                else animation = 0;
            }
        }
        if (autoLogic && facts.AutoReuseOverride) release = true;
        if (animation == 0) maximum = 0;
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
                toolTime = 1;
                if (tool.Channel) channel = true;
            }
        }
        if (!facts.ControlUseItem || facts.SelectionBuffered ||
            facts.LocalOwner && facts.PendingInventoryActions) channel = false;
        bool wasAnimating = animation > 0;
        if (wasAnimating) animation--;
        bool pending = wasAnimating && animation == 0 && facts.ControlUseItem && release;
        selected = selected with
        {
            Animation = animation,
            AnimationMax = maximum,
            ItemTime = Math.Max(0, selected.ItemTime - 1),
            ReleaseUseItem = !facts.ControlUseItem,
            RevolverCritBonus = crit
        };
        bool local = facts.LocalOwner && facts.AllowedToHoldItems && !facts.JustDroppedItem;
        bool shoot = local && tool.Shoot > 0 && animation > 0 && selected.ItemTime == 0 &&
            (!facts.SelectionBuffered || tool.UseTime <= animation);
        var state = new ToolClockState1458(selected, toolTime, previous.PoundRelease, channel);
        int predictedToolTime = channel ? toolTime > 0 ? toolTime - 1 : tool.UseTime - 1 :
            shoot ? tool.UseTime : selected.ItemTime;
        bool action = local && animation > 0 && facts.ControlUseItem && predictedToolTime == 0;
        // This predicts the source boundary assuming the offered shot succeeds.
        // The caller must still adopt that shot and enter the tool stage before
        // using it; the flag alone is not an action certificate.
        transition = new(state, attempt, began, pending, shoot, action, true)
        { LastUseSuccess = useSuccess, LocalToolStage = local };
        return true;
    }

    // Call after the held-projectile shoot preparation has applied its source
    // itemTime, before target action. Remote ItemCheck never executes this stage.
    public static bool TryEnterToolStage(in ToolDefinition1458 tool, in ToolClockState1458 previous,
        bool localToolStage, out ToolClockState1458 after)
    {
        after = previous;
        if (!Valid(tool) || !previous.IsValid) return false;
        if (!localToolStage) return true;
        int time = previous.Channel ? previous.ToolTime - 1 : previous.Selected.ItemTime;
        if (time < 0) time = tool.UseTime - 1;
        after = previous with { ToolTime = time };
        return true;
    }

    public static bool TryApplyShootTime(in ToolDefinition1458 tool, in ToolClockState1458 previous,
        out ToolClockState1458 after)
    {
        after = previous;
        if (!Valid(tool) || !previous.IsValid || tool.Shoot <= 0) return false;
        after = previous with
        {
            Selected = previous.Selected with { ItemTime = tool.UseTime, ItemTimeMax = tool.UseTime }
        };
        return true;
    }

    // A successful validation can still offer no action. Pound release clearing
    // precedes the noBuilding/out-of-range return in the original local method.
    public static bool TryPrepareTarget(in ToolClockState1458 previous,
        in ToolActionClockFacts1458 facts, out ToolClockState1458 after, out bool eligible)
    {
        after = previous;
        eligible = false;
        if (!previous.IsValid || !float.IsFinite(facts.PickSpeed) || facts.PickSpeed <= 0f)
            return false;
        bool boundary = previous.ToolTime == 0 && previous.Selected.Animation > 0 && facts.ControlUseItem;
        if (boundary && (!facts.TargetActive || !facts.TargetIsHammer))
            after = after with { PoundRelease = false };
        if (facts.NoBuilding || !facts.TargetInRange) return true;
        eligible = boundary;
        return true;
    }

    public static ToolClockState1458 CompleteTileStage(in ToolClockState1458 previous,
        in ToolActionClockFacts1458 facts) =>
        !facts.NoBuilding && facts.TargetInRange && !(facts.Kind == ToolActionKind1458.Shovel && previous.ToolTime == 0 &&
            previous.Selected.Animation > 0 && facts.ControlUseItem) &&
        facts.ReleaseUseItem ? previous with { PoundRelease = true } : previous;

    public static bool TryApplyActionTime(in ToolDefinition1458 tool, in ToolClockState1458 previous,
        ToolActionKind1458 kind, float pickSpeed, out ToolClockState1458 after)
        => TryApplyActionTime(tool, previous, kind, pickSpeed, false, out after);

    public static bool TryApplyActionTime(in ToolDefinition1458 tool, in ToolClockState1458 previous,
        ToolActionKind1458 kind, float pickSpeed, bool windowsArithmetic, out ToolClockState1458 after)
    {
        after = previous;
        if (!Valid(tool) || !previous.IsValid || !float.IsFinite(pickSpeed) || pickSpeed <= 0f ||
            (uint)kind > (uint)ToolActionKind1458.Pound ||
            kind == ToolActionKind1458.Pick && tool.Pick <= 0 ||
            kind == ToolActionKind1458.Axe && tool.Axe <= 0 ||
            kind is ToolActionKind1458.Hammer or ToolActionKind1458.Wall or ToolActionKind1458.Pound &&
                tool.Hammer <= 0 || kind == ToolActionKind1458.Shovel && tool.Item.Value != 4711) return false;
        var selected = previous.Selected;
        if (kind == ToolActionKind1458.Wall)
            selected = selected with { ItemTime = tool.UseTime / 2 };
        else
        {
            if (kind is ToolActionKind1458.Axe or ToolActionKind1458.Hammer or ToolActionKind1458.Pound)
                selected = selected with { ItemTime = tool.UseTime, ItemTimeMax = tool.UseTime };
            if (kind == ToolActionKind1458.Shovel || tool.Pick > 0 && kind != ToolActionKind1458.Pound)
            {
                double time = windowsArithmetic ? tool.UseTime * (double)pickSpeed : tool.UseTime * pickSpeed;
                if (!double.IsFinite(time) || time >= short.MaxValue + 1d) return false;
                // This direct source assignment has no one-tick clamp and does
                // not replace itemTimeMax set by an earlier generic tool branch.
                selected = selected with { ItemTime = (int)time };
            }
        }
        after = previous with { Selected = selected };
        return true;
    }

    private static bool Valid(in ToolDefinition1458 tool) =>
        !tool.Item.IsNone && tool.UseTime is > 0 and <= short.MaxValue &&
        tool.UseAnimation is > 0 and <= short.MaxValue && tool.UseStyle is 1 or 5;
}
