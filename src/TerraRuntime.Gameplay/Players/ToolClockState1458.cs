namespace TerraRuntime.Gameplay.Players;

public readonly record struct ToolClockState1458(PlayerSelectedConsumableState1458 Selected,
    int ToolTime, bool PoundRelease, bool Channel)
{
    public static ToolClockState1458 Constructor => new(default, 0, false, false);
    public bool IsValid => ToolTime is >= 0 and <= short.MaxValue &&
        Selected.ItemTime is >= 0 and <= short.MaxValue &&
        Selected.ItemTimeMax is >= 0 and <= short.MaxValue &&
        Selected.Animation is >= 0 and <= short.MaxValue &&
        Selected.AnimationMax is >= 0 and <= short.MaxValue &&
        Selected.RevolverCritBonus >= int.MinValue + 2;
}

// No target is present at this boundary. NoItems gates a fresh start; it is
// distinct from raw Cursed and does not cancel an already active tool swing.
public readonly record struct ToolFrameFacts1458(bool LocalOwner, bool ControlUseItem,
    bool LastUseSuccess, bool NoItems, bool CrowdControlled, bool SelectionBuffered,
    bool PendingInventoryActions, float AnimationMultiplier,
    bool AllowedToHoldItems = true, bool JustDroppedItem = false,
    bool SourceUseAllowed = true, bool AutoReuseOverride = false);

public readonly record struct ToolClockTransition1458(ToolClockState1458 State,
    bool AttemptedUse, bool BeganActualUse, bool PendingItemReuse, bool ShootBoundary,
    bool ActionBoundary, bool CritSampleRequired)
{
    public bool LastUseSuccess { get; init; }
    public bool LocalToolStage { get; init; }
}

public enum ToolActionKind1458 : byte
{
    Pick,
    Axe,
    Hammer,
    Wall,
    Shovel,
    Pound
}

public readonly record struct ToolActionClockFacts1458(ToolActionKind1458 Kind,
    bool ControlUseItem, bool NoBuilding, bool TargetInRange, bool TargetActive,
    bool TargetIsHammer, bool ReleaseUseItem, float PickSpeed, float ItemTimeMultiplier);
