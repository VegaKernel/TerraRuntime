using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // This first admission slice has one represented human, no pending cursor
    // operation and an empty buff owner. Expanding it requires comparing every
    // additional RNG consumer's retained afterimage, not only two endpoints.
    private bool HasSinglePlacementAlternativeActor(RemoteItemPhasePlan1458 plan)
    {
        int count = 0;
        foreach (var member in membership.Members)
        {
            if (!ReferenceEquals(member, plan.Member)) return false;
            count++;
        }
        if (count != 1 || plan.Member.CursorMove is not null) return false;
                if (plan.BeforeItem is not { } phase || phase.Selected.PotionDelay != 0 ||
            phase.ToolUse?.Clocks.Selected.PotionDelay != 0) return false;
        foreach (var buff in plan.BeforeBuffs.CaptureTypes()) if (buff != default) return false;
        return true;
    }

    private bool IsPlacementAlternativeContext(RemoteItemPhasePlan1458 plan,
        RuntimePlayerPlacementAlternative1458 history, in RuntimePlayerUpdateWorld1458 world)
    {
        return HasSinglePlacementAlternativeActor(plan) && history.World == world && playerUpdateWorld == world &&
            history.Inventory.IsCurrent && history.Reads.IsCurrent &&
            plan.BeforeItem is { } previous &&
            (previous with { ToolUse = null }) == history.ExpectedPhaseWithoutTools &&
            previous.ToolUse is { } tools &&
            (tools with { Input = null, PlacementAlternative = null }) == history.ExpectedCanonicalTools &&
            history.Appearance == plan.Appearance &&
            history.Equipment.AsSpan().SequenceEqual(plan.Equipment) &&
            history.PermanentMask == plan.Member.OwnedPermanentUpgradeMask &&
            transferProfiles.IsCurrentBuffState(plan.Member.Connection, history.Buffs);
    }

    private bool TryChoosePlacementAlternative(RemoteItemPhasePlan1458 plan,
        in RuntimePlayerUpdateWorld1458 world, VanillaUnifiedRandom1458 random,
        ref RuntimePlayerItemPhase1458 item)
    {
        if (item.ToolUse is not { PlacementAlternative: { } history } tools ||
            tools.SourceSlot == plan.Member.SelectedItem) return true;
        // Prefer the source history that needed no hidden action whenever it is
        // already ready. A packet13 does not create an earlier use entitlement.
        bool simpleReady = tools.PlacementReady is { } simple &&
            simple.UseEpoch == tools.PlacementHistoryEpoch &&
            simple.Clocks.Selected.ItemTime == 0 && simple.Clocks.Selected.Animation == 0;
        if (simpleReady || !history.Ready || history.HistoryEpoch != tools.PlacementHistoryEpoch ||
            !IsPlacementAlternativeContext(plan, history, world) || !history.MatchesCanonical(random) ||
            item.Selected.RevolverCritBonus != history.CanonicalRemoteCrit)
        {
            item = item with { ToolUse = tools with { PlacementAlternative = null } };
            return true;
        }
        // Both the local clock and the remote common RNG-dependent field are
        // retained source afterimages of this SAME candidate. No other consumer
        // is left on an incompatible already-published result.
        item = item with
        {
            Selected = item.Selected with { RevolverCritBonus = history.AlternativeRemoteCrit },
            ToolUse = tools with
            {
                Clocks = history.Clocks, ReplacementHits = history.ReplacementHits,
                PlacementReady = new(history.Clocks, tools.LastUseSuccess, history.HistoryEpoch),
                PlacementAlternative = null
            }
        };
        // random is the existing whole-phase detached preview. The live owner is
        // adopted only after the enclosing complete census/preimage checks.
        plan.PlacementChosenAlternative = history;
        random.CopyStateFrom(history.CreateAlternativePreview());
        return true;
    }

    private void AdvancePlacementAlternative(RemoteItemPhasePlan1458 plan,
        in RuntimePlayerUpdateWorld1458 world, VanillaUnifiedRandom1458 beforeCommon,
        VanillaUnifiedRandom1458 afterCommon, int critRoll, in ToolFrameFacts1458 facts)
    {
        if (plan.Item is not { ToolUse: { SourcePlacement: { } placement } tools } item ||
            tools.SourceSlot != plan.Member.SelectedItem || tools.DeferredSelectionSlot is not null) return;
        RuntimePlayerPlacementAlternative1458? next = null;
        var oneDraw = beforeCommon.Clone();
        bool oneCommonOnly = oneDraw.Next(3) == critRoll && oneDraw.HasSameState(afterCommon);
        if (!oneCommonOnly || !HasSinglePlacementAlternativeActor(plan))
        {
            plan.Item = item with { ToolUse = tools with { PlacementAlternative = null } };
            return;
        }
        if (plan.BeforeItem?.ToolUse?.PlacementAlternative is { } old &&
            old.HistoryEpoch == tools.PlacementHistoryEpoch &&
            old.MatchesCanonical(beforeCommon) && IsPlacementAlternativeContext(plan, old, world))
        {
            var alternative = old.CreateAlternativePreview();
            int alternativeCrit = alternative.Next(3);
            var definition = placement.Clocks;
            var alternativeFacts = facts with { LocalOwner = true, SelectionBuffered = true,
                LastUseSuccess = tools.LastUseSuccess, AnimationMultiplier = tools.PlacementSpeed };
            if (VanillaLocalItemUse1458.TryAdvanceFrame(definition, old.Clocks, alternativeFacts,
                alternativeCrit, out var use) && !use.ShootBoundary &&
                VanillaLocalItemUse1458.TryEnterOwnerStage(definition, use.State, use.LocalToolStage, out var clocks) &&
                old.AlternativeRemoteCrit >= int.MinValue + 2)
                next = new(afterCommon, alternative, clocks, old.ReplacementHits, old.HistoryEpoch,
                    item, old.Inventory, old.Reads, plan.Buffs, plan.Equipment, plan.Appearance,
                    item.Selected.RevolverCritBonus, old.AlternativeRemoteCrit - (alternativeCrit == 0 ? 2 : 0),
                    plan.Member.OwnedPermanentUpgradeMask, world);
        }
        else if (tools.Input is null && tools.PlacementReady is { } history &&
            history.UseEpoch == tools.PlacementHistoryEpoch && toolWorld is { } sourceWorld &&
            inventory.TryCaptureShopJournal(plan.Member.Connection, out var image) && image is not null &&
            sourceWorld.TryPrepareIncompletePlacementClock(tools, world, image.Before,
                item.PermanentSpeeds?.PickSpeed ?? item.DerivedCombat!.Value.PickSpeed,
                afterCommon, out var clocks, out var replacementHits, out var alternative, out var reads) &&
            alternative is not null && reads is not null)
            next = new(afterCommon, alternative, clocks, replacementHits, history.UseEpoch,
                item, image, reads, plan.Buffs, plan.Equipment, plan.Appearance,
                item.Selected.RevolverCritBonus, item.Selected.RevolverCritBonus,
                plan.Member.OwnedPermanentUpgradeMask, world);
        plan.Item = item with { ToolUse = tools with { PlacementAlternative = next } };
    }
}
