using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed record RuntimePlayerProjectileUseCapture(
    ConnectionHandle Connection,
    PlayerStateSnapshot Player,
    ulong InventorySerial,
    bool HasProfile,
    PlayerEquipmentCommitRequest[] Equipment,
    BuffTypeId[]? Buffs)
{
    internal RuntimePlayerMember? Member { get; init; }
    internal ulong InputRevision { get; init; }
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
        return membership.TryGet(player, out var member) &&
            TryCaptureProjectileUse(member.Connection, out capture);
    }

    internal bool TryCaptureProjectileUse(ConnectionHandle connection, out RuntimePlayerProjectileUseCapture? capture)
    {
        capture = null;
        ulong serial = inventory.Serial;
        if (!membership.TryGet(connection, out var member) || member.Revision == ulong.MaxValue ||
            member.ProjectileUseInputRevision == ulong.MaxValue ||
            !inventory.TryIsCurrent(connection, serial)) return false;
        bool profile = transferProfiles.TryCapture(connection, out var appearance, out var equipment, out var buffs);
        capture = new(connection, member.CaptureSnapshot(), serial, profile, equipment, buffs)
        {
            Member = member, InputRevision = member.ProjectileUseInputRevision, Appearance = appearance,
            ItemPhase = member.ItemPhase, PhysicsPhase = member.PhysicsPhase
        };
        return true;
    }

    internal bool IsCurrentProjectileUse(RuntimePlayerProjectileUseCapture capture)
    {
        if (!membership.TryGet(capture.Connection, out var member) || !ReferenceEquals(member, capture.Member) ||
            member.ProjectileUseInputRevision != capture.InputRevision ||
            member.CaptureSnapshot() != capture.Player ||
            member.ItemPhase != capture.ItemPhase ||
            !inventory.TryIsCurrent(capture.Connection, capture.InventorySerial)) return false;
        bool profile = transferProfiles.TryCapture(capture.Connection, out var appearance, out var equipment, out var buffs);
        return profile == capture.HasProfile && appearance == capture.Appearance && equipment.AsSpan().SequenceEqual(capture.Equipment) &&
            (buffs is null) == (capture.Buffs is null) &&
            (buffs is null || buffs.AsSpan().SequenceEqual(capture.Buffs));
    }

    internal bool IsCurrentPlainBowProjectilePose(RuntimePlayerProjectileUseCapture capture) =>
        !capture.Player.HasMount && capture.PhysicsPhase is { GravityDirection: 1f } &&
        membership.TryGet(capture.Connection, out var member) && ReferenceEquals(member, capture.Member) &&
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
        RuntimePlayerInventoryMutation? mutation, int manaCost)
    {
        if (!IsCurrentProjectileUse(capture) || manaCost < 0 ||
            (manaCost > 0 && mutation is not null)) return false;
        if (manaCost > 0) return capture.Player.HasMana && capture.Player.Mana >= manaCost;
        return mutation is not { } value ||
            (VanillaPlayerItemSlotCatalog.IsInventorySlot(value.Slot) && value.Item.IsCanonical);
    }

    internal bool TryCommitProjectileUseUnpublished(RuntimePlayerProjectileUseCapture capture,
        RuntimePlayerInventoryMutation? mutation, int manaCost)
    {
        if (!IsCurrentProjectileUse(capture) || manaCost < 0 ||
            (manaCost > 0 && mutation is not null)) return false;
        if (manaCost > 0)
        {
            if (!membership.TryGet(capture.Connection, out var member) || !member.HasMana ||
                member.Mana < manaCost || !member.TryAdvanceRevision()) return false;
            member.Mana = checked((short)(member.Mana - manaCost));
            if (member.ItemPhase is { } phase)
                member.ItemPhase = phase with { Mana = phase.Mana with { Mana = member.Mana } };
            return true;
        }
        if (mutation is not { } value) return true;
        Span<RuntimePlayerInventoryMutation> mutations = stackalloc RuntimePlayerInventoryMutation[1];
        mutations[0] = value;
        return inventory.TryApplyAtomic(capture.Connection, mutations, capture.InventorySerial);
    }

    internal void PublishProjectileUse(RuntimePlayerProjectileUseCapture capture, RuntimePlayerInventoryMutation? mutation, int manaCost)
    {
        ConnectionHandle connection = capture.Connection;
        if (manaCost > 0)
        {
            var request = new PlayerManaCommitRequest(connection.Player.Slot,
                checked((short)(capture.Player.Mana - manaCost)), capture.Player.MaxMana);
            events?.PlayerManaUpdated(connection, in request);
        }
        else if (mutation is { } value)
        {
            var request = value.Item.ToCommitRequest(connection.Player.Slot, value.Slot);
            events?.PlayerEquipmentUpdated(connection, in request);
        }
    }
}
