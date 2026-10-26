using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

// This is the delayed-publication portion of the existing WeaponShoot receipt.
// It owns neither a second inventory balance nor a second random stream. Costs
// and entropy are adopted by the complete player-phase journal; these immutable
// facts only qualify later wire reports. A full window refuses a new local use
// before its Shoot tail, without refunding or replaying accepted predecessors.
internal sealed record RuntimePlayerPaidAmmoShoot1458(ulong Serial, ConnectionHandle Connection,
    byte SelectedSlot, RuntimePlayerInventoryItem Weapon, short AmmoSlot,
    RuntimePlayerInventoryItem AmmoBefore, RuntimePlayerInventoryItem AmmoAfter,
    short FundedBefore, VanillaPlayerCombatSnapshot Combat, bool AmmoBox, bool AmmoPotion,
    PlayerStateSnapshot LaunchPose, VanillaUnifiedRandom1458 RandomBefore,
    VanillaUnifiedRandom1458 RandomAfter, int ShotCount, ulong UseEpoch, long SourceTick)
{
    internal TerrariaProjectileUpdateState[] Reports { get; init; } = [];
    internal bool Born { get; init; }
    internal bool Retired { get; init; }
}

internal sealed record RuntimePlayerAmmoShootHistory1458(
    RuntimePlayerPaidAmmoShoot1458[] Uses, TerrariaProjectileUpdateState[] PendingReports,
    PlayerEquipmentCommitRequest[] PendingInventoryReports, ulong NextSerial)
{
    internal const int MaximumUses = 32;
    internal const int MaximumPendingReports = VanillaBulletWeaponLaunch1458.MaximumShotCount;
    internal RuntimePlayerInventoryStore.ShopJournalCapture? InventoryWitness { get; init; }
    internal PlayerStateSnapshot? PendingLaunchPose { get; init; }
    internal RuntimePlayerProjectileUseCapture? PendingCapture { get; init; }
    internal static RuntimePlayerAmmoShootHistory1458 Empty { get; } = new([], [], [], 1);
}

internal sealed partial class PlayerAuthority
{
    private static bool IsOrderedAmmoWeapon(in RuntimePlayerInventoryItem selected) =>
        VanillaOrdinaryBowLaunch1458.Supports(selected.ItemType) ||
        VanillaBulletWeaponLaunch1458.Supports(selected.ItemType);

    // Count all source-compatible physical occurrences, including unknown launch
    // identities and duplicated coin slots. Resolve/fence only the chosen item;
    // never skip it for a later supported or better-funded stack.
    private static int FindOrderedAmmo(in VanillaProjectileWeaponCombatDefinition weapon,
        ReadOnlySpan<RuntimePlayerInventoryItem> items, AmmoCyclingMode1458 mode,
        in AmmoCyclingState1458 cycling, out bool hasAmmo, out RuntimePlayerInventoryItem selected,
        out VanillaProjectileAmmoCombatDefinition definition)
    {
        selected = default; definition = default; hasAmmo = false;
        if (items.Length < 58) return -2;
        int family = weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow ? 40 :
            weapon.AmmoFamily == VanillaProjectileAmmoFamily.Bullet ? 97 : 0;
        if (family == 0) return -2;
        Span<AmmoCyclingSlot1458> image = stackalloc AmmoCyclingSlot1458[58];
        for (int slot = 0; slot < image.Length; slot++)
        {
            var item = items[slot];
            image[slot] = new(VanillaProjectileWeaponCombatCatalog.IsAmmoType(weapon.AmmoFamily, item.ItemType) ? family : 0,
                item.Stack);
        }
        if (!VanillaAmmoCycling1458.TryHasAmmo(family, image, out hasAmmo) ||
            !VanillaAmmoCycling1458.TrySelect(weapon.Type.Value, family, mode, in cycling, image, out int index)) return -2;
        if (index < 0) return -1;
        var candidate = items[index];
        if (!candidate.IsCanonical || candidate.Prefix.Value != 0 ||
            !VanillaProjectileWeaponCombatCatalog.TryGetAmmo(weapon.AmmoFamily, candidate.ItemType, out definition)) return -2;
        selected = candidate; return index;
    }

    private bool TryTrackOrderedAmmoClock(RuntimePlayerToolUse1458 before, byte slot,
        in RuntimePlayerInventoryItem selected, in PlayerSelectedConsumableFacts1458 facts,
        in PlayerSelectedConsumableState1458 remoteBefore, in PlayerSelectedConsumableTransition1458 remoteUse,
        in RuntimePlayerItemPhase1458 phase, RemoteItemPhasePlan1458 plan,
        out RuntimePlayerToolUse1458 after)
    {
        after=before;
        if(toolWorld is null || !IsOrderedAmmoWeapon(selected) ||
            plan.BeforeItemUseCaller!=RuntimeItemUseCaller1458.LocalOwnerProjection)return false;
        if(playerUpdateWorld is not { } world || !before.LocalClocksKnown ||
            !VanillaLocalWeaponClockCatalog1458.TryResolve(selected.ItemType,selected.Prefix,
                world.WindowsItemPrefixArithmetic,out var definition) ||
            !VanillaProjectileWeaponCombatCatalog.TryGetWeapon(selected.ItemType,out var weapon) ||
            phase.DerivedCombat is not { } combat || plan.SourceRandom is not { } random)
        {after=before with {LocalClocksKnown=false,RequiresFreshUse=true};return true;}
        var history=before.WeaponShootHistory??RuntimePlayerAmmoShootHistory1458.Empty;
        if(history.InventoryWitness is null && plan.AmmoInventoryBefore is { } initialImage)
            history=history with {InventoryWitness=initialImage};
        if(history.PendingCapture is { } proposed && !IsOrderedAmmoCaptureCurrent(proposed))
            history=history with {PendingReports=[],PendingInventoryReports=[],PendingLaunchPose=null,PendingCapture=null};
        if(history.InventoryWitness is {IsCurrent:false})
        {
            // A stale inventory image cannot qualify another source payment.
            // Immutable paid predecessors retain their separate delivery facts.
            after=before with {LocalClocksKnown=false,RequiresFreshUse=true};
            return true;
        }
        int delta=remoteBefore.RevolverCritBonus-remoteUse.State.RevolverCritBonus;
        int roll=delta==2?0:delta==0?1:-1;
        var clocks=before.Clocks;
        bool changed=!before.SelectionInitialized || before.SourceSlot!=slot ||
            before.SourceItem!=selected.ItemType || before.SourcePrefix!=selected.Prefix;
        bool deferred=changed && (clocks.Selected.Animation!=0 || clocks.Selected.ItemTime!=0 || clocks.ReuseDelay!=0);
        if(deferred)
        {
            // The existing common owner handles the previous definition until
            // its clocks finish; this path must not borrow the new weapon.
            return false;
        }
        if(changed)clocks=clocks with {Selected=clocks.Selected with {ItemTime=0,ItemTimeMax=0,
            ReleaseUseItem=facts.ControlUseItem || clocks.Selected.ReleaseUseItem},Channel=false,PoundRelease=false};
        if (combat.AmmoCyclerDamage && phase.AmmoCycling is null)
        {after=before with {LocalClocksKnown=false,RequiresFreshUse=true};return true;}
        var cycling = phase.AmmoCycling ?? default;
        int ammoSlot=FindOrderedAmmo(in weapon,plan.InventoryBefore,combat.AmmoCyclingMode,in cycling,
            out bool hasAmmo,out var ammoItem,out var ammo);
        if(ammoSlot==-2)
        {after=before with {LocalClocksKnown=false,RequiresFreshUse=true};return true;}
        var sourceFacts=new ToolFrameFacts1458(true,facts.ControlUseItem,before.LastUseSuccess,
            facts.Cursed,facts.CrowdControlled,facts.SelectionBuffered,false,1f)
            {SourceUseAllowed=hasAmmo,WindowsArithmetic=world.WindowsItemPrefixArithmetic};
        if(roll<0 || !VanillaLocalItemUse1458.TryAdvanceFrame(definition,clocks,sourceFacts,roll,out var frame) ||
            frame.BeganActualUse && before.UseEpoch==ulong.MaxValue)
        {after=before with {LocalClocksKnown=false,RequiresFreshUse=true};return true;}
        ulong epoch=frame.BeganActualUse?before.UseEpoch+1:before.UseEpoch;
        var state=frame.State;
        if(frame.ShootBoundary && ammoSlot>=0)
        {
            if(history.Uses.Length>=RuntimePlayerAmmoShootHistory1458.MaximumUses && (history.Uses[0].Born || history.Uses[0].Retired))
                history=history with {Uses=history.Uses[1..]};
            if(history.Uses.Length>=RuntimePlayerAmmoShootHistory1458.MaximumUses || history.NextSerial==ulong.MaxValue)
            {
                // Bounded delay exhaustion is explicit. Do not expose an unpaid
                // source frame or overwrite a paid, unreported predecessor.
                after=before with {LocalClocksKnown=false,RequiresFreshUse=true};return true;
            }
            bool box=plan.BeforeBuffs.GetLastActiveDuration(new(93))>0;
            bool potion=plan.BeforeBuffs.GetLastActiveDuration(new(112))>0;
            if(!inventory.TryGetFundedQuantity(plan.Member.Connection,ammoSlot,out short funding))
            {after=before with {LocalClocksKnown=false,RequiresFreshUse=true};return true;}
            // Selection precedes conservation. Random mode spends its one source
            // offer here even if conservation later keeps the physical unit.
            if (!VanillaAmmoCycling1458.TryAdvanceSelectedAmmo(definition.UseAnimation, false,
                combat.AmmoCyclingMode, in cycling, random.Next, out var afterCycling))
                throw new InvalidOperationException("Qualified cycling source state lost its transition.");
            if (phase.AmmoCycling is not null) plan.AmmoCyclingAfterShoot = afterCycling;
            var randomBefore=random.Clone();
            bool conserved=ProjectileAuthority.PrepareAmmoConservation(in weapon,in ammo,in combat,random,box,potion);
            int count=1;
            if(VanillaBulletWeaponLaunch1458.Supports(selected.ItemType) &&
                !VanillaBulletWeaponLaunch1458.TryAdvanceSourceRandom(selected.ItemType,random.Next,random.NextDouble,out count))
                throw new InvalidOperationException("Qualified ammo weapon lost its source launch kernel.");
            var remaining=conserved?ammoItem:ammoItem.Stack==1?default:ammoItem with {Stack=(short)(ammoItem.Stack-1)};
            var pose=history.PendingLaunchPose??plan.Before with {PositionX=plan.X,PositionY=plan.Y,VelocityX=plan.Vx,VelocityY=plan.Vy};
            var paid=new RuntimePlayerPaidAmmoShoot1458(history.NextSerial,plan.Member.Connection,slot,selected,
                (short)ammoSlot,ammoItem,remaining,funding,combat,box,potion,pose,
                randomBefore,random.Clone(),count,epoch,currentCombatTick)
                {Reports=history.PendingReports.Length<=count?history.PendingReports:[]};
            history=history with {Uses=[..history.Uses,paid],NextSerial=history.NextSerial+1,
                PendingReports=[],PendingLaunchPose=null,PendingCapture=null};
            if(remaining!=ammoItem)
                plan.InventoryMutations.Add(new(plan.Member.Connection,(short)ammoSlot,ammoItem,remaining)
                    {SourceAmmoConsumption=true});
            if(!VanillaLocalItemUse1458.TryApplyShootTime(definition,state,1f,world.WindowsItemPrefixArithmetic,out state))
                throw new InvalidOperationException("Qualified ammo source frame lost its clock transition.");
        }
        if(!frame.ShootBoundary || ammoSlot<0)
            history=history with {PendingReports=[],PendingLaunchPose=null,PendingCapture=null,PendingInventoryReports=[]};
        // Source no-ammo early return does not reset ShootTime or consume launch
        // entropy; its normal owner/tool stage still follows the common frame.
        if(!VanillaLocalItemUse1458.TryEnterOwnerStage(definition,state,frame.LocalToolStage,out state))
            throw new InvalidOperationException("Qualified ammo source frame lost its owner stage.");
        after=before with {Clocks=state,LocalClocksKnown=true,SelectionInitialized=true,
            SourceItem=selected.ItemType,SourcePrefix=selected.Prefix,SourceSlot=slot,
            SourceDefinition=null,SourceLocalDefinition=definition,DeferredSelectionSlot=null,
            LastUseSuccess=frame.LastUseSuccess,RequiresFreshUse=false,WeaponShoot=null,
            WeaponShootHistory=history,UseEpoch=epoch,LastFrameUpdate=currentCombatTick,
            SourceStagePose=null,Reach=null,BuildingPermission=null,ReleaseWitness=null,
            ShootEligible=false,ToolStagePending=false};
        return true;
    }

    private static RuntimePlayerAmmoShootHistory1458 RefreshUnpaidAmmoInputHistory(
        RuntimePlayerAmmoShootHistory1458 history,RuntimePlayerInventoryStore.ShopJournalCapture initialImage) =>
        history with {PendingReports=[],PendingInventoryReports=[],PendingLaunchPose=null,PendingCapture=null,
            InventoryWitness=initialImage};

    private static RuntimePlayerItemPhase1458? RetainPaidAmmoDeliveryAfterPhaseRetirement(
        RuntimePlayerItemPhase1458? before)
    {
        if(before is not {ToolUse:{WeaponShootHistory:{Uses.Length:>0} history}} phase)return null;
        // The source phase is permanently retired, but its already-paid shoots
        // are immutable facts. Keep only their existing bounded delivery owner;
        // no stale combat, tool, UI, payment or constructor-clock proof survives.
        return new RuntimePlayerItemPhase1458(phase.BaseManaMaximum,phase.Mana,phase.Selected,phase.ManaHeat)
        {
            ToolUse=RuntimePlayerToolUse1458.Constructor with
            {
                LocalClocksKnown=false,RequiresFreshUse=true,
                WeaponShootHistory=history with {PendingReports=[],PendingInventoryReports=[],
                    PendingCapture=null,PendingLaunchPose=null,InventoryWitness=null}
            }
        };
    }

    private void RebindAcceptedAmmoHistory(RuntimePlayerMember member)
    {
        if(member.ItemPhase is not {ToolUse:{WeaponShootHistory:{ } history} tools} phase)return;
        // Called only in the callback-free whole-census tail. The old witness
        // passed the final guard before any owner was adopted; this captures
        // only that accepted journal afterimage, never an observer resnapshot.
        if(history.InventoryWitness is {IsCurrent:true})return;
        if(!inventory.TryCaptureShopJournal(member.Connection,out var witness) || witness is null)
            throw new InvalidOperationException("Accepted ammo phase lost its inventory afterimage.");
        member.ItemPhase=phase with {ToolUse=tools with {WeaponShootHistory=history with {InventoryWitness=witness}}};
        member.MarkRemotePhaseSnapshot();
    }

    internal ToolReportObservationResult1458 ObserveOrderedAmmoShoot(ConnectionHandle connection,
        in TerrariaProjectileUpdateState packet,bool localReportIntent=false)
    {
        if(!HasRemotePlayerPhaseBinding || toolWorld is null || !membership.TryGet(connection,out var member) ||
            member.ItemPhase is not {ToolUse:{ } tools} phase)return ToolReportObservationResult1458.NotApplicable;
        var history=tools.WeaponShootHistory;
        bool paidOrPending=history is not null && (history.PendingReports.Length!=0 ||
            history.Uses.Any(static u=>!u.Born && !u.Retired));
        // Duplicate full keys remain owned after the actor has retired. They
        // cannot be interpreted as the first child of a later paid receipt.
        if(history is not null)
            foreach(var use in history.Uses)
                foreach(var report in use.Reports)
                    if(report.Key==packet.Key)
                        return report==packet?ToolReportObservationResult1458.Retained:ToolReportObservationResult1458.Rejected;
        bool coldIntent=localReportIntent && member.ItemUseCaller==RuntimeItemUseCaller1458.Unknown &&
            playerUpdateWorld is not null && tools.LocalClocksKnown && tools.UseEpoch==0 &&
            tools.LastFrameUpdate==long.MinValue && tools.Clocks.Selected.Animation==0 &&
            tools.Clocks.Selected.ItemTime==0 && tools.Clocks.ReuseDelay==0;
        if(!paidOrPending && !(history?.Uses.Length>0) && !coldIntent && (member.ItemUseCaller!=RuntimeItemUseCaller1458.LocalOwnerProjection ||
            playerUpdateWorld is null || !tools.LocalClocksKnown || (member.ControlFlags&32)==0))
            return ToolReportObservationResult1458.NotApplicable;
        bool currentWeapon=inventory.TryGet(connection,member.SelectedItem,out var weapon) && IsOrderedAmmoWeapon(weapon);
        if(!currentWeapon && !(history?.Uses.Any(static u=>!u.Born && !u.Retired && u.Reports.Length<u.ShotCount)??false))
            return ToolReportObservationResult1458.NotApplicable;
        history??=RuntimePlayerAmmoShootHistory1458.Empty;
        var uses=history.Uses;
        int pending=Array.FindIndex(uses,static u=>!u.Born && !u.Retired && u.Reports.Length<u.ShotCount);
        if(pending>=0)
        {
            var use=uses[pending];
            if(use.Connection!=connection)
                return ToolReportObservationResult1458.Rejected;
            var next=uses.ToArray(); next[pending]=use with {Reports=[..use.Reports,packet]};
            history=history with {Uses=next};
        }
        else
        {
            if(history.InventoryWitness is {IsCurrent:false} || member.IsDead || !coldIntent && member.ItemUseCaller!=RuntimeItemUseCaller1458.LocalOwnerProjection ||
                !currentWeapon || !tools.LocalClocksKnown || tools.DeferredSelectionSlot is not null ||
                !coldIntent && (member.ControlFlags&32)==0 ||
                !CanBeginLocalMagicUse(connection) || history.PendingReports.Length>=RuntimePlayerAmmoShootHistory1458.MaximumPendingReports)
                return ToolReportObservationResult1458.Rejected;
            foreach(var report in history.PendingReports)
                if(report.Key==packet.Key)return report==packet?ToolReportObservationResult1458.Retained:ToolReportObservationResult1458.Rejected;
            var capture=history.PendingCapture;
            if(capture is not null ? !IsOrderedAmmoCaptureCurrent(capture) :
                !TryCaptureProjectileUse(connection,out capture) || capture is null)
                return ToolReportObservationResult1458.Rejected;
            history=history with {PendingReports=[..history.PendingReports,packet],
                PendingCapture=capture,PendingLaunchPose=history.PendingLaunchPose??member.CaptureSnapshot()};
        }
        bool preserve=member.RemotePhaseSnapshot==member.CaptureSnapshot() && member.RemotePhaseItem==phase &&
            member.RemotePhaseInputRevision==member.ProjectileUseInputRevision;
        member.ItemPhase=phase with {ToolUse=tools with {WeaponShootHistory=history}};
        if(preserve)member.MarkRemotePhaseSnapshot();
        return ToolReportObservationResult1458.Retained;
    }

    private bool IsOrderedAmmoCaptureCurrent(RuntimePlayerProjectileUseCapture capture)
    {
        if(capture.ItemPhase is not {ToolUse:{ } before} original ||
            !membership.TryGet(capture.Connection,out var member) ||
            member.ItemPhase is not {ToolUse:{ } currentTools} current)return false;
        return (current with {ToolUse=currentTools with {WeaponShootHistory=before.WeaponShootHistory}})==original &&
            IsCurrentProjectileUse(capture with {ItemPhase=current});
    }

    private RuntimePlayerAmmoShootHistory1458? CaptureOrderedAmmoMovement(RuntimePlayerMember member)
    {
        if(member.ItemPhase?.ToolUse?.WeaponShootHistory is not {PendingCapture:{ } captured} history ||
            captured.InputRevision==ulong.MaxValue || captured.Player.Revision.Value==ulong.MaxValue ||
            !IsOrderedAmmoCaptureCurrent(captured))return null;
        return history;
    }

    private void ObserveOrderedAmmoMovement(RuntimePlayerMember member,RuntimePlayerAmmoShootHistory1458? observed)
    {
        if(observed?.PendingCapture is not { } capture || member.ItemPhase is not {ToolUse:{ } tools} current ||
            !ReferenceEquals(tools.WeaponShootHistory,observed) || capture.ItemPhase is not {ToolUse:{ } originalTools} original)return;
        var now=member.CaptureSnapshot();
        // These calls surround one normalized13 adoption without callbacks.
        // Idempotent source Shoot13 and a late held qualification may keep the
        // report, but movement, selection or resource changes cannot rebase it.
        var expected=capture.Player with {Revision=now.Revision,ControlFlags=now.ControlFlags,MiscFlags2=now.MiscFlags2};
        bool exact=now==expected && (now.ControlFlags&32)!=0 &&
            (now.ControlFlags&~96)==(capture.Player.ControlFlags&~96) &&
            (now.MiscFlags2&~192)==(capture.Player.MiscFlags2&~192) &&
            member.ProjectileUseInputRevision==capture.InputRevision+1 &&
            member.Revision==capture.Player.Revision.Value+1 &&
            (current with {ToolUse=tools with {WeaponShootHistory=originalTools.WeaponShootHistory}})==original;
        var rebound=capture with {Player=now,InputRevision=member.ProjectileUseInputRevision,
            ItemPhase=current,PhysicsPhase=member.PhysicsPhase};
        exact&=IsCurrentProjectileUse(rebound);
        var next=exact?observed with {PendingCapture=rebound with
            {ItemPhase=current with {ToolUse=tools with {WeaponShootHistory=originalTools.WeaponShootHistory}}}}:
            observed with {PendingReports=[],PendingInventoryReports=[],PendingCapture=null,PendingLaunchPose=null};
        member.ItemPhase=current with {ToolUse=tools with {WeaponShootHistory=next}};
    }

    private static bool IsHistoricalAmmoAcknowledgement(RuntimePlayerMember member,short slot,
        RuntimePlayerInventoryItem reported) =>
        member.ItemPhase?.ToolUse?.WeaponShootHistory?.Uses.Any(use=>use.Connection==member.Connection &&
            use.AmmoSlot==slot && use.AmmoAfter==reported)==true;

    private static bool IsAmmoDeliveryPublicationAfterimage(RuntimePlayerItemPhase1458? actual,
        RuntimePlayerItemPhase1458? expected)
    {
        if(actual==expected)return true;
        if(actual is not {ToolUse:{ } actualTools} current ||
            expected is not {ToolUse:{WeaponShootHistory:not null} expectedTools} accepted)return false;
        // A late27 callback may only acknowledge, birth or retire a previously
        // paid delivery. Ignore that participant alone, never resnapshot an
        // observer's changed health, clocks, mana or other phase ownership.
        return (current with {ToolUse=actualTools with
            {WeaponShootHistory=expectedTools.WeaponShootHistory}})==accepted;
    }

    private bool TryObserveOrderedAmmoInventory(PlayerEquipmentRuntimeCommand command, bool unpaidOfferOnly = false)
    {
        var request=command.Request;
        if(!membership.TryGet(command.Connection,out var member) ||
            member.ItemPhase is not {ToolUse:{ } tools} phase ||
            command.Request.SlotId is <0 or >=58 ||
            !RuntimePlayerInventoryItem.TryFromNormalized(in request,out var report))return false;
        var history=tools.WeaponShootHistory??RuntimePlayerAmmoShootHistory1458.Empty;
        bool relevant=false,accepted=false,unpaidOffer=false;
        foreach(var use in history.Uses)
        {
            if(unpaidOfferOnly || use.AmmoSlot!=request.SlotId || use.AmmoAfter!=report)continue;
            relevant=true;
            accepted=true;
        }
        if(!accepted && TryCaptureUnpaidAmmoOffer(member,out short offeredSlot,out var before) &&
            request.SlotId==offeredSlot)
        {
            relevant=true;
            accepted=report==before || report==(before.Stack==1?default:before with {Stack=(short)(before.Stack-1)});
            unpaidOffer=accepted;
        }
        if(!relevant || unpaidOfferOnly && !accepted)return false;
        if(!accepted || unpaidOffer && history.InventoryWitness is {IsCurrent:false})
        {RejectedEquipmentUpdates++;return true;}
        if(unpaidOffer && history.PendingCapture is null)
        {
            if(!TryCaptureProjectileUse(command.Connection,out var captured) || captured is null)
            {RejectedEquipmentUpdates++;return true;}
            history=history with {PendingCapture=captured,PendingLaunchPose=captured.Player};
        }
        // Historical reports acknowledge the immutable accepted cost; they do
        // not write an older stack back over a later canonical debit.
        bool preserve=member.RemotePhaseSnapshot==member.CaptureSnapshot() && member.RemotePhaseItem==phase &&
            member.RemotePhaseInputRevision==member.ProjectileUseInputRevision;
        if(history.PendingInventoryReports.Length<RuntimePlayerAmmoShootHistory1458.MaximumUses &&
            !history.PendingInventoryReports.Contains(request))
            member.ItemPhase=phase with {ToolUse=tools with {WeaponShootHistory=history with
                {PendingInventoryReports=[..history.PendingInventoryReports,request]}}};
        if(preserve)member.MarkRemotePhaseSnapshot();
        AppliedEquipmentUpdates++;
        return true;
    }

    private bool TryCaptureUnpaidAmmoOffer(RuntimePlayerMember member,out short slot,out RuntimePlayerInventoryItem ammoItem)
    {
        slot=-1;ammoItem=default;
        if(member.ItemUseCaller!=RuntimeItemUseCaller1458.LocalOwnerProjection || member.IsDead ||
            toolWorld is null || playerUpdateWorld is not { } world ||
            member.ItemPhase?.ToolUse is not {LocalClocksKnown:true,DeferredSelectionSlot:null} tools ||
            !inventory.TryGet(member.Connection,member.SelectedItem,out var selected) || !IsOrderedAmmoWeapon(selected) ||
            !VanillaLocalWeaponClockCatalog1458.TryResolve(selected.ItemType,selected.Prefix,world.WindowsItemPrefixArithmetic,out var definition) ||
            !VanillaProjectileWeaponCombatCatalog.TryGetWeapon(selected.ItemType,out var weapon) ||
            transferProfiles.CaptureBuffState(member.Connection) is not { } buffs)return false;
        Span<RuntimePlayerInventoryItem> image=stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        if(!inventory.TryCopyInventory(member.Connection,image))return false;
        if (!transferProfiles.TryCapture(member.Connection, out var appearance, out var equipment, out _)) return false;
        bool openVoid = false;
        for (int i = 0; i < 58; i++) openVoid |= VanillaVoidStorageCatalog1458.OpensVoidStorage(image[i].ItemType);
        if (!TryCaptureProvenRemoteEquipment(equipment, appearance, out var combat, out _, openVoid, member.ItemPhase?.AmmoCycling is not null)) return false;
        if (member.ItemPhase?.AmmoCycling is null && combat.AmmoCyclerDamage) return false;
        var sourceCycling = member.ItemPhase?.AmmoCycling is { } known
            ? VanillaAmmoCycling1458.AdvanceLocalUpdate(in known) : default;
        int chosen=FindOrderedAmmo(in weapon,image,combat.AmmoCyclingMode,in sourceCycling,
            out bool hasAmmo,out ammoItem,out _);
        if(chosen<0)return false;
        var clocks=tools.Clocks;
        bool changed=!tools.SelectionInitialized || tools.SourceSlot!=member.SelectedItem ||
            tools.SourceItem!=selected.ItemType || tools.SourcePrefix!=selected.Prefix;
        if(changed)
        {
            if(clocks.Selected.Animation!=0 || clocks.Selected.ItemTime!=0 || clocks.ReuseDelay!=0)return false;
            clocks=clocks with {Selected=clocks.Selected with {ItemTime=0,ItemTimeMax=0,
                ReleaseUseItem=(member.ControlFlags&32)!=0 || clocks.Selected.ReleaseUseItem},Channel=false,PoundRelease=false};
        }
        var facts=new ToolFrameFacts1458(true,(member.ControlFlags&32)!=0,tools.LastUseSuccess,
            buffs.HasNoItemsEffect,buffs.IsItemUseCrowdControlled,false,false,1f)
            {WindowsArithmetic=world.WindowsItemPrefixArithmetic,SourceUseAllowed=hasAmmo};
        // The fixed crit value affects only the accumulator. No source sample
        // or clock is adopted by this target-free qualification query.
        if(!VanillaLocalItemUse1458.TryAdvanceFrame(definition,clocks,facts,1,out var frame) || !frame.ShootBoundary)return false;
        slot=(short)chosen;return true;
    }

    internal RuntimePlayerPaidAmmoShoot1458[] CaptureReadyPaidAmmoShoots()
    {
        var result=new List<RuntimePlayerPaidAmmoShoot1458>();
        foreach(var member in membership.Members.OrderBy(static m=>m.Slot.Value))
            if(member.ItemPhase?.ToolUse?.WeaponShootHistory is { } history)
                foreach(var use in history.Uses)
                    if(!use.Born && !use.Retired && use.Reports.Length==use.ShotCount)result.Add(use);
        return result.ToArray();
    }

    internal bool IsPaidAmmoShootCurrent(RuntimePlayerPaidAmmoShoot1458 use) =>
        // A paid source Shoot precedes its delayed packet27. Source GetData27
        // does not cancel that friendly birth when the same occupation dies;
        // death still cannot reserve a fresh unpaid use above.
        membership.TryGet(use.Connection,out var member) &&
        member.ItemPhase?.ToolUse?.WeaponShootHistory is { } history &&
        history.Uses.Any(candidate=>ReferenceEquals(candidate,use));

    internal bool TryAdoptPaidAmmoBirth(RuntimePlayerPaidAmmoShoot1458 use, bool rejected=false)
    {
        if(!IsPaidAmmoShootCurrent(use) || !membership.TryGet(use.Connection,out var member) ||
            member.ItemPhase is not {ToolUse:{WeaponShootHistory:{ } history} tools} phase)return false;
        var entries=history.Uses.ToArray();
        int index=Array.FindIndex(entries,candidate=>ReferenceEquals(candidate,use));
        entries[index]=use with {Born=!rejected,Retired=rejected};
        bool preserve=member.RemotePhaseSnapshot==member.CaptureSnapshot() && member.RemotePhaseItem==phase &&
            member.RemotePhaseInputRevision==member.ProjectileUseInputRevision;
        // Consume once before any outward callback, with every full key retained.
        member.ItemPhase=phase with {ToolUse=tools with {WeaponShootHistory=history with {Uses=entries}}};
        if(preserve)member.MarkRemotePhaseSnapshot();
        return true;
    }
}
