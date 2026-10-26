using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // An effective client selection is not a certificate that our owned local
    // clocks have finished. Defer the server choice until those clocks permit
    // SelectedItemState.Update; never shorten Animation from packet13/41.
    private bool TryAdvanceToolFrame(in ToolDefinition1458 tool,
        RuntimePlayerToolUse1458 before, in ToolClockState1458 remoteClocks,
        in ToolFrameFacts1458 sourceFacts, in RuntimePlayerToolStagePose1458 sourcePose,
        long update, int critRoll,
        out RuntimePlayerToolUse1458 after, out ToolClockTransition1458 remote)
    {
        after = before;
        remote = default;
        if (before.LastFrameUpdate == update) return false;
        var selectedItem=new RuntimePlayerInventoryItem(tool.Item,1,tool.Prefix,0);
        if(TryConsumePrepaidWeaponClock(before,sourcePose.Player.SelectedItem,in selectedItem,
            sourceFacts.WindowsArithmetic,critRoll,out var prepaid))
        {
            var presentation=sourceFacts with {LocalOwner=false};
            if(!VanillaToolUse1458.TryAdvanceFrame(tool,remoteClocks,presentation,critRoll,out remote))return false;
            after=prepaid;return true;
        }
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
        if (changedSlot && before.PlacementReady is { } qualified &&
            qualified.UseEpoch == before.PlacementHistoryEpoch &&
            qualified.Clocks.Selected.Animation == 0 && qualified.Clocks.Selected.ItemTime == 0)
            localBefore = qualified.Clocks;
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
        if (deferred && before.SourceLocalDefinition is { } oldCommon)
        {
            var oldFacts = sourceFacts with { LocalOwner = true, LastUseSuccess = lastSuccess,
                SelectionBuffered = true, AnimationMultiplier = before.SourcePlacement is not null
                    ? before.PlacementSpeed : sourceFacts.AnimationMultiplier };
            if (!VanillaLocalItemUse1458.TryAdvanceFrame(oldCommon, localBefore, oldFacts, critRoll, out local) ||
                local.ShootBoundary || !VanillaLocalItemUse1458.TryEnterOwnerStage(oldCommon,
                    local.State, local.LocalToolStage, out var common)) return false;
            local = local with { State = common, LocalToolStage = false };
        }
        else if (deferred && before.SourceDefinition is not { })
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
            SourceLocalDefinition = deferred ? before.SourceLocalDefinition :
                new LocalItemClockDefinition1458(tool.UseTime, tool.UseAnimation, (byte)tool.UseStyle,
                    tool.AutoReuse, tool.Channel, tool.Shoot) { ResetsToolTimeOnStart = true },
            SourcePlacement = deferred ? before.SourcePlacement : null,
            PlacementReady = deferred ? before.PlacementReady : null,
            WeaponShoot = null,
            // A source released pick/axe/shovel can enable a subsequent hammer.
            // Keep only this direct known tool selector lineage. An eligible
            // intervening nonhammer use with no qualified target explanation is
            // not evidence that its source pounding flag survived.
            ReleaseWitness = changedIdentity && tool.Hammer <= 0 ||
                sourceFacts.ControlUseItem && local.LocalToolStage && localDefinition.Hammer <= 0 &&
                (localDefinition.Pick > 0 || localDefinition.Axe > 0) &&
                localState.ToolTime == 0 && localState.Selected.Animation > 0
                ? null : before.ReleaseWitness,
            LastUseSuccess = local.LastUseSuccess,
            UseEpoch = local.BeganActualUse ? before.UseEpoch + 1 : before.UseEpoch,
            LastFrameUpdate = update,
            ShootEligible = local.ShootBoundary && !requiresFresh,
            ToolStagePending = pending
        };
        return true;
    }
    private RuntimePlayerReleasedToolClockAlternative1458? CaptureReleasedBufferedToolClock(
        in ToolDefinition1458 tool, RuntimePlayerToolUse1458 before, RuntimePlayerToolUse1458 after,
        in ToolFrameFacts1458 facts, int critRoll, RuntimePlayerMember member, PlayerBuffState buffs,
        in RuntimePlayerUpdateWorld1458 world, VanillaUnifiedRandom1458 random, int remoteCrit)
    {
        if (facts.ControlUseItem || facts.SelectionBuffered || !tool.AutoReuse || tool.Channel || tool.Shoot != 0 ||
            before.Clocks.Selected.Animation != 1 || after.Clocks.Selected.Animation != 0 ||
            !before.SelectionInitialized || !before.LocalClocksKnown || !after.LocalClocksKnown ||
            before.SourceSlot != member.SelectedItem || before.SourceItem != tool.Item || before.SourcePrefix != tool.Prefix ||
            before.DeferredSelectionSlot is not null || after.DeferredSelectionSlot is not null ||
            before.WeaponShoot is not null || after.WeaponShoot is not null || before.SourcePlacement is not null ||
            before.PlacementAlternative is not null || before.PlacementReady is not null ||
            after.Input is not null || member.CursorMove is not null || after.BuildingPermission is not { IsCurrent: true, NoBuilding: false } ||
            buffs.HadOwnedFoodEffect(currentCombatTick)) return null;
        foreach (var actor in membership.Members) if (!ReferenceEquals(actor, member)) return null;
        foreach (var buff in buffs.CaptureTypes()) if (buff != default) return null;
        if (!inventory.TryCaptureShopJournal(member.Connection, out var image) || image is null) return null;
        ulong possible = 0;
        for (byte slot = 0; slot < 59; slot++)
        {
            var item = image.Before[slot];
            if (slot != member.SelectedItem && !item.IsEmpty && item.IsCanonical &&
                VanillaToolCatalog1458.TryResolve(item.ItemType, item.Prefix, world.RemixWorld,
                    world.WindowsItemPrefixArithmetic, out var selected) && selected.Hammer > 0) possible |= 1UL << slot;
        }
        if (possible == 0) return null;
        var buffered = facts with { LocalOwner = true, LastUseSuccess = before.LastUseSuccess, SelectionBuffered = true };
        if (!VanillaToolUse1458.TryAdvanceFrame(tool, before.Clocks, buffered, critRoll, out var branch) ||
            branch.BeganActualUse || branch.ShootBoundary || branch.AttemptedUse ||
            !VanillaToolUse1458.TryEnterToolStage(tool, branch.State, branch.LocalToolStage, out var alternate) ||
            alternate.Selected.Animation != 0 || alternate.Selected.AnimationMax == after.Clocks.Selected.AnimationMax ||
            (alternate with { Selected = alternate.Selected with { AnimationMax = after.Clocks.Selected.AnimationMax } }) != after.Clocks)
            return null;
        return new(after, alternate, image, buffs, random, world, member.Connection,
            currentCombatTick, remoteCrit, possible);
    }

}
