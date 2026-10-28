using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private static bool TryAdvanceWiringFrame(in WiringToolDefinition1458 definition,
        RuntimePlayerToolUse1458 before, in ToolClockState1458 remoteBefore,
        in ToolFrameFacts1458 facts, in RuntimePlayerToolStagePose1458 pose, long update, int critRoll,
        out RuntimePlayerToolUse1458 after, out ToolClockTransition1458 remote)
    {
        after = before; remote = default;
        if (before.LastFrameUpdate == update) return false;
        var clocks = definition.Clocks;
        var remoteFacts = facts with { LocalOwner = false };
        if (!VanillaLocalItemUse1458.TryAdvanceFrame(clocks, remoteBefore, remoteFacts, critRoll, out remote) ||
            !VanillaLocalItemUse1458.TryEnterOwnerStage(clocks, remote.State, remote.LocalToolStage, out var remoteState)) return false;
        remote = remote with { State = remoteState };
        bool changed = before.SelectionInitialized && (before.SourceSlot != pose.Player.SelectedItem ||
            before.SourceItem != definition.Item || before.SourcePrefix.Value != 0);
        bool ready = before.Clocks.Selected.Animation == 0 && before.Clocks.Selected.ItemTime == 0;
        if (!before.LocalClocksKnown || changed && !ready)
        {
            // An unrepresented active previous selection cannot become a local wiring clock.
            after = before with { LocalClocksKnown = false, SourceStagePose = null, Reach = null,
                SourceWiring = null, RequiresFreshUse = true, LastFrameUpdate = update };
            return true;
        }
        var localBefore = before.Clocks;
        if (changed) localBefore = localBefore with { Selected = localBefore.Selected with
            { ItemTime = 0, ItemTimeMax = 0, ReleaseUseItem = facts.ControlUseItem || localBefore.Selected.ReleaseUseItem }, Channel = false };
        var localFacts = facts with { LocalOwner = true, LastUseSuccess = changed || before.LastUseSuccess };
        if (!VanillaLocalItemUse1458.TryAdvanceFrame(clocks, localBefore, localFacts, critRoll, out var local) ||
            !VanillaLocalItemUse1458.TryEnterOwnerStage(clocks, local.State, local.LocalToolStage, out var localState) ||
            local.BeganActualUse && before.UseEpoch == ulong.MaxValue) return false;
        after = before with
        {
            Clocks = localState, SourceWiring = definition, SourceDefinition = null, SourcePlacement = null,
            SourceLocalDefinition = clocks, SelectionInitialized = true, SourceSlot = pose.Player.SelectedItem,
            SourceItem = definition.Item, SourcePrefix = default, DeferredSelectionSlot = null,
            SourceStagePose = pose, ReleaseWitness = null, RequiresFreshUse = false,
            ShootEligible = false, ToolStagePending = false, LastUseSuccess = local.LastUseSuccess,
            UseEpoch = local.BeganActualUse ? before.UseEpoch + 1 : before.UseEpoch, LastFrameUpdate = update,
            PlacementReady = null, PlacementAlternative = null, PlacementHistoryEpoch = 0
        };
        return true;
    }
}
