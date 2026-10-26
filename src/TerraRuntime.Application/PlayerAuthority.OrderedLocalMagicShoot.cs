using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal ToolReportObservationResult1458 ObserveOrderedLocalMagicShoot(ConnectionHandle connection,
        in TerrariaProjectileUpdateState packet)
    {
        // A never-bound strict component has no UpdatePlayers source phase to
        // order. Preserve its existing neutral CheckMana-only producer; no local
        // clock or feedback entitlement is inferred from constructor ToolUse.
        if(!HasRemotePlayerPhaseBinding || toolWorld is null || !membership.TryGet(connection,out var member) ||
            member.ItemPhase is not {ToolUse:{LocalClocksKnown:true} tools} phase ||
            !inventory.TryGet(connection,member.SelectedItem,out var selected))
            return ToolReportObservationResult1458.NotApplicable;
        bool channel=VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeapon(selected.ItemType,out var weapon);
        bool gem=VanillaGemStaff1458.TryGetWeapon(selected.ItemType,out var gemWeapon);
        if(!channel && !gem)return ToolReportObservationResult1458.NotApplicable;
        int projectile=gem?gemWeapon.Projectile.Value:weapon.ProjectileType.Value;
        if(phase.MagicPayment is {EligibleBirth:true,BirthConsumed:false,AwaitingPhase:false})
            return ToolReportObservationResult1458.NotApplicable;
        if(tools.WeaponShoot is {PendingReport:{ } retained} existing)
        {
            if(retained==packet || existing.PendingSecondReport==packet)return ToolReportObservationResult1458.Retained;
            if(!gem || existing.PendingSecondReport is not null || packet.Key==retained.Key ||
                packet.ProjectileType!=projectile || existing.PendingCapture is not { } captured ||
                !IsOrderedLocalMagicCaptureCurrent(captured))return ToolReportObservationResult1458.Rejected;
            bool preserve=member.RemotePhaseSnapshot==member.CaptureSnapshot() && member.RemotePhaseItem==phase &&
                member.RemotePhaseInputRevision==member.ProjectileUseInputRevision;
            member.ItemPhase=phase with {ToolUse=tools with {WeaponShoot=existing with {PendingSecondReport=packet}}};
            if(preserve)member.MarkRemotePhaseSnapshot();
            return ToolReportObservationResult1458.Retained;
        }
        if(!TryPrepareLocalMagicManaStage(member,in phase,out var stage))
            return ToolReportObservationResult1458.Rejected;
        // Scalar-only prepayment can remain an exact projection. Entropy must
        // stay at the ordered UpdatePlayers boundary, including earlier slots.
        if(stage is null || stage.VisualSamples==0 && phase.ManaHeat==1f && (member.ControlFlags&32)!=0)
            return ToolReportObservationResult1458.NotApplicable;
        if(!CanBeginLocalMagicUse(connection) || packet.ProjectileType!=projectile ||
            tools.DeferredSelectionSlot is not null ||
            !TryCaptureProjectileUse(connection,out var capture) || capture is null ||
            !member.TryAdvanceRevision())return ToolReportObservationResult1458.Rejected;
        capture=capture with {Player=member.CaptureSnapshot(),InputRevision=member.ProjectileUseInputRevision};
        var receipt=new RuntimePlayerWeaponShoot1458(connection.Player,member.SelectedItem,selected,
            tools.UseEpoch,tools.Clocks,null,false,false,1f,true)
            {PendingReport=packet,PendingCapture=capture,PendingLaunchPose=capture.Player,PendingGem=gem};
        // The receipt is the sole changed field. Capture its new owned image,
        // preserving the original launch pose and complete profile preimage.
        member.ItemPhase=phase with {ToolUse=tools with {WeaponShoot=receipt}};
        // Self-reference is deliberately absent: compare the stored report
        // against the captured pre-receipt phase through the narrow predicate.
        member.MarkRemotePhaseSnapshot();
        return ToolReportObservationResult1458.Retained;
    }

    private bool IsOrderedLocalMagicCaptureCurrent(RuntimePlayerProjectileUseCapture captured)
    {
        if(captured.ItemPhase is not {ToolUse:{ } old} phase ||
            !membership.TryGet(captured.Connection,out var member) || member.ItemPhase is not {ToolUse:{ } liveTools} current)
            return false;
        // Insertion changes only the same bounded Shoot receipt. All public,
        // resource, input, inventory and profile guards remain those of capture.
        var without=current with {ToolUse=liveTools with {WeaponShoot=old.WeaponShoot}};
        return IsCurrentProjectileUse(captured with {ItemPhase=current}) &&
            without==phase;
    }

    private RuntimePlayerWeaponShoot1458? CaptureOrderedMagicMovement(RuntimePlayerMember member,
        in PlayerMovementCommitRequest request)
    {
        if(member.ItemPhase?.ToolUse?.WeaponShoot is not {PendingReport:not null,PendingCapture:{ } captured} receipt ||
            captured.InputRevision==ulong.MaxValue ||
            captured.Player.Revision.Value==ulong.MaxValue || !IsOrderedLocalMagicCaptureCurrent(captured))return null;
        return receipt;
    }

    private void ObserveOrderedMagicMovement(RuntimePlayerMember member,RuntimePlayerWeaponShoot1458? receipt)
    {
        if(receipt?.PendingCapture is not { } captured || member.ItemPhase is not {ToolUse:{ } tools} phase ||
            !ReferenceEquals(tools.WeaponShoot,receipt))return;
        // These calls surround exactly one normalized13 write, before observers.
        // It qualifies held intent, never a clock/resource or a new launch pose.
        if(captured.ItemPhase is not {ToolUse:{ } old} original)return;
        bool exact=(captured.Player.ControlFlags&32)==0 && (member.ControlFlags&32)!=0 &&
            member.SelectedItem==captured.Player.SelectedItem &&
            member.Revision==captured.Player.Revision.Value+1 &&
            member.ProjectileUseInputRevision==captured.InputRevision+1 &&
            (phase with {ToolUse=tools with {WeaponShoot=old.WeaponShoot}})==original &&
            member.HasMana==captured.Player.HasMana && member.Mana==captured.Player.Mana &&
            member.MaxMana==captured.Player.MaxMana &&
            IsCurrentProjectileUse(captured with {Player=member.CaptureSnapshot(),
                InputRevision=member.ProjectileUseInputRevision,ItemPhase=phase,PhysicsPhase=member.PhysicsPhase});
        if(!exact)
        {
            // A second/changed input closes this one unqualified cohort. It
            // cannot lend its report to another selector or a later held frame.
            if(receipt.PendingManaReport is not null)RejectedManaUpdates++;
            member.ItemPhase=phase with {ToolUse=tools with {WeaponShoot=null}};
            return;
        }
        var rebound=captured with {Player=member.CaptureSnapshot(),InputRevision=member.ProjectileUseInputRevision,
            ItemPhase=phase with {ToolUse=tools with {WeaponShoot=old.WeaponShoot}},PhysicsPhase=member.PhysicsPhase};
        member.ItemPhase=phase with {ToolUse=tools with {WeaponShoot=receipt with {PendingCapture=rebound}}};
    }

    private bool TryObserveOrderedMagicMana(PlayerManaRuntimeCommand command)
    {
        if(!membership.TryGet(command.Connection,out var member) ||
            member.ItemPhase is not {ToolUse:{WeaponShoot:{PendingReport:not null,PendingCapture:{ } capture} receipt} tools} phase)
            return false;
        var report=command.Request;
        if(report.PlayerSlot!=command.Connection.Player.Slot || report.Mana is <0 or >400 ||
            report.MaxMana!=phase.BaseManaMaximum || !IsOrderedLocalMagicCaptureCurrent(capture))
        {RejectedManaUpdates++;return true;}
        if(receipt.PendingManaReport is { } previous)
        {
            if(previous!=report)RejectedManaUpdates++;
            return true; // A duplicate cannot pay, advance or renew anything.
        }
        bool preserve=member.RemotePhaseSnapshot==member.CaptureSnapshot() && member.RemotePhaseItem==phase &&
            member.RemotePhaseInputRevision==member.ProjectileUseInputRevision;
        member.ItemPhase=phase with {ToolUse=tools with {WeaponShoot=receipt with {PendingManaReport=report}}};
        if(preserve)member.MarkRemotePhaseSnapshot();
        return true;
    }

    internal RuntimePlayerOrderedMagicBirth1458[] PrepareOrderedLocalMagicShoots()
    {
        var accepted=new List<RuntimePlayerOrderedMagicBirth1458>();
        foreach(var member in membership.Members.OrderBy(static m=>m.Slot.Value))
        {
            if(member.ItemPhase is not {ToolUse:{WeaponShoot:{PendingReport:{ } report,
                PendingCapture:{ } original} receipt} tools} || (!receipt.PendingPhaseReady && receipt.Frame is null) ||
                tools.LastFrameUpdate!=currentCombatTick || !TryCaptureProjectileUse(member.Connection,out var current) || current is null)continue;
            accepted.Add(new(current,original,receipt,report));
        }
        return accepted.ToArray();
    }

    internal bool TryTakeOrderedLocalMagicShoot(RuntimePlayerOrderedMagicBirth1458 prepared,
        out ConnectionHandle connection,out TerrariaProjectileUpdateState packet,out PlayerStateSnapshot launchPose)
    {
        connection=default;packet=default;launchPose=default;
        var accepted=prepared.Accepted;var original=prepared.Original;
        if(!membership.TryGet(accepted.Connection,out var member) ||
            !ReferenceEquals(accepted.Member,member) || member.ItemPhase is not {ToolUse:{ } tools} phase ||
            !ReferenceEquals(tools.WeaponShoot,prepared.Receipt))return false;
        bool current=IsCurrentProjectileUse(accepted) && ReferenceEquals(original.Member,member) &&
            original.Connection==member.Connection && original.InputRevision==member.ProjectileUseInputRevision &&
            original.Player.SelectedItem==member.SelectedItem && prepared.Receipt.Weapon.ItemType==tools.SourceItem &&
            prepared.Receipt.Weapon.Prefix==tools.SourcePrefix;
        // Consume the exact old receipt before callbacks. A newer receipt or
        // generation was excluded above and is never overwritten on refusal.
        if(!member.TryAdvanceRemotePhaseRevision())return false;
        member.ItemPhase=phase with {ToolUse=tools with {WeaponShoot=prepared.Receipt.PendingGem?null:prepared.Receipt with {PendingReport=null,PendingCapture=null,PendingLaunchPose=null,PendingManaReport=null}}};
        member.MarkRemotePhaseSnapshot();
        if(!current)
        {if(prepared.Receipt.PendingManaReport is not null)RejectedManaUpdates++;return false;}
        if(prepared.Receipt.PendingManaReport is { } manaReport)
        {
            if(phase.MagicPayment is { } payment && payment.Matches(member.Connection.Player,member.SelectedItem,
                prepared.Receipt.Weapon,phase.BaseManaMaximum) && manaReport.Mana==payment.PaidMana &&
                manaReport.MaxMana==phase.BaseManaMaximum)AppliedManaUpdates++;
            else RejectedManaUpdates++;
        }
        connection=member.Connection;packet=prepared.Packet;
        launchPose=prepared.Receipt.PendingLaunchPose??original.Player;return true;
    }
}

internal sealed record RuntimePlayerOrderedMagicBirth1458(RuntimePlayerProjectileUseCapture Accepted,
    RuntimePlayerProjectileUseCapture Original,RuntimePlayerWeaponShoot1458 Receipt,TerrariaProjectileUpdateState Packet);
