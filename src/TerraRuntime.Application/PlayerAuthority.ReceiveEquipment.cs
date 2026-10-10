using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private VanillaUnifiedRandom1458? receiveEquipmentRandom;
    private bool receiveWindowsArithmetic;

    // Terraria 1.4.5.8 MessageBuffer.GetData5 calls Item.Prefix on Main.rand.
    // Composition supplies its existing shared gameplay cursor and explicit arithmetic.
    // Binding another cursor would fabricate recovery of source custody.
    internal void BindReceiveEquipmentRandom(VanillaUnifiedRandom1458 random, bool windowsArithmetic = false)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (receiveEquipmentRandom is not null &&
            (!ReferenceEquals(receiveEquipmentRandom, random) || receiveWindowsArithmetic != windowsArithmetic))
        {
            throw new InvalidOperationException("Cannot replace the owned receive cursor.");
        }
        receiveEquipmentRandom = random;
        receiveWindowsArithmetic = windowsArithmetic;
    }

    internal bool TryPrepareReceivedEquipment(PlayerEquipmentRuntimeCommand command,
        out ReceivedEquipmentPreparation? preparation)
    {
        preparation = null;
        var request = command.Request;
        if (receiveEquipmentRandom is not { } random ||
            !command.Connection.IsAssigned || request.PlayerSlot != command.Connection.Player.Slot ||
            !VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId) ||
            !request.TryGetCanonicalItemType(out var type) ||
            !VanillaPrefixIds.TryCreate(request.Prefix, out _) ||
            !inventory.CanAccept(command.Connection) || inventory.Serial >= ulong.MaxValue - 1)
        {
            return false;
        }
        if (!VanillaItemPrefixNormalization1458.IsSupported(type, new PrefixId(request.Prefix)))
        {
            return false;
        }
        membership.TryGet(request.PlayerSlot, out var member);
        if (member is not null && (member.Connection != command.Connection ||
            member.Revision == ulong.MaxValue || member.ProjectileUseInputRevision == ulong.MaxValue))
        {
            return false;
        }
        bool inventorySlot = VanillaPlayerItemSlotCatalog.IsInventorySlot(request.SlotId);
        if (inventorySlot && !RuntimePlayerInventoryItem.TryFromNormalized(in request, out _))
        {
            return false;
        }
        var before = random.Clone();
        var after = before.Clone();
        var status = VanillaItemPrefixNormalization1458.Resolve(type, new PrefixId(request.Prefix), after.Next,
            receiveWindowsArithmetic, out var resolution);
        // Exhaustion of the selective attempt budget discards the clone. Once admitted,
        // dispatch refuses this receive rather than falling back to unnormalized adoption.
        if (status != VanillaItemPrefixNormalizationStatus1458.Resolved)
        {
            return false;
        }
        byte normalized = checked((byte)resolution.Prefix.Value);
        preparation = new(this, member, command.Connection, request with { Prefix = normalized }, random, before, after, inventorySlot);
        return true;
    }

    internal sealed class ReceivedEquipmentPreparation
    {
        private readonly PlayerAuthority owner;
        private readonly RuntimePlayerMember? member;
        private readonly ConnectionHandle connection;
        private readonly PlayerStateSnapshot? beforePlayer;
        private readonly RuntimePlayerItemPhase1458? beforePhase;
        private readonly ulong beforeInput, beforeSerial, beforeMembership;
        private readonly bool beforeProfile;
        private readonly PlayerAppearanceCommitRequest? beforeAppearance;
        private readonly PlayerEquipmentCommitRequest[] beforeEquipment;
        private readonly BuffTypeId[]? beforeBuffs;
        private readonly PlayerEquipmentCommitRequest[] afterEquipment;
        private readonly VanillaUnifiedRandom1458 random, before, after;
        private readonly bool inventorySlot;
        private PlayerStateSnapshot? acceptedPlayer;
        private RuntimePlayerItemPhase1458? acceptedPhase;
        private ulong acceptedInput, acceptedSerial;
        private bool adopted, published;
        internal PlayerEquipmentCommitRequest Request { get; }

        internal ReceivedEquipmentPreparation(PlayerAuthority owner, RuntimePlayerMember? member,
            ConnectionHandle connection, PlayerEquipmentCommitRequest request,
            VanillaUnifiedRandom1458 random, VanillaUnifiedRandom1458 before,
            VanillaUnifiedRandom1458 after, bool inventorySlot)
        {
            this.owner = owner;
            this.member = member;
            this.connection = connection;
            Request = request;
            this.random = random;
            this.before = before;
            this.after = after;
            this.inventorySlot = inventorySlot;
            beforePlayer = member?.CaptureSnapshot();
            beforePhase = member?.ItemPhase;
            beforeInput = member?.ProjectileUseInputRevision ?? 0;
            beforeSerial = owner.inventory.Serial;
            beforeMembership = owner.membership.Serial;
            beforeProfile = owner.transferProfiles.TryCapture(connection, out beforeAppearance, out beforeEquipment, out beforeBuffs);
            afterEquipment = inventorySlot ? beforeEquipment : beforeEquipment.Where(e => e.SlotId != request.SlotId)
                .Concat(request.Stack > 0 ? new[] { request } : Array.Empty<PlayerEquipmentCommitRequest>()).OrderBy(e => e.SlotId).ToArray();
        }

        private bool SameMember(PlayerStateSnapshot? snapshot, RuntimePlayerItemPhase1458? phase, ulong input)
        {
            owner.membership.TryGet(connection.Player.Slot, out var current);
            return ReferenceEquals(current, member) && current?.CaptureSnapshot() == snapshot &&
                current?.ItemPhase == phase && (current?.ProjectileUseInputRevision ?? 0) == input;
        }

        private bool ProfileCurrent(bool accepted)
        {
            bool profile = owner.transferProfiles.TryCapture(connection, out var appearance, out var equipment, out var buffs);
            return profile == (accepted && !inventorySlot ? true : beforeProfile) && appearance == beforeAppearance &&
                equipment.AsSpan().SequenceEqual(accepted ? afterEquipment : beforeEquipment) &&
                (buffs is null) == (beforeBuffs is null) && (buffs is null || buffs.AsSpan().SequenceEqual(beforeBuffs));
        }

        internal bool IsCurrent => !adopted && ReferenceEquals(owner.receiveEquipmentRandom, random) &&
            random.HasSameState(before) && owner.membership.Serial == beforeMembership &&
            SameMember(beforePlayer, beforePhase, beforeInput) && ProfileCurrent(false) &&
            owner.inventory.Serial == beforeSerial && owner.inventory.CanAccept(connection) &&
            owner.inventory.Serial < ulong.MaxValue - 1;

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent)
            {
                return false;
            }
            // Concrete single-writer tail: all members/items/profiles/cursor/counters
            // are adopted before invoking the existing equipment observer.
            if (member is not null && !member.TryAdvanceRevision())
            {
                throw new InvalidOperationException("Admitted member changed.");
            }
            var request = Request;
            bool written = inventorySlot
                ? owner.inventory.TrySet(connection, in request)
                : owner.transferProfiles.TrySetEquipment(connection, in request);
            if (!written)
            {
                throw new InvalidOperationException("Admitted item owner changed.");
            }
            random.CopyStateFrom(after);
            owner.AppliedEquipmentUpdates++;
            acceptedPlayer = member?.CaptureSnapshot();
            acceptedPhase = member?.ItemPhase;
            acceptedInput = member?.ProjectileUseInputRevision ?? 0;
            acceptedSerial = owner.inventory.Serial;
            adopted = true;
            return true;
        }

        internal bool TryPublish()
        {
            if (!adopted || published || !ReferenceEquals(owner.receiveEquipmentRandom, random) ||
                !SameMember(acceptedPlayer, acceptedPhase, acceptedInput) ||
                owner.membership.Serial != beforeMembership || !ProfileCurrent(true) ||
                !random.HasSameState(after) || owner.inventory.Serial != acceptedSerial)
            {
                return false;
            }
            var request = Request;
            published = true;
            owner.events?.PlayerEquipmentUpdated(connection, in request);
            return true;
        }
    }
}
