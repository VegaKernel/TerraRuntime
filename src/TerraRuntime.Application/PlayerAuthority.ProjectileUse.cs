using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;
internal sealed record RuntimePlayerProjectileUseCapture(ConnectionHandle Connection,
    PlayerStateSnapshot Player,
    ulong InventorySerial,
    bool HasProfile,
    PlayerEquipmentCommitRequest[] Equipment,
    BuffTypeId[]? Buffs)
{
    internal RuntimePlayerMember? Member { get; init; }
    internal ulong InputRevision { get; init; }
    internal bool OwnsManaMaximum { get; init; }
    internal PlayerAppearanceCommitRequest? Appearance { get; init; }
    // Retained clocks and phase retirement can change without changing the public player pose.
    internal RuntimePlayerItemPhase1458? ItemPhase { get; init; }
    // Only the ordinary bow origin lane consumes this pose provenance. Gun pending
    // capture/currentness keeps its existing contract.
    internal RuntimePlayerPhysicsPhase1458? PhysicsPhase { get; init; }
}

internal sealed partial class PlayerAuthority
{
    internal bool TryCaptureProjectileUse(PlayerHandle player, out RuntimePlayerProjectileUseCapture? capture)
    {
        capture = null;
        return membership.TryGet(player, out var member) && TryCaptureProjectileUse(member.Connection, out capture);
    }

    internal bool TryCaptureProjectileUse(ConnectionHandle connection, out RuntimePlayerProjectileUseCapture? capture)
    {
        capture = null;
        ulong serial = inventory.Serial;
        if (!membership.TryGet(connection,
            out var member) ||
            member.Revision == ulong.MaxValue ||
            member.ProjectileUseInputRevision == ulong.MaxValue ||
            !inventory.TryIsCurrent(connection,
            serial))
            return false;
        bool profile = transferProfiles.TryCapture(connection, out var appearance, out var equipment, out var buffs);
        capture = new(connection, member.CaptureSnapshot(), serial, profile, equipment, buffs)
        {
            Member = member,
            InputRevision = member.ProjectileUseInputRevision,
            Appearance = appearance,
            OwnsManaMaximum = member.OwnsManaMaximum,
            ItemPhase = member.ItemPhase,
            PhysicsPhase = member.PhysicsPhase
        };
        return true;
    }

    internal bool IsCurrentProjectileUse(RuntimePlayerProjectileUseCapture capture)
    {
        if (!membership.TryGet(capture.Connection,
            out var member) ||
            !ReferenceEquals(member,
            capture.Member) ||
            member.ProjectileUseInputRevision != capture.InputRevision ||
            member.OwnsManaMaximum != capture.OwnsManaMaximum ||
            member.CaptureSnapshot() != capture.Player ||
            member.ItemPhase != capture.ItemPhase ||
            !inventory.TryIsCurrent(capture.Connection,
            capture.InventorySerial))
            return false;
        bool profile = transferProfiles.TryCapture(capture.Connection, out var appearance, out var equipment, out var buffs);
        return profile == capture.HasProfile &&
            appearance == capture.Appearance &&
            equipment.AsSpan().SequenceEqual(capture.Equipment) &&
            (buffs is null) == (capture.Buffs is null) &&
            (buffs is null ||
            buffs.AsSpan().SequenceEqual(capture.Buffs));
    }

    internal bool IsCurrentPlainBowProjectilePose(RuntimePlayerProjectileUseCapture capture) => !capture.Player.HasMount &&
        capture.PhysicsPhase is { GravityDirection: 1f } &&
        membership.TryGet(capture.Connection,
        out var member) &&
        ReferenceEquals(member,
        capture.Member) &&
        member.PhysicsPhase == capture.PhysicsPhase;
    // Player.ResetEffects/UpdateBuffs: source births own empty buff slots; imported missing slots remain unknown.
    internal bool TryCaptureAmmoConservationContext(ConnectionHandle connection, out bool ammoBox, out bool ammoPotion)
    {
        int? boxes = transferProfiles.CountActiveBuffs(connection, new BuffTypeId(93));
        int? potions = transferProfiles.CountActiveBuffs(connection, new BuffTypeId(112));
        ammoBox = boxes > 0;
        ammoPotion = potions > 0;
        return boxes is not null && potions is not null;
    }

    internal bool CanCommitProjectileUse(RuntimePlayerProjectileUseCapture capture,
        RuntimePlayerInventoryMutation? mutation,
        int manaCost,
        RuntimePlayerMagicPayment1458? magicPayment = null,
        RuntimePlayerMagicUsePreparation? magicUse = null)
    {
        if (!IsCurrentProjectileUse(capture) || manaCost < 0 || (manaCost > 0 && mutation is not null && magicUse is null) ||
            !TryPrepareOwnedWeaponClockShoot(capture, magicPayment, out _))
            return false;
        if (magicUse is { } prepared)
            return mutation is null &&
                magicPayment == prepared.Payment &&
                prepared.Payment.IsValid &&
                prepared.Payment.Refill is { } refill &&
                refill.Connection == capture.Connection &&
                capture.ItemPhase is { } capturedPhase &&
                prepared.Payment.BaseManaMaximum == capturedPhase.BaseManaMaximum &&
                (prepared.Payment.ManaStage is not { } refillStage || IsLocalMagicManaStageCurrent(capture,refillStage)) &&
                prepared.Inventory.IsCurrent &&
                transferProfiles.IsCurrentBuffState(capture.Connection,
                prepared.BeforeBuffs);
        if (magicPayment is { } payment)
            return payment.IsValid &&
                capture.ItemPhase is { } phase &&
                payment.Player == capture.Connection.Player &&
                payment.BaseManaMaximum == phase.BaseManaMaximum &&
                capture.Player.HasMana &&
                (payment.ManaStage is { } manaStage
                    ? IsLocalMagicManaStageCurrent(capture,manaStage) &&
                        manaStage.Regenerated.Mana>=manaCost && payment.PaidMana==manaStage.Regenerated.Mana-manaCost &&
                        manaStage.AfterPayment.Mana==payment.PaidMana
                    : capture.Player.Mana >= manaCost && (manaCost == 0 ||
                        payment.PaidMana == capture.Player.Mana - manaCost));
        if (manaCost > 0)
            return capture.Player.HasMana && capture.Player.Mana >= manaCost;
        return mutation is not { } value || inventory.CanApplyProjectileConsumption(capture.Connection, in value, capture.InventorySerial);
    }

    internal bool TryCommitProjectileUseUnpublished(RuntimePlayerProjectileUseCapture capture,
        RuntimePlayerInventoryMutation? mutation,
        int manaCost,
        RuntimePlayerMagicPayment1458? magicPayment = null,
        RuntimePlayerMagicUsePreparation? magicUse = null)
    {
        if (!IsCurrentProjectileUse(capture) || manaCost < 0 || (manaCost > 0 && mutation is not null && magicUse is null) ||
            !TryPrepareOwnedWeaponClockShoot(capture, magicPayment, out var weaponClock))
            return false;
        if (magicUse is { } prepared)
        {
            if (!CanCommitProjectileUse(capture,
                mutation,
                manaCost,
                magicPayment,
                prepared) ||
                !membership.TryGet(capture.Connection,
                out var restoredMember) ||
                restoredMember.ItemPhase is not { } restoredPhase ||
                restoredMember.Revision == ulong.MaxValue)
                return false;
            // All owners were preflighted before the caller adopted the projectile generation.
            if (!prepared.Inventory.TryAdoptUnpublished() ||
                !transferProfiles.TryAdoptBuffState(capture.Connection,
                prepared.BeforeBuffs,
                prepared.AfterBuffs) ||
                !restoredMember.TryAdvanceRevision())
                throw new InvalidOperationException("Validated QuickMana changed during callback-free adoption.");
            restoredMember.OwnsManaMaximum = true;
            restoredMember.Mana = checked((short)prepared.Payment.PaidMana);
            restoredMember.ItemPhase = restoredPhase with
            {
                Mana = prepared.Payment.ManaStage?.AfterPayment ?? restoredPhase.Mana with
                {
                    Mana = restoredMember.Mana
                },
                Channel = !prepared.Payment.GemStaff,
                OwnedChannelExpectedType = prepared.Payment.GemStaff ? restoredPhase.OwnedChannelExpectedType : ResolveOwnedChannelExpectedType(prepared.Payment),
                SlowMagicUse = prepared.Payment.SlowMagicUse,
                MagicPayment = prepared.Payment
            };
            return AdoptOwnedWeaponClockShoot(capture, weaponClock, revisionAlreadyAdvanced: true);
        }

        if (magicPayment is { } payment)
        {
            if (!CanCommitProjectileUse(capture,
                mutation,
                manaCost,
                payment) ||
                !membership.TryGet(capture.Connection,
                out var paidMember) ||
                paidMember.ItemPhase is not { } phase ||
                !paidMember.TryAdvanceRevision())
                return false;
            paidMember.OwnsManaMaximum = true;
            paidMember.Mana = checked((short)(payment.ManaStage?.AfterPayment.Mana ?? paidMember.Mana-manaCost));
            paidMember.ItemPhase = phase with
            {
                Mana = payment.ManaStage?.AfterPayment ?? phase.Mana with
                {
                    Mana = paidMember.Mana
                },
                Channel = !payment.GemStaff,
                OwnedChannelExpectedType = payment.GemStaff ? phase.OwnedChannelExpectedType : ResolveOwnedChannelExpectedType(payment),
                SlowMagicUse = payment.SlowMagicUse,
                MagicPayment = payment
            };
            return AdoptOwnedWeaponClockShoot(capture, weaponClock, revisionAlreadyAdvanced: true);
        }

        if (manaCost > 0)
        {
            if (!membership.TryGet(capture.Connection, out var member) || !member.HasMana || member.Mana < manaCost || !member.TryAdvanceRevision())
                return false;
            member.Mana = checked((short)(member.Mana - manaCost));
            if (member.ItemPhase is { } phase)
                member.ItemPhase = phase with
                {
                    Mana = phase.Mana with
                    {
                        Mana = member.Mana
                    }
                };
            return AdoptOwnedWeaponClockShoot(capture, weaponClock, revisionAlreadyAdvanced: true);
        }

        if (mutation is not { } value)
            return AdoptOwnedWeaponClockShoot(capture, weaponClock);
        return inventory.TryApplyProjectileConsumption(capture.Connection, in value, capture.InventorySerial) &&
            AdoptOwnedWeaponClockShoot(capture, weaponClock);
    }

    private static int ResolveOwnedChannelExpectedType(RuntimePlayerMagicPayment1458 payment)
    {
        if (!TerraRuntime.Gameplay.Items.VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeapon(payment.Item.ItemType, out var weapon))
            throw new InvalidOperationException("Validated channel payment lost its source weapon.");
        return weapon.ProjectileType.Value;
    }

    internal void PublishProjectileUse(RuntimePlayerProjectileUseCapture capture,
        RuntimePlayerInventoryMutation? mutation,
        int manaCost,
        RuntimePlayerMagicUsePreparation? magicUse = null,
        RuntimePlayerMagicPayment1458? magicPayment = null)
    {
        ConnectionHandle connection = capture.Connection;
        if (magicUse is { } prepared)
        {
            Exception? failure = null;
            void Publish(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                }
            }

            // The complete refill/payment/buff/item state is owned before the first observer.
            if (prepared.Inventory.IsAcceptedItemCurrent(0))
                Publish(() =>
                {
                    var item = prepared.Payment.Refill!.Value;
                    var request = item.After.ToCommitRequest(connection.Player.Slot, item.Slot);
                    events?.PlayerEquipmentUpdated(connection, in request);
                });
            if (membership.TryGet(connection,
                out var member) &&
                member.ItemPhase?.MagicPayment == prepared.Payment &&
                member.Mana == prepared.Payment.PaidMana &&
                member.MaxMana == capture.Player.MaxMana)
                Publish(() =>
                {
                    var request = new PlayerManaCommitRequest(connection.Player.Slot, checked((short)prepared.Payment.PaidMana), capture.Player.MaxMana);
                    events?.PlayerManaUpdated(connection, in request);
                });
            if (transferProfiles.IsCurrentBuffState(connection, prepared.AfterBuffs))
                Publish(() =>
                {
                    var request = new PlayerBuffTypesCommitRequest(connection.Player.Slot, prepared.AfterBuffs.CaptureTypes());
                    events?.PlayerBuffTypesUpdated(connection, in request);
                });
            if (failure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return;
        }

        if (manaCost > 0)
        {
            int after=magicPayment?.PaidMana ?? capture.Player.Mana-manaCost;
            if(after!=capture.Player.Mana && membership.TryGet(connection,out var member) &&
                member.Mana==after && member.MaxMana==capture.Player.MaxMana &&
                (magicPayment is null || member.ItemPhase?.MagicPayment==magicPayment))
            {
                var request = new PlayerManaCommitRequest(connection.Player.Slot, checked((short)after), capture.Player.MaxMana);
                events?.PlayerManaUpdated(connection, in request);
            }
        }
        else if (mutation is { } value)
        {
            var request = value.Item.ToCommitRequest(connection.Player.Slot, value.Slot);
            events?.PlayerEquipmentUpdated(connection, in request);
        }
    }
}
