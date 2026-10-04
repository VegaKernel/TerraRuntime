using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Worlds;

public sealed partial class RuntimeWorldItemStore
{
    /// <summary>Changes only tint for the exact retained item revision; a reused or claimed slot cannot be recolored.</summary>
    public bool TryApplyColor(in WorldItemSnapshot expected, WorldItemColor color, out WorldItemSnapshot snapshot)
    {
        snapshot = default;
        if (!expected.IsActive || !IsValidSlot(expected.Handle.Slot)) return false;
        BeginWrite();
        try
        {
            ref var state = ref _slots[expected.Handle.Slot];
            if (!state.Active || state.Claimed || Capture(expected.Handle.Slot, state) != expected ||
                !TryAdvance(ref state.Revision)) return false;
            state.Update = state.Update with { Color = color };
            snapshot = Capture(expected.Handle.Slot, state);
        }
        finally { EndWrite(); }
        Publish(WorldItemStateCommitKind.Color, in snapshot);
        return true;
    }
}
