using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

// A bounded alternative common-clock history: selection was buffered before
// another unobserved PlaceThing action. It owns no target, payment or RNG reset.
internal readonly record struct RuntimePlayerPlacementReady1458(
    ToolClockState1458 Clocks, bool LastUseSuccess, ulong UseEpoch);

internal sealed partial class PlayerAuthority
{
    private static bool TryAdvancePlacementFrame(in LocalPlacementDefinition1458 definition,
        RuntimePlayerToolUse1458 before, in ToolClockState1458 remoteBefore,
        in ToolFrameFacts1458 facts, in RuntimePlayerToolStagePose1458 pose,
        float placementSpeed, long update, int critRoll,
        out RuntimePlayerToolUse1458 after, out ToolClockTransition1458 remote)
    {
        after = before;
        remote = default;
        if (before.LastFrameUpdate == update ||
            !float.IsFinite(placementSpeed) || placementSpeed <= 0f) return false;
        if (!before.LocalClocksKnown)
        {
            // An unknown local operation cannot retire the independent
            // dedicated-player presentation. Advance only that known remote
            // preimage using the one common sample already owned by the phase.
            var presentationDefinition = definition.Clocks;
            var presentationFacts = facts with { LocalOwner = false, AnimationMultiplier = placementSpeed };
            if (!VanillaLocalItemUse1458.TryAdvanceFrame(in presentationDefinition,
                in remoteBefore, in presentationFacts, critRoll, out remote)) return false;
            after = before with
            {
                // Local clocks and knowledge remain untouched. No source-use
                // stage or latent local entitlement can be made from this step.
                SourceStagePose = null, Reach = null, BuildingPermission = null,
                ReleaseWitness = null, PlacementReady = null, PlacementAlternative = null,
                PlacementHistoryEpoch = 0, RequiresFreshUse = true,
                ShootEligible = false, ToolStagePending = false,
                LastFrameUpdate = update
            };
            // Retain Input only until the enclosing callback-free adoption.
            // Its missing local stage then takes the existing retirement path,
            // including any deferred health publication; it cannot be rebased
            // into a later recovered local frame or become a paid action.
            return true;
        }
        var localBefore = before.Clocks;
        bool changedSlot = before.SelectionInitialized && before.SourceSlot != pose.Player.SelectedItem;
        bool changed = before.SelectionInitialized && (changedSlot ||
            before.SourceItem != definition.Item || before.SourcePrefix.Value != 0);
        if (changedSlot && before.PlacementReady is { } qualified &&
            qualified.UseEpoch == before.PlacementHistoryEpoch &&
            qualified.Clocks.Selected.Animation == 0 && qualified.Clocks.Selected.ItemTime == 0)
            localBefore = qualified.Clocks;
        bool ready = localBefore.Selected.Animation == 0 && localBefore.Selected.ItemTime == 0;
        bool deferred = changedSlot && !ready;
        if (changedSlot && ready)
            localBefore = localBefore with { Selected = localBefore.Selected with
                { ItemTime = 0, ItemTimeMax = 0,
                  ReleaseUseItem = facts.ControlUseItem || localBefore.Selected.ReleaseUseItem },
                Channel = false };
        var localFacts = facts with { LocalOwner = true,
            AnimationMultiplier = placementSpeed, LastUseSuccess = changedSlot && ready || before.LastUseSuccess };
        var clocks = definition.Clocks;
        var localDefinition = deferred ? before.SourceLocalDefinition : clocks;
        if (localDefinition is not { } retainedDefinition) return false;
        if (deferred) localFacts = localFacts with { SelectionBuffered = true,
            LastUseSuccess = before.LastUseSuccess,
            AnimationMultiplier = before.SourcePlacement is not null ? before.PlacementSpeed : 1f };
        if (!VanillaLocalItemUse1458.TryAdvanceFrame(in retainedDefinition, in localBefore, in localFacts,
            critRoll, out var local) || local.ShootBoundary ||
            !VanillaLocalItemUse1458.TryEnterOwnerStage(in retainedDefinition,
            local.State, local.LocalToolStage, out var localState)) return false;
        var remoteFacts = facts with { LocalOwner = false, AnimationMultiplier = placementSpeed };
        if (!VanillaLocalItemUse1458.TryAdvanceFrame(in clocks, in remoteBefore, in remoteFacts,
            critRoll, out remote)) return false;
        if (local.BeganActualUse && before.UseEpoch == ulong.MaxValue) return false;
        ulong epoch = local.BeganActualUse ? before.UseEpoch + 1 : before.UseEpoch;
        RuntimePlayerPlacementReady1458? readyHistory = null;
        if (!changed && before.PlacementReady is { } retained && retained.UseEpoch == before.PlacementHistoryEpoch)
        {
            var waitFacts = localFacts with { SelectionBuffered = true, LastUseSuccess = retained.LastUseSuccess };
            if (!VanillaLocalItemUse1458.TryAdvanceFrame(in clocks, retained.Clocks, in waitFacts,
                critRoll, out var wait) || !VanillaLocalItemUse1458.TryEnterOwnerStage(in clocks,
                wait.State, wait.LocalToolStage, out var waited)) return false;
            readyHistory = new(waited, wait.LastUseSuccess, retained.UseEpoch);
        }
        ulong historyEpoch = before.PlacementHistoryEpoch;
        if (local.BeganActualUse && (changed || before.PlacementReady is null))
        {
            readyHistory = new(localState, local.LastUseSuccess, epoch);
            historyEpoch = epoch;
        }
        after = before with
        {
            Clocks = localState, SourcePlacement = deferred ? before.SourcePlacement : definition,
            SourceLocalDefinition = retainedDefinition, SourceDefinition = deferred ? before.SourceDefinition : null,
            PlacementSpeed = deferred ? before.PlacementSpeed : placementSpeed,
            PlacementReady = deferred ? before.PlacementReady : readyHistory, PlacementHistoryEpoch = historyEpoch,
            SelectionInitialized = true, SourceSlot = deferred ? before.SourceSlot : pose.Player.SelectedItem,
            SourceItem = deferred ? before.SourceItem : definition.Item,
            SourcePrefix = deferred ? before.SourcePrefix : default,
            DeferredSelectionSlot = deferred ? pose.Player.SelectedItem : null, RequiresFreshUse = deferred,
            SourceStagePose = pose, ShootEligible = false, ToolStagePending = false,
            LastUseSuccess = local.LastUseSuccess, UseEpoch = epoch, LastFrameUpdate = update,
            ReleaseWitness = null
        };
        return true;
    }

}
