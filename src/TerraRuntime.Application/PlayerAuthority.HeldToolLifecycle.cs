using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed class HeldToolOwnerPreparation1458
{
    private readonly PlayerAuthority owner;
    internal RuntimePlayerMember Member { get; }
    internal PlayerHandle Player { get; }
    internal PlayerStateSnapshot Before { get; }
    internal RuntimePlayerItemPhase1458? Phase { get; }
    internal RuntimePlayerPhysicsPhase1458? Physics { get; }
    internal bool Inactive { get; }
    internal RuntimePlayerUpdateWorld1458 World { get; }
    internal PlayerBuffState? Buffs { get; }
    internal int SelectedPick { get; }
    internal bool DontHurtNature { get; }
    internal RuntimePlayerInventoryItem Selected { get; }
    internal ulong InventorySerial { get; }
    private int? direction;
    private float rotation;
    private bool adopted;

    internal HeldToolOwnerPreparation1458(PlayerAuthority owner, RuntimePlayerMember member,
        PlayerHandle player, bool inactive, PlayerBuffState? buffs, int selectedPick,
        RuntimePlayerInventoryItem selected, ulong inventorySerial, RuntimePlayerUpdateWorld1458 world, bool dontHurtNature)
    {
        this.owner = owner;
        Member = member;
        Player = player;
        Before = member.CaptureSnapshot();
        Phase = member.ItemPhase;
        Physics = member.PhysicsPhase;
        Inactive = inactive;
        Buffs = buffs;
        SelectedPick = selectedPick;
        Selected = selected;
        InventorySerial = inventorySerial;
        World = world;
        DontHurtNature = dontHurtNature;
    }

    internal bool IsCurrent => !adopted && owner.IsCurrentHeldToolOwner(this);
    internal bool HasCurrentPhase => !Inactive &&
        Member.RemotePhaseSnapshot == Before && Member.RemotePhaseItem == Phase &&
        Member.RemotePhaseInputRevision == Member.ProjectileUseInputRevision;
    internal bool NoItems => Buffs?.HasNoItemsEffect == true;
    // These are source UpdateBuffs writers of frozen/webbed/stoned. They may
    // cancel the exact retained tool; they grant no new use or combat authority.
    internal bool CrowdControlled => Buffs?.IsItemUseCrowdControlled == true;

    internal bool TryStageDummyOwner(int sourceDirection, float sourceRotation)
    {
        if (!IsCurrent || direction.HasValue || sourceDirection is not (-1 or 1) ||
            !float.IsFinite(sourceRotation)) return false;
        direction = sourceDirection;
        rotation = sourceRotation;
        return true;
    }

    internal bool TryAdoptUnpublished()
    {
        if (!IsCurrent || direction is not { } sourceDirection ||
            !owner.TryAdoptHeldToolOwnerUnpublished(this, sourceDirection, rotation)) return false;
        adopted = true;
        return true;
    }
}

internal sealed partial class PlayerAuthority
{

    internal bool TryPrepareHeldToolLifecycle(RuntimePlayerMember boundMember, PlayerHandle player,
        int retainedSelectedPick, out HeldToolOwnerPreparation1458? preparation)
    {
        preparation = null;
        if (playerUpdateWorld is not { IsValid: true } world ||
            boundMember.Connection.Player != player || boundMember.Revision == ulong.MaxValue ||
            !float.IsFinite(boundMember.PositionX) || !float.IsFinite(boundMember.PositionY) ||
            retainedSelectedPick is < 0 or > 1000) return false;
        bool inactive = !membership.TryGet(player, out var current);
        if (!inactive && !ReferenceEquals(current, boundMember)) return false;
        PlayerBuffState? buffs = null;
        int pick = retainedSelectedPick;
        RuntimePlayerInventoryItem selected = default;
        ulong inventorySerial = inventory.Serial;
        bool dontHurtNature = false;
        if (!inactive)
        {
            buffs = transferProfiles.CaptureBuffState(boundMember.Connection);
            if (buffs is null || !inventory.TryGet(boundMember.Connection, boundMember.SelectedItem,
                    out selected) || !selected.IsCanonical || selected.ItemType.Value >= 6196)
                return false;
            // The complete SetDefaults census proves that every positive-pick
            // retained identity belongs to this catalog; other known items have0.
            pick = VanillaToolCatalog1458.TryGet(selected.ItemType, out var tool) ? tool.Pick : 0;
            bool openVoid = false;
            for (short slot = 0; slot < 58; slot++)
            {
                if (!inventory.TryGet(boundMember.Connection, slot, out var stored) || !stored.IsCanonical) return false;
                openVoid |= VanillaVoidStorageCatalog1458.OpensVoidStorage(stored.ItemType);
                dontHurtNature |= stored.ItemType.Value is 5309 or 5323;
            }
            if (openVoid)
                // Inventory owner indexes all219 physical lanes: bank4 is179..218.
                // Packet5's700..739 projection belongs to the wire slot catalog.
                for (int slot = 179; slot < 219; slot++)
                {
                    if (!inventory.TryGet(boundMember.Connection, slot, out var stored) || !stored.IsCanonical) return false;
                    if (stored.ItemType.Value is 5309 or 5323 &&
                        VanillaVoidStorageCatalog1458.TryGet(stored.ItemType, out var role) && role.WorksInVoidBag)
                        dontHurtNature = true;
                }
        }
        preparation = new(this, boundMember, player, inactive, buffs, pick, selected, inventorySerial, world, dontHurtNature);
        return preparation.IsCurrent;
    }

    internal bool IsCurrentHeldToolOwner(HeldToolOwnerPreparation1458 capture)
    {
        bool present = membership.TryGet(capture.Player, out var current);
        return playerUpdateWorld == capture.World &&
            capture.Member.Connection.Player == capture.Player &&
            (capture.Inactive ? !present : present && ReferenceEquals(current, capture.Member)) &&
            capture.Member.Revision < ulong.MaxValue && capture.Member.CaptureSnapshot() == capture.Before &&
            capture.Member.ItemPhase == capture.Phase && capture.Member.PhysicsPhase == capture.Physics &&
            (capture.Inactive || inventory.TryIsCurrent(capture.Member.Connection, capture.InventorySerial) &&
                capture.Buffs is { } buffs &&
                transferProfiles.IsCurrentBuffState(capture.Member.Connection, buffs));
    }

    internal bool TryAdoptHeldToolOwnerUnpublished(HeldToolOwnerPreparation1458 capture,
        int direction, float rotation)
    {
        if (!capture.IsCurrent || direction is not (-1 or 1) || !float.IsFinite(rotation) ||
            !capture.Member.TryAdvanceRemotePhaseRevision()) return false;
        var member = capture.Member;
        if (capture.Phase is { } phase)
        {
            var next = phase with
            {
                Selected = phase.Selected with { Animation = 2, ItemTime = 2, ItemTimeMax = 3 }
            };
            if (phase.ToolUse is { } tools)
                next = next with { ToolUse = tools with { Clocks = tools.Clocks with
                    { Selected = tools.Clocks.Selected with { Animation = 2, ItemTime = 2, ItemTimeMax = 3 } } } };
            member.ItemPhase = next;
        }
        // A retired/unknown item phase is not reconstructed from a constructor.
        // Its public animation/facing still receives the terminal source writer.
        member.ItemAnimation = 2;
        member.ControlFlags = (byte)((member.ControlFlags & ~64) | (direction > 0 ? 64 : 0));
        member.ItemRotation = rotation;
        member.MarkRemotePhaseSnapshot();
        return true;
    }
}
