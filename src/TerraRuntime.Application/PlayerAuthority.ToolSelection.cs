using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private RuntimePlayerToolUse1458 TrackNonToolLocalClock(RuntimePlayerToolUse1458 before,
        byte slot, in RuntimePlayerInventoryItem selected, in PlayerSelectedConsumableFacts1458 facts,
        in PlayerSelectedConsumableState1458 remoteBefore, in PlayerSelectedConsumableTransition1458 remoteUse,
        RuntimePlayerMagicPayment1458? payment, bool ownedConsumableClock,
        in RuntimePlayerItemPhase1458 phase, RemoteItemPhasePlan1458 plan)
    {
        if (TryTrackLocalWeaponClock(before, slot, in selected, in facts,
            in remoteBefore, in remoteUse, in phase, plan, out var ownedWeapon)) return ownedWeapon;
        bool changed = before.SelectionInitialized && (before.SourceSlot != slot ||
            before.SourceItem != selected.ItemType || before.SourcePrefix != selected.Prefix);
        var local = before.Clocks;
        bool known = before.LocalClocksKnown;
        bool success = before.LastUseSuccess;
        bool ready = local.Selected.Animation == 0 && local.Selected.ItemTime == 0;
        bool deferred = changed && !ready;
        if (changed && ready)
        {
            local = local with
            {
                Selected = local.Selected with { ItemTime = 0, ItemTimeMax = 0,
                    ReleaseUseItem = facts.ControlUseItem || local.Selected.ReleaseUseItem },
                Channel = false, PoundRelease = false
            };
            success = true;
        }
        RuntimePlayerWeaponShoot1458? pendingGem=null;
        LocalItemClockDefinition1458? retainedDefinition=null;
        if ((plan.LocalGemFrame is not null || payment is {GemStaff:true,AwaitingPhase:false} ownedGem &&
            ownedGem.Item==selected && ownedGem.Slot==slot) && VanillaGemStaff1458.TryGetWeapon(selected.ItemType,out _))
        {
            // Reuse the already owned local producer. Its source clock is not the
            // dedicated remote renewal clock and is not reconstructed from41.
            var gem=payment;
            var sourceGemClocks=plan.LocalGemFrame?.State??gem!.Value.GemClocks;
            local = local with { Selected = sourceGemClocks, Channel = false };
            if(playerUpdateWorld is not { } gemWorld ||
                !VanillaGemStaff1458.TryResolveWeapon(selected.ItemType,selected.Prefix,gemWorld.WindowsItemPrefixArithmetic,out var gemWeapon,out _) ||
                !VanillaLocalItemUse1458.TryGetSourceUseStyle(selected.ItemType,out byte gemStyle))
            {known=false;}
            else
            {
                retainedDefinition=new(gemWeapon.UseTime,gemWeapon.Animation,gemStyle,gemWeapon.AutoReuse,false,gemWeapon.Projectile.Value)
                    {Style5AnimationBonus=gemWeapon.AutoReuse};
                if(!VanillaLocalItemUse1458.TryEnterOwnerStage(retainedDefinition.Value,local,true,out local))known=false;
                else known=true;
            }
            if(before.WeaponShoot is {PendingGem:true,PendingReport:not null,PendingCapture:{ } candidate} retained &&
                IsOrderedLocalMagicCaptureCurrent(candidate) && gem is { } eligible &&
                (eligible.EligibleBirth && !eligible.BirthConsumed || eligible.MidpointEligible && !eligible.MidpointConsumed))
                pendingGem=retained with {PendingPhaseReady=true};
            success = plan.LocalGemFrame?.LastUseSuccess??gem!.Value.GemLastUseSuccess;
            deferred = false;
        }
        else if (known && !deferred && ownedConsumableClock &&
            (local.Selected with { PotionDelay = remoteBefore.PotionDelay }) == remoteBefore)
        {
            // The funded/denied local consumer already executed this exact
            // preimage and its visual RNG tail. Reuse that owned transition,
            // never packet41 or a second evaluation of the random stream.
            local = local with
            {
                Selected = remoteUse.State,
                ToolTime = remoteUse.OwnerToolTime ?? local.ToolTime,
                Channel = false
            };
            success = true;
            // Keep the definition of this already owned consumable tail for a
            // buffered switch to a pole/tool/weapon. The next selected family
            // must not discard real clocks merely because its predecessor was
            // a paid potion. Canonical metadata cannot recover unknown clocks.
            if (VanillaSelectedConsumableCatalog1458.TryGet(selected.ItemType, out var consumable) &&
                consumable.UseTime > 0 && consumable.UseAnimation > 0 &&
                VanillaLocalItemUse1458.TryGetSourceUseStyle(selected.ItemType, out byte style) && style > 0)
                retainedDefinition = new(consumable.UseTime, consumable.UseAnimation, style, false, false, 0);
        }
        else if (known && (selected.IsEmpty ||
            VanillaRemotePassiveItemCatalog1458.TryGet(selected.ItemType, selected.Prefix, out var passive) &&
                (!passive.PermitsUse || !facts.ControlUseItem)))
        {
            // For these non-using common paths, Next(3) affects only ==0. The
            // actual common draw has already advanced the phase stream once.
            int delta = remoteBefore.RevolverCritBonus - remoteUse.State.RevolverCritBonus;
            int crit = delta == 2 ? 0 : delta == 0 ? 1 : -1;
            if (crit < 0) known = false;
            else if (selected.IsEmpty)
            {
                var air = facts with { SelectionBuffered = deferred };
                if (!VanillaSelectedConsumable1458.TryStep(local.Selected, in air,
                    (min, max) => min == 0 && max == 3 ? crit : throw new InvalidOperationException("Unexpected empty-item draw."),
                    () => throw new InvalidOperationException("Unexpected empty-item draw."), out var next)) known = false;
                else local = local with { Selected = next.State, ToolTime = next.State.ItemTime, Channel = false };
            }
            else
            {
                var common = new PlayerRemotePassiveItemFacts1458(selected.ItemType, facts.ControlUseItem,
                    success, facts.Cursed, facts.CrowdControlled, deferred, selected.Prefix);
                if (!VanillaRemotePassiveItemCheck1458.TryStep(local.Selected, in common,
                    (min, max) => min == 0 && max == 3 ? crit : throw new InvalidOperationException("Unexpected common-item draw."),
                    out var next)) known = false;
                else local = local with { Selected = next.State, ToolTime = next.State.ItemTime, Channel = false };
            }
        }
        else known = false;
        return before with
        {
            Clocks = local, LocalClocksKnown = known, SelectionInitialized = true,
            SourceLocalDefinition = retainedDefinition, WeaponShoot = pendingGem,
            SourceItem = deferred ? before.SourceItem : selected.ItemType,
            SourcePrefix = deferred ? before.SourcePrefix : selected.Prefix,
            SourceSlot = deferred ? before.SourceSlot : slot,
            SourceDefinition = deferred ? before.SourceDefinition : null,
            DeferredSelectionSlot = deferred ? slot : null,
            RequiresFreshUse = true, LastUseSuccess = success,
            ReleaseWitness = changed ? null : before.ReleaseWitness,
            SourceStagePose = null, Reach = null, BuildingPermission = null,
            ShootEligible = false, ToolStagePending = false,
            LastFrameUpdate = pendingGem is not null ? currentCombatTick : before.LastFrameUpdate
        };
    }
}
