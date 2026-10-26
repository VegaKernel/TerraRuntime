using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

// One bounded source-Shoot boundary in the existing local item phase. It is
// retired on the next owned frame if no matching paid producer completes it.
internal sealed record RuntimePlayerWeaponShoot1458(PlayerHandle Player, byte Slot,
    RuntimePlayerInventoryItem Weapon, ulong Epoch, ToolClockState1458 Before,
    ToolClockTransition1458? Frame, bool Committed, bool SlowMagic,
    float AnimationMultiplier, bool HadAmmo)
{
    internal ToolClockState1458? PaidClocks { get; init; }
    // Untrusted intent only: one bounded source-Shoot cohort, with at most
    // two synchronous Gem reports. No mana, RNG, clock or projectile is adopted
    // before its ordered phase.
    internal TerrariaProjectileUpdateState? PendingReport { get; init; }
    internal RuntimePlayerProjectileUseCapture? PendingCapture { get; init; }
    internal PlayerStateSnapshot? PendingLaunchPose { get; init; }
    internal PlayerManaCommitRequest? PendingManaReport { get; init; }
    internal TerrariaProjectileUpdateState? PendingSecondReport { get; init; }
    internal bool PendingGem { get; init; }
    internal bool PendingPhaseReady { get; init; }
}

internal readonly record struct RuntimePlayerWeaponClockPreparation1458(
    RuntimePlayerToolUse1458 Before, RuntimePlayerToolUse1458 After, bool PreserveRemote);

internal sealed partial class PlayerAuthority
{
    private bool TryTrackLocalWeaponClock(RuntimePlayerToolUse1458 before, byte slot,
        in RuntimePlayerInventoryItem selected, in PlayerSelectedConsumableFacts1458 facts,
        in PlayerSelectedConsumableState1458 remoteBefore, in PlayerSelectedConsumableTransition1458 remoteUse,
        in RuntimePlayerItemPhase1458 phase, RemoteItemPhasePlan1458 plan,
        out RuntimePlayerToolUse1458 after)
    {
        after=before;
        if(TryTrackOrderedAmmoClock(before,slot,in selected,in facts,in remoteBefore,in remoteUse,in phase,plan,out after))return true;
        if (toolWorld is null || playerUpdateWorld is not { } world) return false;
        int delta=remoteBefore.RevolverCritBonus-remoteUse.State.RevolverCritBonus;
        int roll=delta==2?0:delta==0?1:-1;
        if(TryConsumePrepaidWeaponClock(before,slot,in selected,world.WindowsItemPrefixArithmetic,roll,out after))return true;
        bool currentWeapon=VanillaLocalWeaponClockCatalog1458.TryResolve(selected.ItemType,selected.Prefix,
            world.WindowsItemPrefixArithmetic,out var definition);
        bool changed=!before.SelectionInitialized || before.SourceSlot!=slot ||
            before.SourceItem!=selected.ItemType || before.SourcePrefix!=selected.Prefix;
        var clocks=before.Clocks;
        bool ready=clocks.Selected.Animation==0 && clocks.Selected.ItemTime==0 && clocks.ReuseDelay==0;
        bool deferred=changed && !ready;
        if (!currentWeapon && !(deferred && before.SourceLocalDefinition is not null)) return false;
        var receipt=before.WeaponShoot;
        if (receipt is { Committed:false,PendingReport:null })
        {
            after=before with {LocalClocksKnown=false,WeaponShoot=null,RequiresFreshUse=true};
            return true;
        }
        if (!before.LocalClocksKnown && receipt is null) return true;
        if(receipt is {PendingReport:not null,PendingCapture:{ } pendingCapture} &&
            (!IsOrderedLocalMagicCaptureCurrent(pendingCapture) || !ReferenceEquals(pendingCapture.Member,plan.Member)))
        {
            // Lost source input retires only the unqualified report. It cannot
            // be refreshed from a later raw selection, inventory or generation.
            receipt=null;
        }
        if (deferred)
        {
            if(before.SourceLocalDefinition is not { } old) {after=before with {LocalClocksKnown=false};return true;}
            definition=old;
        }
        else if(changed)
        {
            clocks=clocks with {Selected=clocks.Selected with {ItemTime=0,ItemTimeMax=0,
                ReleaseUseItem=facts.ControlUseItem || clocks.Selected.ReleaseUseItem},Channel=false,PoundRelease=false};
        }
        bool hasAmmo=receipt?.HadAmmo ?? HasPhysicalWeaponAmmo(in selected,plan.InventoryBefore);
        bool slow=receipt?.PendingReport is not null?phase.SlowMagicUse:receipt?.SlowMagic ?? phase.SlowMagicUse;
        float multiplier=receipt?.AnimationMultiplier ??
            (VanillaRemoteMeleeItemCatalog1458.TryGet(selected.ItemType,out _) ? phase.DerivedCombat?.MeleeAnimationMultiplier ?? 1f : slow?2.5f:1f);
        var sourceFacts=new ToolFrameFacts1458(true,facts.ControlUseItem,before.LastUseSuccess,
            facts.Cursed,facts.CrowdControlled,deferred || facts.SelectionBuffered,false,multiplier)
            {SourceUseAllowed=hasAmmo,WindowsArithmetic=world.WindowsItemPrefixArithmetic};
        if(receipt is {Committed:true,Frame:null})
        {
            // The trusted packet-first producer already captured this exact
            // local preimage. A later control release cannot replay its cost.
            clocks=receipt.Before;
            sourceFacts=sourceFacts with {ControlUseItem=true,SelectionBuffered=false};
        }
        if(roll<0 || !VanillaLocalItemUse1458.TryAdvanceFrame(definition,clocks,sourceFacts,roll,out var frame))
        {after=before with {LocalClocksKnown=false,WeaponShoot=null,RequiresFreshUse=true};return true;}
        if(frame.BeganActualUse && before.UseEpoch==ulong.MaxValue)
        {after=before with {LocalClocksKnown=false,WeaponShoot=null,RequiresFreshUse=true};return true;}
        ulong epoch=frame.BeganActualUse?before.UseEpoch+1:before.UseEpoch;
        var state=frame.State;
        bool pending=frame.ShootBoundary && !deferred;
        if(pending && receipt is {Committed:true})
        {
            if(receipt.Player!=plan.Member.Connection.Player || receipt.Slot!=slot || receipt.Weapon!=selected ||
                !VanillaLocalItemUse1458.TryApplyShootTime(definition,state,slow?2.5f:1f,
                    world.WindowsItemPrefixArithmetic,out state))
            {after=before with {LocalClocksKnown=false,WeaponShoot=null,RequiresFreshUse=true};return true;}
            pending=false;
        }
        if(!pending && !VanillaLocalItemUse1458.TryEnterOwnerStage(definition,state,frame.LocalToolStage,out state))
        {after=before with {LocalClocksKnown=false,WeaponShoot=null,RequiresFreshUse=true};return true;}
        after=before with
        {
            Clocks=state,LocalClocksKnown=!pending,SelectionInitialized=true,
            SourceItem=deferred?before.SourceItem:selected.ItemType,
            SourcePrefix=deferred?before.SourcePrefix:selected.Prefix,SourceSlot=deferred?before.SourceSlot:slot,
            SourceDefinition=deferred?before.SourceDefinition:null,SourceLocalDefinition=definition,DeferredSelectionSlot=deferred?slot:null,
            LastUseSuccess=frame.LastUseSuccess,RequiresFreshUse=deferred,
            WeaponShoot=pending?new(plan.Member.Connection.Player,slot,selected,epoch,clocks,frame,false,
                slow,multiplier,hasAmmo){PendingReport=receipt?.PendingReport,PendingCapture=receipt?.PendingCapture,
                    PendingLaunchPose=receipt?.PendingLaunchPose,PendingManaReport=receipt?.PendingManaReport}:null,
            UseEpoch=epoch,LastFrameUpdate=currentCombatTick,
            SourceStagePose=null,Reach=null,BuildingPermission=null,ReleaseWitness=null,
            ShootEligible=false,ToolStagePending=false
        };
        return true;
    }

    private bool TryConsumePrepaidWeaponClock(RuntimePlayerToolUse1458 before,byte selectedSlot,
        in RuntimePlayerInventoryItem selected,bool windows,int roll,out RuntimePlayerToolUse1458 after)
    {
        after=before;
        if(before.WeaponShoot is not {Committed:true,Frame:null} receipt)return false;
        if(roll<0 || receipt.PaidClocks is not { } paidClocks || before.Clocks!=paidClocks ||
            receipt.Epoch!=before.UseEpoch || !VanillaLocalWeaponClockCatalog1458.TryResolve(receipt.Weapon.ItemType,
                receipt.Weapon.Prefix,windows,out var definition))
        {after=before with {LocalClocksKnown=false,WeaponShoot=null,RequiresFreshUse=true};return true;}
        var facts=new ToolFrameFacts1458(true,true,before.LastUseSuccess,false,false,false,false,
            receipt.AnimationMultiplier){WindowsArithmetic=windows};
        if(!VanillaLocalItemUse1458.TryAdvanceFrame(definition,receipt.Before,facts,roll,out var frame) ||
            !frame.ShootBoundary || !VanillaLocalItemUse1458.TryApplyShootTime(definition,frame.State,
                receipt.SlowMagic?2.5f:1f,windows,out var clocks) ||
            !VanillaLocalItemUse1458.TryEnterOwnerStage(definition,clocks,frame.LocalToolStage,out clocks))
        {after=before with {LocalClocksKnown=false,WeaponShoot=null,RequiresFreshUse=true};return true;}
        bool deferred=selectedSlot!=receipt.Slot || selected!=receipt.Weapon;
        after=before with {Clocks=clocks,LocalClocksKnown=true,WeaponShoot=null,
            SourceItem=receipt.Weapon.ItemType,SourcePrefix=receipt.Weapon.Prefix,SourceSlot=receipt.Slot,
            SourceDefinition=null,SourceLocalDefinition=definition,LastUseSuccess=frame.LastUseSuccess,
            DeferredSelectionSlot=deferred?selectedSlot:null,RequiresFreshUse=deferred,
            SourceStagePose=null,Reach=null,BuildingPermission=null,ReleaseWitness=null,
            ShootEligible=false,ToolStagePending=false,LastFrameUpdate=currentCombatTick};
        return true;
    }

    private static bool HasPhysicalWeaponAmmo(in RuntimePlayerInventoryItem selected,
        ReadOnlySpan<RuntimePlayerInventoryItem> items)
    {
        if(!VanillaRemoteRangedItemCheck1458.TryGetAmmoFamily(selected.ItemType,out var family)) return true;
        for(int i=0;i<Math.Min(58,items.Length);i++)if(items[i].Stack>0 &&
            VanillaProjectileWeaponCombatCatalog.TryGetAmmo(family,items[i].ItemType,out _))return true;
        return false;
    }

    private bool TryPrepareOwnedWeaponClockShoot(RuntimePlayerProjectileUseCapture capture,
        RuntimePlayerMagicPayment1458? payment, out RuntimePlayerWeaponClockPreparation1458? prepared)
    {
        prepared=null;
        if(toolWorld is null || playerUpdateWorld is not { } world || capture.ItemPhase is not {ToolUse:{ } before} phase ||
            !inventory.TryGet(capture.Connection,capture.Player.SelectedItem,out var selected) ||
            !VanillaLocalWeaponClockCatalog1458.TryResolve(selected.ItemType,selected.Prefix,
                world.WindowsItemPrefixArithmetic,out var definition))return true;
        if(IsOrderedAmmoWeapon(selected) && (before.WeaponShootHistory is {Uses.Length:>0} ||
            before.LocalClocksKnown && capture.Member?.ItemUseCaller==RuntimeItemUseCaller1458.LocalOwnerProjection &&
            (capture.Player.ControlFlags&32)!=0))return false;
        if(before.WeaponShoot is {Committed:true})return false;
        if(!before.LocalClocksKnown && before.WeaponShoot is null)return true;
        if(before.DeferredSelectionSlot is not null)return false;
        bool slow=payment?.SlowMagicUse ?? phase.SlowMagicUse;
        float multiplier=VanillaRemoteMeleeItemCatalog1458.TryGet(selected.ItemType,out _)?
            phase.DerivedCombat?.MeleeAnimationMultiplier ?? 1f:slow?2.5f:1f;
        RuntimePlayerToolUse1458 after;
        if(before.WeaponShoot is {Frame:{ } frame,Committed:false} offered)
        {
            if(offered.Player!=capture.Connection.Player || offered.Slot!=capture.Player.SelectedItem ||
                offered.Weapon!=selected || offered.Epoch!=before.UseEpoch ||
                !VanillaLocalItemUse1458.TryApplyShootTime(definition,frame.State,slow?2.5f:1f,
                    world.WindowsItemPrefixArithmetic,out var state) ||
                !VanillaLocalItemUse1458.TryEnterOwnerStage(definition,state,frame.LocalToolStage,out state))return false;
            after=before with {Clocks=state,LocalClocksKnown=true,WeaponShoot=null,RequiresFreshUse=false};
        }
        else
        {
            // All fresh local item starts share the source contact/cursed fact.
            // The already-owned offered frame above precedes this gate.
            if(!CanBeginLocalMagicUse(capture.Connection))return false;
            if((capture.Player.ControlFlags&32)==0)
            {
                // Existing source-qualified ranged Shoot components can arrive
                // after release without an owned local ItemCheck start. They
                // keep their strict paid birth, but cannot initialize timers.
                after=before with {LocalClocksKnown=false,WeaponShoot=null,RequiresFreshUse=true};
                prepared=new(before,after,capture.Member is { } released &&
                    released.RemotePhaseSnapshot==capture.Player && released.RemotePhaseItem==capture.ItemPhase &&
                    released.RemotePhaseInputRevision==capture.InputRevision);
                return true;
            }
            if(before.UseEpoch==ulong.MaxValue)return false;
            var localBefore=before.Clocks;
            bool changed=!before.SelectionInitialized || before.SourceSlot!=capture.Player.SelectedItem ||
                before.SourceItem!=selected.ItemType || before.SourcePrefix!=selected.Prefix;
            if(changed)
            {
                if(localBefore.Selected.Animation!=0 || localBefore.Selected.ItemTime!=0 || localBefore.ReuseDelay!=0)return false;
                localBefore=localBefore with {Selected=localBefore.Selected with {ItemTime=0,ItemTimeMax=0,
                    ReleaseUseItem=true},Channel=false,PoundRelease=false};
            }
            int? noItems=transferProfiles.CountActiveBuffs(capture.Connection,new(23));
            if(noItems is null)return false;
            var facts=new ToolFrameFacts1458(true,true,changed || before.LastUseSuccess,noItems!=0,false,false,false,multiplier)
                {WindowsArithmetic=world.WindowsItemPrefixArithmetic};
            if(!VanillaLocalItemUse1458.TryAdvanceFrame(definition,localBefore,facts,1,out var predictedFrame) ||
                !predictedFrame.ShootBoundary ||
                !VanillaLocalItemUse1458.TryApplyShootTime(definition,predictedFrame.State,slow?2.5f:1f,
                    world.WindowsItemPrefixArithmetic,out var paidClocks) ||
                !VanillaLocalItemUse1458.TryEnterOwnerStage(definition,paidClocks,predictedFrame.LocalToolStage,out paidClocks))return false;
            ulong epoch=predictedFrame.BeganActualUse?before.UseEpoch+1:before.UseEpoch;
            var receipt=new RuntimePlayerWeaponShoot1458(capture.Connection.Player,capture.Player.SelectedItem,
                selected,epoch,localBefore,null,true,slow,multiplier,true){PaidClocks=paidClocks};
            after=before with {WeaponShoot=receipt,Clocks=paidClocks,UseEpoch=epoch,
                SelectionInitialized=true,SourceSlot=capture.Player.SelectedItem,SourceItem=selected.ItemType,
                SourcePrefix=selected.Prefix,SourceLocalDefinition=definition,SourceDefinition=null,
                LastUseSuccess=predictedFrame.LastUseSuccess,RequiresFreshUse=false};
        }
        prepared=new(before,after,capture.Member is { } member &&
            member.RemotePhaseSnapshot==capture.Player && member.RemotePhaseItem==capture.ItemPhase &&
            member.RemotePhaseInputRevision==capture.InputRevision);return true;
    }

    private bool AdoptOwnedWeaponClockShoot(RuntimePlayerProjectileUseCapture capture,
        RuntimePlayerWeaponClockPreparation1458? prepared, bool revisionAlreadyAdvanced = false)
    {
        if(prepared is not { } token)return true;
        if(!membership.TryGet(capture.Connection,out var member) || member.ItemPhase is not { } phase ||
            phase.ToolUse!=token.Before || !revisionAlreadyAdvanced && !member.TryAdvanceRevision())return false;
        var after=phase with {ToolUse=token.After};member.ItemPhase=after;
        if(token.PreserveRemote)member.MarkRemotePhaseSnapshot();
        return true;
    }
}
