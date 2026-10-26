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
        if (!Valid(tool)) return false;
        var definition = new LocalItemClockDefinition1458(tool.UseTime, tool.UseAnimation,
            (byte)tool.UseStyle, tool.AutoReuse, tool.Channel, tool.Shoot) { ResetsToolTimeOnStart = true };
        return VanillaLocalItemUse1458.TryAdvanceFrame(in definition, in previous, in facts,
            critRoll, out transition);
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
