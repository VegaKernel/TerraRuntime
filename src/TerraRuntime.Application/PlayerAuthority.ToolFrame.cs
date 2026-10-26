using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // An effective client selection is not a certificate that our owned local
    // clocks have finished. Defer the server choice until those clocks permit
    // SelectedItemState.Update; never shorten Animation from packet13/41.
    private static bool TryAdvanceToolFrame(in ToolDefinition1458 tool,
        RuntimePlayerToolUse1458 before, in ToolClockState1458 remoteClocks,
        in ToolFrameFacts1458 sourceFacts, in RuntimePlayerToolStagePose1458 sourcePose,
        long update, int critRoll,
        out RuntimePlayerToolUse1458 after, out ToolClockTransition1458 remote)
    {
        after = before;
        remote = default;
        if (before.LastFrameUpdate == update) return false;
        if (!before.LocalClocksKnown)
        {
            // An unrepresented local operation invalidates local tool actions,
            // not the separately owned dedicated-player ItemCheck. In particular,
            // a placement must not discard paid buffs, mana and remote animation.
            // This branch neither reconstructs local clocks nor grants a birth.
            var presentationFacts = sourceFacts with { LocalOwner = false };
            if (!VanillaToolUse1458.TryAdvanceFrame(in tool, in remoteClocks,
                in presentationFacts, critRoll, out remote)) return false;
            after = before with
            {
                SourceStagePose = null, Reach = null, BuildingPermission = null,
                ReleaseWitness = null, RequiresFreshUse = true,
                ShootEligible = false, ToolStagePending = false,
                LastFrameUpdate = update
            };
            return true;
        }
        bool changedSlot = before.SelectionInitialized && before.SourceSlot != sourcePose.Player.SelectedItem;
        bool changedIdentity = before.SelectionInitialized &&
            (before.SourceItem != tool.Item || before.SourcePrefix != tool.Prefix || changedSlot);
        var localBefore = before.Clocks;
        bool ready = localBefore.Selected.Animation == 0 && localBefore.Selected.ItemTime == 0;
        bool deferred = changedSlot && !ready;
        bool lastSuccess = before.LastUseSuccess;
        if (changedSlot && ready)
        {
            // Source OnSelectionChanged resets time/max, not Animation, ToolTime
            // or HitTile. This is the represented normal buffered continuation;
            // no hidden override-selection or fabricated mouse input is claimed.
            localBefore = localBefore with
            {
                Selected = localBefore.Selected with
                {
                    ItemTime = 0, ItemTimeMax = 0,
                    ReleaseUseItem = sourceFacts.ControlUseItem || localBefore.Selected.ReleaseUseItem
                },
                Channel = false,
                PoundRelease = false
            };
            lastSuccess = true;
        }
        else if (changedIdentity)
            localBefore = localBefore with { Channel = false, PoundRelease = false };

        ToolDefinition1458 localDefinition = tool;
        ToolClockTransition1458 local;
        if (deferred && before.SourceDefinition is not { })
        {
            // Only a proven non-using common clock can bridge an intervening
            // passive selection. An unprojected active owner remains unknown.
            if (!VanillaRemotePassiveItemCatalog1458.TryGet(before.SourceItem, before.SourcePrefix, out var inert) ||
                inert.PermitsUse) return false;
            var passiveFacts = new PlayerRemotePassiveItemFacts1458(before.SourceItem,
                sourceFacts.ControlUseItem, lastSuccess, sourceFacts.NoItems,
                sourceFacts.CrowdControlled, true, before.SourcePrefix);
            if (!VanillaRemotePassiveItemCheck1458.TryStep(localBefore.Selected,
                in passiveFacts, (min, max) => min == 0 && max == 3 ? critRoll :
                    throw new InvalidOperationException("Unexpected common-clock random request."), out var passive)) return false;
            var common = localBefore with { Selected = passive.State, ToolTime = passive.State.ItemTime, Channel = false };
            local = new(common, false, false, passive.PendingItemReuse, false, false, true)
                { LastUseSuccess = lastSuccess, LocalToolStage = false };
        }
        else
        {
            if (deferred) localDefinition = before.SourceDefinition!.Value;
            // This flag describes our owned deferred selector, not a recovered
            // client HasBufferedChange bit. No old-use benefit is admitted.
            var localFacts = sourceFacts with
            {
                LocalOwner = true, LastUseSuccess = lastSuccess,
                SelectionBuffered = sourceFacts.SelectionBuffered || deferred
            };
            if (!VanillaToolUse1458.TryAdvanceFrame(in localDefinition, in localBefore,
                in localFacts, critRoll, out local)) return false;
        }
        if (local.BeganActualUse && before.UseEpoch == ulong.MaxValue) return false;
        var remoteFacts = sourceFacts with { LocalOwner = false };
        if (!VanillaToolUse1458.TryAdvanceFrame(in tool, in remoteClocks,
            in remoteFacts, critRoll, out remote)) return false;

        var localState = local.State;
        bool requiresFresh = deferred || (before.RequiresFreshUse || changedIdentity) && !local.BeganActualUse;
        bool pending = local.ShootBoundary && !requiresFresh;
        if (!pending && local.LocalToolStage && !VanillaToolUse1458.TryEnterToolStage(in localDefinition,
            in localState, true, out localState)) return false;
        if (deferred) localState = localState with { Channel = false };
        after = before with
        {
            Clocks = localState,
            SourceStagePose = sourcePose,
            RequiresFreshUse = requiresFresh,
            SelectionInitialized = true,
            DeferredSelectionSlot = deferred ? sourcePose.Player.SelectedItem : null,
            SourceItem = deferred ? before.SourceItem : tool.Item,
            SourcePrefix = deferred ? before.SourcePrefix : tool.Prefix,
            SourceSlot = deferred ? before.SourceSlot : sourcePose.Player.SelectedItem,
            SourceDefinition = deferred ? before.SourceDefinition : tool,
            ReleaseWitness = changedIdentity ? null : before.ReleaseWitness,
            LastUseSuccess = local.LastUseSuccess,
            UseEpoch = local.BeganActualUse ? before.UseEpoch + 1 : before.UseEpoch,
            LastFrameUpdate = update,
            ShootEligible = local.ShootBoundary && !requiresFresh,
            ToolStagePending = pending
        };
        return true;
    }
}
