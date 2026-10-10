using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private VanillaUnifiedRandom1458? receiveEquipmentRandom;
    private bool receiveWindowsArithmetic;
    private VanillaItemPrefixWorld1458? receivePrefixWorld;

    // Terraria 1.4.5.8 MessageBuffer.GetData5 calls Item.Prefix on Main.rand.
    // Composition supplies its existing shared gameplay cursor and explicit arithmetic.
    // The cursor, source arithmetic and nullable world context bind once together.
    // Replacing them would fabricate recovery of source custody.
    internal void BindReceiveEquipmentRandom(VanillaUnifiedRandom1458 random, bool windowsArithmetic = false,
        VanillaItemPrefixWorld1458? world = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (receiveEquipmentRandom is not null &&
            (!ReferenceEquals(receiveEquipmentRandom, random) || receiveWindowsArithmetic != windowsArithmetic || receivePrefixWorld != world))
        {
            throw new InvalidOperationException("Cannot replace the owned receive cursor, arithmetic or world context.");
        }
        receiveEquipmentRandom = random;
        receiveWindowsArithmetic = windowsArithmetic;
        receivePrefixWorld = world;
    }

    // Only the bound server-relay receive path adopts SetDefaults identity. Existing ingress
    // stack/prefix clipping and signed net-id compatibility remain separate policies.
    private static bool TryResolveRetainedReceiveIdentity(in PlayerEquipmentCommitRequest original,
        out PlayerEquipmentCommitRequest accepted, out ItemTypeId type)
    {
        accepted = original;
        if (!original.TryGetCanonicalItemType(out var requested) ||
            !VanillaItemRetainedIdentity1458.TryResolve(requested, out type))
        {
            type = default;
            return false;
        }
        accepted = type.IsNone
            ? original with { ItemNetId = 0, Stack = 0, Prefix = 0, ItemFlags = 0 }
            : original with { ItemNetId = checked((short)type.Value) };
        return true;
    }

    internal bool TryPrepareReceivedEquipment(PlayerEquipmentRuntimeCommand command,
        out ReceivedEquipmentPreparation? preparation)
    {
        preparation = null;
        var request = command.Request;
        if (receiveEquipmentRandom is not { } random ||
            !command.Connection.IsAssigned || request.PlayerSlot != command.Connection.Player.Slot ||
            !VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId) ||
            !TryResolveRetainedReceiveIdentity(in request, out request, out var type) ||
            !VanillaPrefixIds.TryCreate(request.Prefix, out _) ||
            !inventory.CanAccept(command.Connection) || inventory.Serial >= ulong.MaxValue - 1)
        {
            return false;
        }
        if (!VanillaItemPrefixNormalization1458.IsSupported(type, new PrefixId(request.Prefix), receivePrefixWorld))
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
            receiveWindowsArithmetic, out var resolution, receivePrefixWorld);
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
        private readonly VanillaItemPrefixWorld1458? prefixWorld;
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
            prefixWorld = owner.receivePrefixWorld;
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

        internal bool IsCurrent => !adopted && owner.receivePrefixWorld == prefixWorld && ReferenceEquals(owner.receiveEquipmentRandom, random) &&
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
            if (!adopted || published || owner.receivePrefixWorld != prefixWorld || !ReferenceEquals(owner.receiveEquipmentRandom, random) ||
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
